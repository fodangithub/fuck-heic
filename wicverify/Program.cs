using System;
using System.IO;
using System.Windows.Media.Imaging;

// Test Windows WIC HEIC decoding via BitmapDecoder (explicit frame) to get true dimensions.
string dir = @"\\Ns-server\devshared\temp\baby";
var heics = Directory.GetFiles(dir, "*.heic").OrderBy(f => f).ToList();
Console.WriteLine($"Found {heics.Count} HEIC files - BitmapDecoder\n");

string tmp = Path.Combine(Path.GetTempPath(), "wic_test.heic");
int ok = 0, fail = 0;
foreach (var f in heics)
{
    try
    {
        // Copy to local first (WIC can be finicky with UNC streams).
        File.Copy(f, tmp, overwrite: true);
        using var fs = File.OpenRead(tmp);
        var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = dec.Frames[0];
        ok++;
        Console.WriteLine($"OK   {Path.GetFileName(f)}  ->  {frame.PixelWidth}x{frame.PixelHeight}  {frame.Format}");
    }
    catch (Exception ex)
    {
        fail++;
        Console.WriteLine($"FAIL {Path.GetFileName(f)} -> {ex.GetType().Name}: {ex.Message}");
    }
}
if (File.Exists(tmp)) File.Delete(tmp);
Console.WriteLine($"\n=== BitmapDecoder: {ok} OK, {fail} FAIL out of {heics.Count} ===");
