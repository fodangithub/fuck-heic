using System.IO;
using System.Text.Json;

namespace PhotoViewer;

/// <summary>
/// Persisted application settings, stored as JSON under %AppData%\PhotoViewer.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Auto-play slide interval in seconds. Default 2.5 s.</summary>
    public double AutoPlayIntervalSeconds { get; set; } = 2.5;

    /// <summary>Debug HTTP control port (only used in Debug builds).</summary>
    public int DebugHttpPort { get; set; } = 8321;

    private static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhotoViewer");

    private static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>Load settings from disk, or return defaults if none/corrupt.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // Corrupt or unreadable settings — fall back to defaults.
        }
        return new AppSettings();
    }

    /// <summary>Write the current settings to disk (best effort).</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Saving settings must never crash the app.
        }
    }

    /// <summary>Clamped interval in seconds (>= 0.1 s).</summary>
    public double IntervalSecondsClamped => Math.Clamp(AutoPlayIntervalSeconds, 0.1, 3600);
}
