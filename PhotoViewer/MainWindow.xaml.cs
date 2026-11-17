using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
    private double _flipX = 1.0;      // -1 when horizontally mirrored
    private double _flipY = 1.0;      // -1 when vertically mirrored
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
        UpdateImageContextMenu();
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

    /// <summary>Open a file programmatically (Debug HTTP service / tests). Same path as Ctrl+O and drag-drop.</summary>
    public void OpenPath(string path) => LoadFile(path);

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
        _flipX = 1.0;
        _flipY = 1.0;
        _pan = new Point(0, 0);
        // Reset both the zoom model and its transform. A new image must never inherit
        // the previous photo's zoom, even for a single frame.
        _zoom = 1.0;
        _fitZoom = 1.0;
        ApplyBaseSize();
        ApplyTransforms();
        ImageHost.Source = bmp;

        if (Viewport.ActualWidth > 0 && Viewport.ActualHeight > 0)
        {
            // Viewport already measured (opened after startup): fit synchronously so
            // the image is never displayed at 100% or a stale zoom.
            FitToWindow();
        }
        else
        {
            // Startup before the first layout: keep the image hidden until the fit has
            // been applied, so no un-fitted frame is ever shown.
            _isFit = true;
            ImageHost.Visibility = Visibility.Hidden;
            Dispatcher.BeginInvoke(new Action(FitToWindow),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        UpdateImageContextMenu();
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
        if (_source is null) return;

        if (Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0)
        {
            // Layout hasn't run yet (startup). Retry once it has instead of leaving
            // the image at 100%, which would show only a small middle portion of a
            // large photo.
            if (IsVisible)
                Dispatcher.BeginInvoke(new Action(FitToWindow),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }

        bool rotated = Math.Abs(_rotation % 180) > 0.01;
        double iw = rotated ? _source.PixelHeight : _source.PixelWidth;
        double ih = rotated ? _source.PixelWidth : _source.PixelHeight;

        double scaleX = Viewport.ActualWidth / iw;
        double scaleY = Viewport.ActualHeight / ih;
        _fitZoom = Math.Min(scaleX, scaleY);
        _fitZoom = Math.Min(_fitZoom, 1.0); // never upscale beyond 100% on fit

        // Keep the layout element at the fitted size. A WPF element larger than its
        // parent is clipped BEFORE its RenderTransform, so scaling down an
        // original-size Image would render only its center. Zooming is relative to
        // this fitted baseline instead.
        ApplyBaseSize();

        // Reset pan so the fitted image is centered.
        _pan = new Point(0, 0);
        PanTransform.X = 0;
        PanTransform.Y = 0;
        SetZoom(_fitZoom);
        _isFit = true;
        // The image may have been hidden while waiting for the first fit (startup).
        ImageHost.Visibility = Visibility.Visible;
        UpdateImageContextMenu();
    }

    /// <summary>Size the layout element to the fitted image, before any zoom transform.</summary>
    private void ApplyBaseSize()
    {
        if (_source is null) return;
        double fit = Math.Max(_fitZoom, 1e-3);
        ImageHost.Width = _source.PixelWidth * fit;
        ImageHost.Height = _source.PixelHeight * fit;
    }

    /// <summary>
    /// Zoom is a RenderTransform relative to the fitted layout size. Keeping the layout
    /// element small avoids both WPF's pre-transform clipping at fit zoom and its
    /// oversized-rasterization clipping at high zoom + pan.
    /// </summary>
    private void ApplyTransforms()
    {
        double relativeZoom = _zoom / Math.Max(_fitZoom, 1e-3);
        ZoomTransform.ScaleX = relativeZoom;
        ZoomTransform.ScaleY = relativeZoom;
        FlipTransform.ScaleX = _flipX;
        FlipTransform.ScaleY = _flipY;
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
        _flipX = 1.0;
        _flipY = 1.0;
        RotateTransform.Angle = 0;
        FitToWindow();
    }

    /// <summary>Rotate by a relative number of degrees.</summary>
    public void RotateBy(double degrees) => Rotate(degrees);

    /// <summary>Test hook: open/close the zoom drop-down programmatically.</summary>
    public void SetZoomDropdownOpen(bool open) => ZoomBox.IsDropDownOpen = open;

    /// <summary>Test hook: open the image context menu centered on the viewport.</summary>
    public void SetImageContextMenuOpen(bool open)
    {
        UpdateImageContextMenu();
        if (open)
        {
            ImageContextMenu.PlacementTarget = Viewport;
            ImageContextMenu.Placement = PlacementMode.Center;
            ImageContextMenu.HorizontalOffset = 0;
            ImageContextMenu.VerticalOffset = 0;
            ImageContextMenu.IsOpen = true;
            ImageContextMenu.UpdateLayout();
        }
        else
        {
            ImageContextMenu.IsOpen = false;
            ImageContextMenu.PlacementTarget = ImageHost;
            ImageContextMenu.Placement = PlacementMode.MousePoint;
        }
    }

    // ---------- Image context menu ----------
    public void RotateLeft() => RotateBy(-90);
    public void RotateRight() => RotateBy(90);

    public void FlipHorizontal()
    {
        if (_source is null) return;
        _flipX *= -1;
        ApplyTransforms();
    }

    public void FlipVertical()
    {
        if (_source is null) return;
        _flipY *= -1;
        ApplyTransforms();
    }

    /// <summary>Debug-only snapshot of the context-menu structure for regression tests.</summary>
    public object GetImageContextMenuInfo()
    {
        UpdateImageContextMenu();
        var items = new List<object>();
        foreach (object entry in ImageContextMenu.Items)
        {
            if (entry is Separator)
            {
                items.Add(new { type = "separator" });
                continue;
            }
            if (entry is not MenuItem menuItem) continue;

            var iconElement = menuItem.Icon as FrameworkElement;
            bool spacer = (iconElement?.Tag as string) == "spacer";
            string? icon = null;
            if (!spacer && menuItem.Icon is System.Windows.Controls.Image image)
                icon = image.Source?.ToString();

            items.Add(new
            {
                type = "item",
                name = menuItem.Name,
                header = menuItem.Header?.ToString(),
                enabled = menuItem.IsEnabled,
                hasIcon = !spacer && menuItem.Icon is not null,
                icon
            });
        }
        return new { itemCount = items.Count, items };
    }

    /// <summary>Raise a context-menu item's Click event by its x:Name (Debug tests only).</summary>
    public bool InvokeImageContextMenuItem(string name)
    {
        UpdateImageContextMenu();
        var item = ImageContextMenu.Items.OfType<MenuItem>()
            .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        if (item is null || !item.IsEnabled) return false;
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        return true;
    }

    private void UpdateImageContextMenu()
    {
        bool hasImage = _source is not null;
        string? path = _nav.Current;
        bool hasFile = !string.IsNullOrEmpty(path) && File.Exists(path);

        ImageInfoMenuItem.Header = hasImage
            ? $"{_source!.PixelWidth} × {_source.PixelHeight} px  •  Scale {Math.Round(_zoom * 100)}%"
            : "No image";
        ImageInfoMenuItem.IsEnabled = false;

        CopyImageMenuItem.IsEnabled = hasImage;
        CopyPathMenuItem.IsEnabled = hasFile;
        OpenFolderMenuItem.IsEnabled = hasFile;
        RotateLeftMenuItem.IsEnabled = hasImage;
        RotateRightMenuItem.IsEnabled = hasImage;
        FlipHorizontalMenuItem.IsEnabled = hasImage;
        FlipVerticalMenuItem.IsEnabled = hasImage;
        PrintMenuItem.IsEnabled = hasImage;
        PropertiesMenuItem.IsEnabled = hasFile;
    }

    private void ImageContextMenu_Opened(object sender, RoutedEventArgs e) => UpdateImageContextMenu();

    private void CopyImage_Click(object sender, RoutedEventArgs e) => CopyImageToClipboard();
    private void CopyPath_Click(object sender, RoutedEventArgs e) => CopyImagePathToClipboard();
    private void OpenFolder_Click(object sender, RoutedEventArgs e) => OpenContainingFolder();
    private void RotateLeft_Click(object sender, RoutedEventArgs e) => RotateLeft();
    private void RotateRight_Click(object sender, RoutedEventArgs e) => RotateRight();
    private void FlipHorizontal_Click(object sender, RoutedEventArgs e) => FlipHorizontal();
    private void FlipVertical_Click(object sender, RoutedEventArgs e) => FlipVertical();
    private void Print_Click(object sender, RoutedEventArgs e) => PrintImage();
    private void Properties_Click(object sender, RoutedEventArgs e) => ShowFileProperties();

    private void CopyImageToClipboard()
    {
        if (_source is null) return;
        try
        {
            Clipboard.Clear();
            Clipboard.SetImage(_source);
        }
        catch (Exception ex) { ShowActionError("copy the image", ex); }
    }

    private void CopyImagePathToClipboard()
    {
        string? path = _nav.Current;
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            Clipboard.Clear();
            Clipboard.SetText(Path.GetFullPath(path));
        }
        catch (Exception ex) { ShowActionError("copy the image path", ex); }
    }

    private void OpenContainingFolder()
    {
        string? path = _nav.Current;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true,
                Arguments = $"/select,\"{path}\""
            });
        }
        catch (Exception ex) { ShowActionError("open the containing folder", ex); }
    }

    private void PrintImage()
    {
        if (_source is null) return;
        try
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return;

            double pageWidth = dialog.PrintableAreaWidth;
            double pageHeight = dialog.PrintableAreaHeight;
            if (pageWidth <= 0 || pageHeight <= 0) return;

            double scale = Math.Min(pageWidth / _source.PixelWidth, pageHeight / _source.PixelHeight);
            double width = _source.PixelWidth * scale;
            double height = _source.PixelHeight * scale;
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawImage(_source,
                    new Rect((pageWidth - width) / 2, (pageHeight - height) / 2, width, height));
            }

            string name = _nav.Current is null ? "image" : Path.GetFileName(_nav.Current);
            dialog.PrintVisual(visual, $"Photo Viewer — {name}");
        }
        catch (Exception ex) { ShowActionError("print the image", ex); }
    }

    private void ShowFileProperties()
    {
        string? path = _nav.Current;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            // ProcessStartInfo.Verb="properties" depends on a file association, which
            // HEIC often lacks. ShellExecuteEx + SEE_MASK_INVOKEIDLIST asks the Shell's
            // context-menu handler to show the system property sheet directly.
            NativeShell.ShowFileProperties(this, path);
        }
        catch (Exception ex) { ShowActionError("open file properties", ex); }
    }

    private void ShowActionError(string action, Exception ex)
    {
        MessageBox.Show(this, $"Could not {action}:\n\n{ex.Message}",
            "Photo Viewer", MessageBoxButton.OK, MessageBoxImage.Error);
    }

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
            // The untransformed layout element must stay within the viewport; otherwise
            // WPF clips it before the zoom RenderTransform and shows only a center crop.
            imageLayout = new
            {
                width = ImageHost.Width,
                height = ImageHost.Height,
                actualWidth = ImageHost.ActualWidth,
                actualHeight = ImageHost.ActualHeight
            },
            renderScale = ZoomTransform.ScaleX,
            imageVisible = ImageHost.Visibility == Visibility.Visible,
            zoom = _zoom,
            zoomPercent = Math.Round(_zoom * 100, 2),
            fitZoom = _fitZoom,
            rotation = _rotation,
            flipHorizontal = _flipX < 0,
            flipVertical = _flipY < 0,
            contextMenuOpen = ImageContextMenu.IsOpen,
            contextMenuItems = ImageContextMenu.Items.Count,
            pan = new { x = _pan.X, y = _pan.Y },
            isAutoPlaying = IsAutoPlaying,
            autoPlayIntervalSeconds = AutoPlayIntervalSeconds,
            zoomDropdownOpen = ZoomBox.IsDropDownOpen,
            zoomItems = ZoomBox.Items.Count
        };
    }

    /// <summary>
    /// Measure the photo pixels in an offscreen RenderTargetBitmap render of the viewport.
    /// The viewport has a transparent background, so the alpha bounding box captures the
    /// visual tree's transform/clipping geometry. Used by Debug-only regression tests.
    /// </summary>
    public object GetRenderedImageBounds()
    {
        Viewport.UpdateLayout();
        int w = (int)Math.Ceiling(Viewport.ActualWidth);
        int h = (int)Math.Ceiling(Viewport.ActualHeight);
        if (w <= 0 || h <= 0)
            return new { left = 0, top = 0, width = 0, height = 0, viewportWidth = w, viewportHeight = h, coverage = 0.0 };

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(Viewport);
        int stride = w * 4;
        var pixels = new byte[stride * h];
        rtb.CopyPixels(pixels, stride, 0);

        int minX = w, minY = h, maxX = -1, maxY = -1;
        long visiblePixels = 0;
        for (int y = 0; y < h; y++)
        {
            int row = y * stride;
            for (int x = 0; x < w; x++)
            {
                if (pixels[row + x * 4 + 3] <= 8) continue;   // transparent viewport
                visiblePixels++;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        int boundsWidth = maxX < 0 ? 0 : maxX - minX + 1;
        int boundsHeight = maxY < 0 ? 0 : maxY - minY + 1;
        return new
        {
            left = maxX < 0 ? 0 : minX,
            top = maxY < 0 ? 0 : minY,
            width = boundsWidth,
            height = boundsHeight,
            viewportWidth = w,
            viewportHeight = h,
            coverage = (double)visiblePixels / (w * h)
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

internal static class NativeShell
{
    private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    private const int SW_SHOW = 5;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ShellExecuteExW",
        ExactSpelling = true, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

    /// <summary>Open the Windows file property sheet without requiring a file association.</summary>
    public static void ShowFileProperties(Window owner, string path)
    {
        string fullPath = Path.GetFullPath(path);
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = SEE_MASK_INVOKEIDLIST,
            hwnd = new WindowInteropHelper(owner).Handle,
            lpVerb = "properties",
            lpFile = fullPath,
            lpParameters = null,
            lpDirectory = Path.GetDirectoryName(fullPath),
            nShow = SW_SHOW,
            hInstApp = IntPtr.Zero,
            lpIDList = IntPtr.Zero,
            lpClass = null,
            hkeyClass = IntPtr.Zero,
            dwHotKey = 0,
            hIcon = IntPtr.Zero,
            hProcess = IntPtr.Zero
        };

        if (!ShellExecuteEx(ref info))
            throw new InvalidOperationException(
                $"ShellExecuteEx failed with Win32 error {Marshal.GetLastWin32Error()}.");
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
