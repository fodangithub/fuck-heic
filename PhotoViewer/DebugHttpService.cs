#if DEBUG
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoViewer;

/// <summary>
/// Debug-only local HTTP control service. Lets automated tests drive the viewer and read
/// its state over a tiny REST surface on 127.0.0.1. Compiled out of Release builds.
/// Implemented over <see cref="TcpListener"/> (not HttpListener) so it needs no URL-ACL or
/// admin rights and works on any port.
/// </summary>
public sealed class DebugHttpService : IDisposable
{
    private readonly MainWindow _window;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    public int Port { get; }

    public DebugHttpService(MainWindow window, int port)
    {
        _window = window;
        Port = port;
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public void Start()
    {
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { /* already stopped */ }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { break; }
            catch (ObjectDisposedException) { break; }
            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            NetworkStream stream = client.GetStream();
            var request = await ReadRequestAsync(stream, ct);
            if (request is null) return;

            // /screenshot renders the UI to PNG (useful for visual regression checks).
            if (request.Value.path.TrimEnd('/').Equals("/screenshot", StringComparison.OrdinalIgnoreCase))
            {
                byte[] png = await _window.Dispatcher.InvokeAsync(RenderWindowPng).Task;
                await WriteBytesAsync(stream, "image/png", png, ct);
                return;
            }

            string body;
            try
            {
                // All handlers touch WPF state — marshal onto the UI thread.
                body = await _window.Dispatcher.InvokeAsync(
                    () => Route(request.Value.method, request.Value.path, request.Value.query)).Task;
            }
            catch (Exception ex)
            {
                body = JsonError(ex.Message);
            }

            await WriteResponseAsync(stream, body, ct);
        }
    }

