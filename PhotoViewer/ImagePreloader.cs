using System.Collections.Concurrent;
using System.Windows.Media.Imaging;

namespace PhotoViewer;

/// <summary>
/// Decodes images on background threads and caches the results so navigating to a
/// neighboring photo in the same folder is instant. Decoded <see cref="BitmapSource"/>s
/// are frozen, so they can be created and consumed across threads safely.
/// </summary>
public sealed class ImagePreloader
{
    // Lazy wraps each decode so a path is only ever decoded once, even if requested
    // by both a preload and the user navigating to it at the same time.
    private readonly ConcurrentDictionary<string, Lazy<Task<BitmapSource>>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns a cached decode, or starts decoding on a background thread.
    /// Never touches the UI thread — safe to await from anywhere.
    /// </summary>
    public Task<BitmapSource> GetOrDecodeAsync(string path) =>
        _cache.GetOrAdd(path, static p => new Lazy<Task<BitmapSource>>(() => Task.Run(() => ImageDecoder.Decode(p))))
              .Value;

    /// <summary>
    /// True if the image is already fully decoded and cached (no waiting required).
    /// </summary>
    public bool TryGetCached(string path, out BitmapSource? bitmap)
    {
        bitmap = null;
        if (_cache.TryGetValue(path, out var lazy) &&
            lazy.IsValueCreated &&
            lazy.Value.Status == TaskStatus.RanToCompletion)
        {
            bitmap = lazy.Value.Result;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Decode the images around <paramref name="index"/> in the background and evict
    /// everything outside that window, bounding memory to (forward + backward + 1) images.
    /// </summary>
    public void PreloadAround(IReadOnlyList<string> files, int index, int forward, int backward)
    {
        if (files.Count == 0 || index < 0 || index >= files.Count) return;

        int n = files.Count;
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { files[index] };
        var toStart = new List<string>();

        for (int d = 1; d <= forward; d++)
        {
            var p = files[(index + d) % n];
            if (keep.Add(p)) toStart.Add(p);
        }
        for (int d = 1; d <= backward; d++)
        {
            var p = files[(index - d + n) % n];
            if (keep.Add(p)) toStart.Add(p);
        }

        // Evict decoded/decoding entries that fell outside the window.
        foreach (var key in _cache.Keys)
            if (!keep.Contains(key))
                _cache.TryRemove(key, out _);

        // Fire-and-forget. Any decode that faults while preloading is observed here so it
        // doesn't surface as an unobserved task exception; navigating to it retries fresh.
        foreach (var p in toStart)
        {
            var task = GetOrDecodeAsync(p);
            if (!task.IsCompleted)
                task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    public void Clear() => _cache.Clear();
}
