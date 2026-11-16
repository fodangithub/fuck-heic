using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PhotoViewer;

public partial class MainWindow : Window
{
    private readonly FolderNavigator _nav = new();
    private BitmapSource? _source;
    private double _zoom = 1.0;
    private double _fitZoom = 1.0;
    private bool _isFit = true;       // fit-to-window when a new image loads
    private double _rotation;         // accumulated degrees (0/90/180/270)
    private Point _pan;               // pan offset in device pixels (applied via PanTransform)
    private Point _dragOrigin;
    private Point _panOrigin;
    private bool _isDragging;

    public MainWindow()
    {
        InitializeComponent();

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
            SetImage(bmp);
            HintText.Visibility = Visibility.Collapsed;
            UpdateStatus();
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
        RotateTransform.Angle = 0;
        PanTransform.X = 0;
        PanTransform.Y = 0;
        ImageHost.Source = bmp;
        // Recompute fit after layout has the new intrinsic size.
        Dispatcher.BeginInvoke(() => FitToWindow(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ShowSibling(string? path)
    {
        if (path is null) return;
        try
        {
            SetImage(ImageDecoder.Decode(path));
            UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open:\n{path}\n\n{ex.Message}",
                "Photo Viewer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

    private void SetZoom(double value)
    {
        _zoom = Math.Clamp(value, 0.02, 40.0);
        ZoomTransform.ScaleX = _zoom;
        ZoomTransform.ScaleY = _zoom;
        ZoomText.Text = $"{Math.Round(_zoom * 100)}%";
    }

    private void ZoomAt(double factor, Point p)
    {
        double old = _zoom;
        double newZoom = Math.Clamp(old * factor, 0.02, 40.0);
        if (Math.Abs(newZoom - old) < 1e-9) return;

        // Keep the image point under the cursor fixed while scaling. With the
        // transform origin at the image center, the rotation cancels out:
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
