using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PhotoViewer;

public partial class MainWindow : Window
{
    private readonly FolderNavigator _nav = new();
    private readonly ImagePreloader _preloader = new();
    private readonly AppSettings _settings;
    private readonly System.Windows.Threading.DispatcherTimer _autoPlayTimer;
    private int _loadGeneration;      // guards against out-of-order async navigation
    private bool _updatingZoomUi;     // suppress combo events while we set the text in code
    private BitmapSource? _source;
    private double _zoom = 1.0;
    private double _fitZoom = 1.0;
    private bool _isFit = true;       // fit-to-window when a new image loads
    private double _rotation;         // accumulated degrees (0/90/180/270)
    private Point _pan;               // pan offset in device pixels (applied via PanTransform)
    private Point _dragOrigin;
    private Point _panOrigin;
    private bool _isDragging;

    /// <summary>Port the Debug HTTP control service listens on (Debug builds only).</summary>
    public int DebugPort => _settings.DebugHttpPort;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        IntervalBox.Text = _settings.AutoPlayIntervalSeconds.ToString("0.##");

        _autoPlayTimer = new System.Windows.Threading.DispatcherTimer();
        _autoPlayTimer.Tick += (_, _) => Next();

        IntervalBox.LostFocus += (_, _) => ApplyIntervalFromBox();
        IntervalBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { ApplyIntervalFromBox(); e.Handled = true; }
        };

        var openBinding = new KeyBinding(
            new RelayCommand(_ => OpenExecuted()),
            Key.O, ModifierKeys.Control);
        InputBindings.Add(openBinding);
    }

    /// <summary>
    /// Fit once the window has actually been rendered. Fixes the case where an
    /// image is opened from the command line before layout has run — without this
    /// FitToWindow sees a zero-size viewport and leaves the image at 100% (cropped).
    /// </summary>
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_isFit) FitToWindow();
    }

    /// <summary>Open a file passed on the command line, if any.</summary>
    public void OpenFromArgs(string[] args)
    {
        if (args is { Length: > 0 } && File.Exists(args[0]))
            LoadFile(args[0]);
    }

    // ---------- Commands ----------
    private void OpenExecuted()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open image",
            Filter = FilterExtensions.OpenFilterString(),
            Multiselect = false
        };
        if (dlg.ShowDialog(this) == true)
            LoadFile(dlg.FileName);
    }

    // ---------- Loading ----------
    private void LoadFile(string path)
    {
        try
        {
            var bmp = ImageDecoder.Decode(path);
            _nav.LoadFolder(path);
            _preloader.Clear();           // new folder — drop stale neighbor decodes
            SetImage(bmp);
            HintText.Visibility = Visibility.Collapsed;
            UpdateStatus();
            PreloadNeighbors();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Could not open:\n{path}\n\n{ex.Message}",
                "Photo Viewer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetImage(BitmapSource bmp)
    {
        _source = bmp;
        _rotation = 0;
        _pan = new Point(0, 0);
        // The Image element stays at the ORIGINAL pixel size and never grows with zoom;
        // zooming is done via ScaleTransform (clipped before rasterization), which avoids
        // WPF's unreliable rendering of giant layout elements when panned at high zoom.
        ImageHost.Width = bmp.PixelWidth;
        ImageHost.Height = bmp.PixelHeight;
        RotateTransform.Angle = 0;
        PanTransform.X = 0;
        PanTransform.Y = 0;
        ZoomTransform.ScaleX = 1;
        ZoomTransform.ScaleY = 1;
        ImageHost.Source = bmp;
        // Recompute fit after layout has the new intrinsic size.
        Dispatcher.BeginInvoke(() => FitToWindow(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Show a sibling image. Uses the preloaded decode when available (instant); otherwise
    /// decodes on a background thread so the UI stays responsive. A generation counter
    /// ensures rapid arrow presses can't apply a stale result out of order.
    /// </summary>
    private async void ShowSibling(string? path)
    {
        if (path is null) return;
        int generation = ++_loadGeneration;
        try
        {
            BitmapSource bmp;
            if (_preloader.TryGetCached(path, out var cached) && cached is not null)
            {
                bmp = cached;
            }
            else
            {
                bmp = await _preloader.GetOrDecodeAsync(path);
                if (generation != _loadGeneration) return;   // user moved on; drop stale
            }

            SetImage(bmp);
            UpdateStatus();
            PreloadNeighbors();
        }
        catch (Exception ex)
        {
            if (generation != _loadGeneration) return;
            MessageBox.Show(this, $"Could not open:\n{path}\n\n{ex.Message}",
                "Photo Viewer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Preload the images most likely to be navigated to next (forward-biased, since
    /// paging forward is the common case). Keeps at most 4 full-res decodes in memory.
    /// </summary>
    private void PreloadNeighbors()
    {
        if (_nav.Count <= 1) return;
        _preloader.PreloadAround(_nav.Files, _nav.Index, forward: 2, backward: 1);
    }

    // ---------- Fit / zoom ----------
    private void FitToWindow()
    {
        if (_source is null || Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0) return;

        bool rotated = Math.Abs(_rotation % 180) > 0.01;
        double iw = rotated ? _source.PixelHeight : _source.PixelWidth;
        double ih = rotated ? _source.PixelWidth : _source.PixelHeight;

        double scaleX = Viewport.ActualWidth / iw;
        double scaleY = Viewport.ActualHeight / ih;
        _fitZoom = Math.Min(scaleX, scaleY);
        _fitZoom = Math.Min(_fitZoom, 1.0); // never upscale beyond 100% on fit

        // Reset pan so the fitted image is centered.
        _pan = new Point(0, 0);
        PanTransform.X = 0;
        PanTransform.Y = 0;
        SetZoom(_fitZoom);
        _isFit = true;
    }

    /// <summary>
    /// Zoom is a ScaleTransform (about the image center) on an element kept at the original
    /// pixel size. Because render transforms are clipped before rasterization, only the
    /// visible viewport region is drawn — so high zoom + pan stays correct and fast.
    /// </summary>
    private void ApplyTransforms()
    {
        ZoomTransform.ScaleX = _zoom;
        ZoomTransform.ScaleY = _zoom;
        PanTransform.X = _pan.X;
        PanTransform.Y = _pan.Y;
        RotateTransform.Angle = _rotation;
    }

    private void SetZoom(double value)
    {
        _zoom = Math.Clamp(value, 0.02, 40.0);
        ApplyTransforms();
        string percent = $"{Math.Round(_zoom * 100)}%";
        ZoomText.Text = percent;
        // Keep the toolbar combo in sync without re-triggering its handlers.
        _updatingZoomUi = true;
        ZoomBox.Text = percent;
        _updatingZoomUi = false;
    }

    private void ZoomAt(double factor, Point p)
    {
        double old = _zoom;
        double newZoom = Math.Clamp(old * factor, 0.02, 40.0);
        if (Math.Abs(newZoom - old) < 1e-9) return;

        // Keep the image point under the cursor fixed while scaling. With the image
        // centered (and rotation applied about its center), the rotation cancels:
        //   newPan = d - k*(d - pan),  where d = p - viewportCenter, k = newZoom/old.
        double cx = Viewport.ActualWidth / 2;
        double cy = Viewport.ActualHeight / 2;
        double dx = p.X - cx;
        double dy = p.Y - cy;
        double k = newZoom / old;

        _pan = new Point(dx - k * (dx - _pan.X), dy - k * (dy - _pan.Y));
        PanTransform.X = _pan.X;
        PanTransform.Y = _pan.Y;
        SetZoom(newZoom);
        _isFit = false;
    }

    // ---------- Navigation ----------
    private void Next() => ShowSibling(_nav.Next());
    private void Previous() => ShowSibling(_nav.Previous());

    private void UpdateStatus()
    {
        string name = _nav.Current is not null ? Path.GetFileName(_nav.Current) : "(none)";
        string pos = _nav.Count > 0 ? $"  |  {_nav.Index + 1} / {_nav.Count}" : "";
        string dims = _source is not null ? $"  |  {_source.PixelWidth} × {_source.PixelHeight}" : "";
        StatusText.Text = $"{name}{pos}{dims}";
        Title = $"Photo Viewer — {name}";
    }

    // ---------- Rotation ----------
    private void Rotate(double degrees)
    {
        if (_source is null) return;
        _rotation = (_rotation + degrees) % 360;
        RotateTransform.Angle = _rotation;
        if (_isFit) FitToWindow();
    }

    // ---------- Toolbar: navigation + auto-play ----------
    private void PrevButton_Click(object sender, RoutedEventArgs e) => NavigatePrevious();
    private void NextButton_Click(object sender, RoutedEventArgs e) => NavigateNext();

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsAutoPlaying) StopAutoPlay(); else StartAutoPlay();
    }

    /// <summary>Parse the interval box and persist it; restart the timer if running.</summary>
    private void ApplyIntervalFromBox()
    {
        if (double.TryParse(IntervalBox.Text.Trim(), out double seconds) && seconds > 0)
        {
            _settings.AutoPlayIntervalSeconds = seconds;
            _settings.Save();
            if (IsAutoPlaying)
                _autoPlayTimer.Interval = TimeSpan.FromSeconds(_settings.IntervalSecondsClamped);
        }
        else
        {
            // Restore the box to the last valid value.
            IntervalBox.Text = _settings.AutoPlayIntervalSeconds.ToString("0.##");
        }
    }

    public bool IsAutoPlaying => _autoPlayTimer.IsEnabled;

    /// <summary>Current auto-play interval in seconds (as persisted/clamped).</summary>
    public double AutoPlayIntervalSeconds => _settings.IntervalSecondsClamped;

    /// <summary>Start auto-play, optionally overriding the interval in seconds.</summary>
    public void StartAutoPlay(double? intervalSeconds = null)
    {
        if (intervalSeconds is double s && s > 0)
        {
            _settings.AutoPlayIntervalSeconds = s;
            IntervalBox.Text = s.ToString("0.##");
            _settings.Save();
        }
        _autoPlayTimer.Interval = TimeSpan.FromSeconds(_settings.IntervalSecondsClamped);
        _autoPlayTimer.Start();
        PlayButton.Content = "\u23F8  Pause";
    }

    public void StopAutoPlay()
    {
        _autoPlayTimer.Stop();
        PlayButton.Content = "\u25B6  Play";
    }

    // ---------- Toolbar: zoom ----------
    private Point ViewportCenter() => new(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2);

    private void ZoomInButton_Click(object sender, RoutedEventArgs e) => ZoomStep(1.25);
    private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => ZoomStep(1 / 1.25);
    private void FitButton_Click(object sender, RoutedEventArgs e) => FitWindow();
    private void OriginalButton_Click(object sender, RoutedEventArgs e) => ShowOriginal();
    private void ResetButton_Click(object sender, RoutedEventArgs e) => ResetView();

    private void ZoomBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyZoomFromBox();
            e.Handled = true;
        }
    }

    private void ZoomBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_updatingZoomUi) return;
        ApplyZoomFromBox();
    }

    /// <summary>Apply the zoom percentage typed/chosen in the combo, anchored at the viewport center.</summary>
    private void ApplyZoomFromBox()
    {
        string text = ZoomBox.Text.Trim().TrimEnd('%').Trim();
        if (double.TryParse(text, out double percent) && percent > 0)
            ZoomToScale(percent / 100.0);
        else
            SetZoom(_zoom);   // restore current value in the box
    }

    // ---------- Public command API (used by toolbar and the Debug HTTP service) ----------
    public void NavigateNext() => Next();
    public void NavigatePrevious() => Previous();

    /// <summary>Zoom to an absolute scale (1.0 = original pixels), anchored at the viewport center.</summary>
    public void ZoomToScale(double scale)
    {
        double target = Math.Clamp(scale, 0.02, 40.0);
        ZoomAt(target / Math.Max(_zoom, 1e-9), ViewportCenter());
    }

    /// <summary>Zoom by a multiplicative step (e.g. 1.25 to zoom in), anchored at the viewport center.</summary>
    public void ZoomStep(double factor) => ZoomAt(factor, ViewportCenter());

    /// <summary>Pan the image by a relative offset in device pixels (positive dx = right, dy = down).</summary>
    public void PanBy(double dx, double dy)
    {
        _pan = new Point(_pan.X + dx, _pan.Y + dy);
        PanTransform.X = _pan.X;
        PanTransform.Y = _pan.Y;
        _isFit = false;
    }

    /// <summary>Fit the image to the window (never above 100%).</summary>
    public void FitWindow() => FitToWindow();

    /// <summary>Show the image at original size (100%), centered.</summary>
    public void ShowOriginal()
    {
        _pan = new Point(0, 0);
        PanTransform.X = 0;
        PanTransform.Y = 0;
        SetZoom(1.0);
        _isFit = false;
    }

    /// <summary>Reset position, size and rotation back to the freshly-opened (fit) state.</summary>
    public void ResetView()
    {
        _rotation = 0;
        RotateTransform.Angle = 0;
        FitToWindow();
    }

    /// <summary>Rotate by a relative number of degrees.</summary>
    public void RotateBy(double degrees) => Rotate(degrees);

    /// <summary>Test hook: open/close the zoom drop-down programmatically.</summary>
    public void SetZoomDropdownOpen(bool open) => ZoomBox.IsDropDownOpen = open;

    /// <summary>
    /// Snapshot of image + UI geometry for the Debug HTTP service and tests.
    /// Must be called on the UI thread.
    /// </summary>
    public object GetDebugInfo()
    {
        return new
        {
            file = _nav.Current,
            index = _nav.Index,
            count = _nav.Count,
            image = _source is null
                ? null
                : new { pixelWidth = _source.PixelWidth, pixelHeight = _source.PixelHeight },
            viewport = new { width = Viewport.ActualWidth, height = Viewport.ActualHeight },
            // Intended display size (pixel size x zoom). Reported directly rather than
            // ImageHost.ActualWidth, which lags because WPF layout is deferred and would
            // otherwise return a stale value when read synchronously right after a zoom.
            imageElement = _source is null
                ? null
                : new { width = _source.PixelWidth * _zoom, height = _source.PixelHeight * _zoom },
            zoom = _zoom,
            zoomPercent = Math.Round(_zoom * 100, 2),
            fitZoom = _fitZoom,
            rotation = _rotation,
            pan = new { x = _pan.X, y = _pan.Y },
            isAutoPlaying = IsAutoPlaying,
            autoPlayIntervalSeconds = AutoPlayIntervalSeconds,
            zoomDropdownOpen = ZoomBox.IsDropDownOpen,
            zoomItems = ZoomBox.Items.Count
        };
    }

    // ---------- Events ----------
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Right || e.Key == Key.PageDown) { Next(); e.Handled = true; }
        else if (e.Key == Key.Left || e.Key == Key.PageUp) { Previous(); e.Handled = true; }
        else if (e.Key == Key.Home) { Rotate(-90); e.Handled = true; }
        else if (e.Key == Key.End) { Rotate(90); e.Handled = true; }
        else if (e.Key == Key.F) { FitToWindow(); e.Handled = true; }
        else if (e.Key == Key.D1) { SetZoom(1.0); _isFit = false; e.Handled = true; }
        else if (e.Key == Key.OemPlus || e.Key == Key.Add) { SetZoom(_zoom * 1.25); _isFit = false; e.Handled = true; }
        else if (e.Key == Key.OemMinus || e.Key == Key.Subtract) { SetZoom(_zoom / 1.25); _isFit = false; e.Handled = true; }
        else if (e.Key == Key.Space && Keyboard.FocusedElement is not TextBox && Keyboard.FocusedElement is not ComboBox)
        { if (IsAutoPlaying) StopAutoPlay(); else StartAutoPlay(); e.Handled = true; }
    }

    private void ImageHost_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_source is null) return;
        double factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        ZoomAt(factor, e.GetPosition(Viewport));
    }

    private void ImageHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragOrigin = e.GetPosition(this);
        _panOrigin = _pan;
        ImageHost.CaptureMouse();
    }

    private void ImageHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        ImageHost.ReleaseMouseCapture();
    }

    private void ImageHost_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var pos = e.GetPosition(this);
        _pan = new Point(_panOrigin.X + (pos.X - _dragOrigin.X),
                         _panOrigin.Y + (pos.Y - _dragOrigin.Y));
        PanTransform.X = _pan.X;
        PanTransform.Y = _pan.Y;
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isFit) FitToWindow();
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            if (files.Length > 0 && File.Exists(files[0]))
                LoadFile(files[0]);
        }
    }
}

public static class FilterExtensions
{
    public static string OpenFilterString()
    {
        return string.Join("|",
            "All supported images|*.jpg;*.jpeg;*.jpe;*.jfif;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.ico;*.wdp;*.jxr;*.hdp;*.heic;*.heif;*.avif;*.hif",
            "HEIF/HEIC/AVIF|*.heic;*.heif;*.avif;*.hif",
            "JPEG|*.jpg;*.jpeg",
            "PNG|*.png",
            "BMP|*.bmp",
            "GIF|*.gif",
            "TIFF|*.tif;*.tiff",
            "WebP|*.webp",
            "All files|*.*");
    }
}
