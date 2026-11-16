using System.IO;

namespace PhotoViewer;

/// <summary>
/// Loads the sibling images of a file and provides prev/next navigation.
/// </summary>
public sealed class FolderNavigator
{
    private List<string> _files = new();
    private int _index = -1;

    /// <summary>All sibling image files in the same folder.</summary>
    public IReadOnlyList<string> Files => _files;

    /// <summary>Zero-based position of the current file, or -1.</summary>
    public int Index => _index;

    /// <summary>Total number of navigable images in the folder.</summary>
    public int Count => _files.Count;

    public string? Current => _index >= 0 && _index < _files.Count ? _files[_index] : null;

    /// <summary>
    /// Scan the folder containing <paramref name="path"/> for supported images
    /// and position the navigator on that file. Natural sort by name.
    /// </summary>
    public void LoadFolder(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            _files = new List<string> { path };
            _index = 0;
            return;
        }

        // Normalize both sides: the command line may hand us "F:/tmp/x.heic" while
        // directory enumeration returns "F:\tmp\x.heic" — without this the file gets
        // inserted a second time and appears twice in the navigator.
        string full = Path.GetFullPath(path);
        _files = Directory.EnumerateFiles(dir)
            .Where(ImageDecoder.IsSupported)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _index = _files.FindIndex(f =>
            string.Equals(Path.GetFullPath(f), full, StringComparison.OrdinalIgnoreCase));
        if (_index < 0)
        {
            // File may have been filtered out (unknown ext); still show it.
            _files.Insert(0, path);
            _index = 0;
        }
    }

    public string? Next()
    {
        if (_files.Count == 0) return null;
        _index = (_index + 1) % _files.Count;
        return Current;
    }

    public string? Previous()
    {
        if (_files.Count == 0) return null;
        _index = (_index - 1 + _files.Count) % _files.Count;
        return Current;
    }
}
