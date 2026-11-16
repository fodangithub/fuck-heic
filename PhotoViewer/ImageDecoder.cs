using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibHeifSharp;

namespace PhotoViewer;

/// <summary>
/// Decodes image files to WPF <see cref="BitmapSource"/>.
/// HEIC/HEIF/AVIF go through the bundled libheif; everything else uses WIC (Windows codecs).
/// </summary>
public static class ImageDecoder
{
    private static readonly HashSet<string> HeifExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".heic", ".heif", ".avif", ".hif"
    };

    /// <summary>Supported image extensions (lowercase, with dot).</summary>
    public static readonly HashSet<string> SupportedExtensions = BuildSupported();

    private static HashSet<string> BuildSupported()
    {
        var set = new HashSet<string>(HeifExtensions, StringComparer.OrdinalIgnoreCase);
        foreach (var e in new[]
        {
            ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".gif",
            ".tif", ".tiff", ".webp", ".ico", ".wdp", ".jxr", ".hdp"
        })
        {
            set.Add(e);
        }
        return set;
    }

    public static bool IsSupported(string path)
    {
        return SupportedExtensions.Contains(Path.GetExtension(path));
    }

    /// <summary>
    /// Decode a file to a frozen <see cref="BitmapSource"/>. Throws on failure.
    /// </summary>
    public static BitmapSource Decode(string path)
    {
        if (HeifExtensions.Contains(Path.GetExtension(path)))
            return DecodeHeif(path);
        return DecodeNative(path);
    }

    private static BitmapSource DecodeNative(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;   // release file handle immediately
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static BitmapSource DecodeHeif(string path)
    {
        // Some iPhone HEICs carry an HDR gain-map as a movie-sequence track that
        // libheif's still-image parser rejects. Strip those boxes in memory first.
        byte[] sanitized = HeicSanitizer.Sanitize(path);

        using var context = new HeifContext(sanitized);
        using var handle = context.GetPrimaryImageHandle();
        // Decode to RGB24: it matches WPF's PixelFormats.Rgb24 byte-for-byte (R,G,B),
        // so no R/B channel swap is needed and it uses 25% less memory than RGBA32.
        using var image = handle.Decode(HeifColorspace.Rgb, HeifChroma.InterleavedRgb24, null);

        int width = image.Width;
        int height = image.Height;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Decoded image has invalid dimensions.");

        var plane = image.GetPlane(HeifChannel.Interleaved);
        int stride = plane.Stride;
        int bytesPerPixel = 3;
        var buffer = new byte[width * height * bytesPerPixel];

        if (stride == width * bytesPerPixel)
        {
            // No row padding — copy the whole plane in one fast call.
            Marshal.Copy(plane.Scan0, buffer, 0, width * height * bytesPerPixel);
        }
        else
        {
            for (int y = 0; y < height; y++)
            {
                IntPtr rowSrc = IntPtr.Add(plane.Scan0, y * stride);
                Marshal.Copy(rowSrc, buffer, y * width * bytesPerPixel, width * bytesPerPixel);
            }
        }

        var bmp = BitmapSource.Create(
            width, height, 96, 96,
            PixelFormats.Rgb24, null, buffer, width * bytesPerPixel);
        bmp.Freeze();
        return bmp;
    }
}