    /// <summary>Render the main window's visual tree to PNG bytes (UI-thread only).</summary>
    private byte[] RenderWindowPng()
    {
        _window.UpdateLayout();
        int w = (int)Math.Ceiling(_window.ActualWidth);
        int h = (int)Math.Ceiling(_window.ActualHeight);
        if (w <= 0 || h <= 0) return Array.Empty<byte>();

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(_window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static async Task<(string method, string path, Dictionary<string, string> query)?> ReadRequestAsync(
        NetworkStream stream, CancellationToken ct)
    {
        var buffer = new List<byte>(4096);
        byte[] tmp = new byte[4096];
        int headerEnd = -1;
        while (buffer.Count < 64 * 1024)
        {
            int read = await stream.ReadAsync(tmp, ct);
            if (read <= 0) break;
            buffer.AddRange(tmp.AsSpan(0, read));
            headerEnd = IndexOfDoubleCrlf(buffer);
            if (headerEnd >= 0) break;
        }
        if (headerEnd < 0) return null;

        string headerText = Encoding.ASCII.GetString(buffer.GetRange(0, headerEnd).ToArray());
        string[] reqLineParts = headerText.Split("\r\n")[0].Split(' ');
        if (reqLineParts.Length < 2) return null;

        string method = reqLineParts[0];
        string target = reqLineParts[1];
        string path = target;
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        int q = target.IndexOf('?');
        if (q >= 0)
        {
            path = target[..q];
            foreach (string pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string key = eq < 0 ? pair : pair[..eq];
                string val = eq < 0 ? string.Empty : Uri.UnescapeDataString(pair[(eq + 1)..]);
                query[key] = val;
            }
        }
        return (method, path, query);
    }

    private static int IndexOfDoubleCrlf(List<byte> b)
    {
        for (int i = 0; i + 3 < b.Count; i++)
        {
            if (b[i] == (byte)'\r' && b[i + 1] == (byte)'\n' && b[i + 2] == (byte)'\r' && b[i + 3] == (byte)'\n')
                return i;
        }
        return -1;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, string body, CancellationToken ct)
    {
        byte[] payload = Encoding.UTF8.GetBytes(body);
        await WriteBytesAsync(stream, "application/json; charset=utf-8", payload, ct);
    }

    private static async Task WriteBytesAsync(NetworkStream stream, string contentType, byte[] payload, CancellationToken ct)
    {
        string head =
            "HTTP/1.1 200 OK\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {payload.Length}\r\n" +
            "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }

    private string Route(string method, string path, Dictionary<string, string> query)
    {
        path = path.TrimEnd('/').ToLowerInvariant();
        bool post = method.Equals("POST", StringComparison.OrdinalIgnoreCase);
        bool get = method.Equals("GET", StringComparison.OrdinalIgnoreCase);

        switch (path)
        {
            case "/info":
                if (get) return Json(_window.GetDebugInfo());
                break;

            // Alpha bounds of the photo in an offscreen visual-tree render.
            case "/renderbounds":
                if (get) return Json(_window.GetRenderedImageBounds());
                break;

            case "/pan":
                if (post) { _window.PanBy(Num(query, "dx"), Num(query, "dy")); return Json(_window.GetDebugInfo()); }
                break;
            case "/pan/left":
                if (post) { _window.PanBy(-Num(query, "n", 50), 0); return Json(_window.GetDebugInfo()); }
                break;
            case "/pan/right":
                if (post) { _window.PanBy(Num(query, "n", 50), 0); return Json(_window.GetDebugInfo()); }
                break;
            case "/pan/up":
                if (post) { _window.PanBy(0, -Num(query, "n", 50)); return Json(_window.GetDebugInfo()); }
                break;
            case "/pan/down":
                if (post) { _window.PanBy(0, Num(query, "n", 50)); return Json(_window.GetDebugInfo()); }
                break;

            case "/zoom":
                if (post) { _window.ZoomToScale(Num(query, "scale", 1)); return Json(_window.GetDebugInfo()); }
                break;
            case "/zoom/in":
                if (post) { _window.ZoomStep(1.25); return Json(_window.GetDebugInfo()); }
                break;
            case "/zoom/out":
                if (post) { _window.ZoomStep(1 / 1.25); return Json(_window.GetDebugInfo()); }
                break;

            case "/nav/next":
                if (post) { _window.NavigateNext(); return Json(_window.GetDebugInfo()); }
                break;
            case "/nav/prev":
                if (post) { _window.NavigatePrevious(); return Json(_window.GetDebugInfo()); }
                break;

            // Open an arbitrary file — same code path as Ctrl+O / drag-drop, so tests
            // can verify the fit-on-open behaviour after the window is already shown.
            case "/open":
                if (post)
                {
                    if (query.TryGetValue("path", out string? op) && !string.IsNullOrEmpty(op))
                    {
                        if (File.Exists(op))
                        {
                            _window.OpenPath(op);
                            return Json(_window.GetDebugInfo());
                        }
                        return JsonError($"File not found: {op}");
                    }
                    return JsonError("Missing 'path' query parameter");
                }
                break;

            case "/fit":
                if (post) { _window.FitWindow(); return Json(_window.GetDebugInfo()); }
                break;
            case "/original":
                if (post) { _window.ShowOriginal(); return Json(_window.GetDebugInfo()); }
                break;
            case "/reset":
                if (post) { _window.ResetView(); return Json(_window.GetDebugInfo()); }
                break;
            case "/rotate":
                if (post) { _window.RotateBy(Num(query, "deg", 90)); return Json(_window.GetDebugInfo()); }
                break;

            case "/play":
                if (post)
                {
                    double? interval = query.TryGetValue("interval", out string? iv)
                                       && double.TryParse(iv, NumberStyles.Any, CultureInfo.InvariantCulture, out double d)
                        ? d : null;
                    _window.StartAutoPlay(interval);
                    return Json(_window.GetDebugInfo());
                }
                break;
            case "/pause":
                if (post) { _window.StopAutoPlay(); return Json(_window.GetDebugInfo()); }
                break;

            // Test-only: open/close the zoom drop-down so the template wiring can be verified.
            case "/debug/dropdown":
                if (post)
                {
                    _window.SetZoomDropdownOpen(Num(query, "open", 1) != 0);
                    return Json(_window.GetDebugInfo());
                }
                break;
        }

        return JsonError($"Unknown endpoint: {method} {path}");
    }

    /// <summary>Read a numeric query param (InvariantCulture so "2.5" parses regardless of OS locale).</summary>
    private static double Num(Dictionary<string, string> q, string key, double def = 0)
    {
        return q.TryGetValue(key, out string? v)
               && double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out double d)
            ? d : def;
    }

    private string Json(object o) => JsonSerializer.Serialize(o, _jsonOptions);
    private string JsonError(string msg) => JsonSerializer.Serialize(new { error = msg }, _jsonOptions);
}
#endif
