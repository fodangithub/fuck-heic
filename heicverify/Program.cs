using System;
using System.IO;
using PhotoViewer;

// Decode a single HEIC using the app's real ImageDecoder. One file per invocation.
string path = args[0];
try
{
    var bmp = ImageDecoder.Decode(path);
    Console.WriteLine($"OK {Path.GetFileName(path)} -> {bmp.PixelWidth}x{bmp.PixelHeight} {bmp.Format}");
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL {Path.GetFileName(path)} -> {ex.GetType().Name}: {ex.Message}");
    Environment.ExitCode = 3;
}
