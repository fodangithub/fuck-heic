using System;
using System.IO;
using System.Runtime.InteropServices;
using LibHeifSharp;

// Standalone probe: try multiple decode strategies on one HEIC.
// Run one file per process so a native crash can't take down the batch.
string path = args[0];

void Try(string label, Action a)
{
    try { a(); Console.WriteLine($"  [{label}] OK"); }
    catch (Exception ex) { Console.WriteLine($"  [{label}] {ex.GetType().Name}: {ex.Message}"); }
}

using (var ctx = new HeifContext(path))
{
    var ids = ctx.GetTopLevelImageIds();
    Console.WriteLine($"top-level image ids: {ids.Count}");
    foreach (var id in ids)
    {
        string label = $"decode id={id}";
        Try(label, () =>
        {
            using var h = ctx.GetImageHandle(id);
            using var img = h.Decode(HeifColorspace.Rgb, HeifChroma.InterleavedRgba32, null);
            Console.WriteLine($"        -> {img.Width}x{img.Height}");
        });
    }
}
