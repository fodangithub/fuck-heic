using System.Windows;

namespace PhotoViewer;

public partial class App : Application
{
#if DEBUG
    private DebugHttpService? _debugHttp;
#endif

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;          // required for ShutdownMode=OnMainWindowClose
        window.OpenFromArgs(e.Args);
        window.Show();

#if DEBUG
        // Local-only HTTP control service so automated tests can drive the UI.
        _debugHttp = new DebugHttpService(window, window.DebugPort);
        _debugHttp.Start();
        System.Diagnostics.Debug.WriteLine(
            $"[PhotoViewer] Debug HTTP control on http://127.0.0.1:{window.DebugPort}/ (GET /info)");
#endif
    }

    protected override void OnExit(ExitEventArgs e)
    {
#if DEBUG
        _debugHttp?.Dispose();
#endif
        base.OnExit(e);
    }
}
