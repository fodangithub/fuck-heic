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
        // Decode with transformations applied (EXIF orientation handled by libheif).
        using var context = new HeifContext(path);
        using var handle = context.GetPrimaryImageHandle();
        using var image = handle.Decode(HeifColorspace.Rgb, HeifChroma.InterleavedRgba32, null);

        int width = image.Width;
        int height = image.Height;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Decoded image has invalid dimensions.");

        var plane = image.GetPlane(HeifChannel.Interleaved);
        int stride = plane.Stride;
        int bytesPerPixel = 4;
        int requiredBytes = checked(stride * height);

        var buffer = new byte[width * height * bytesPerPixel];
        // Copy row by row to drop any trailing stride padding.
        for (int y = 0; y < height; y++)
        {
            IntPtr rowSrc = IntPtr.Add(plane.Scan0, y * stride);
            Marshal.Copy(rowSrc, buffer, y * width * bytesPerPixel, width * bytesPerPixel);
        }

        var bmp = BitmapSource.Create(
            width, height, 96, 96,
            PixelFormats.Bgra32, null, buffer, width * bytesPerPixel);
        bmp.Freeze();
        return bmp;
    }
}
