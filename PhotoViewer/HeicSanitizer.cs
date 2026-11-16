using System;
using System.Collections.Generic;
using System.IO;

namespace PhotoViewer;

/// <summary>
/// Some iPhone HEICs embed an HDR "gain map" as a second, movie-sequence track
/// (top-level `moov`/`sidx` boxes alongside the still-image `meta`). libheif's
/// still-image parser rejects those files with "Unknown track handler". Stripping
/// the sequence boxes leaves a clean still HEIC that decodes normally.
/// </summary>
public static class HeicSanitizer
{
    public static byte[] Sanitize(string path)
    {
        var src = File.ReadAllBytes(path);
        return Sanitize(src);
    }

    public static byte[] Sanitize(byte[] data)
    {
        var outBuf = new List<byte>(data.Length);
        int off = 0;
        int n = data.Length;
        while (off + 8 <= n)
        {
            int size = ReadU32(data, off);
            string typ = ReadType(data, off + 4);
            int hdr = 8;
            if (size == 1)
            {
                // 64-bit extended size (rare); can't safely handle with int math.
                long big = ReadU64(data, off + 8);
                if (big > int.MaxValue) break;
                size = (int)big;
                hdr = 16;
            }
            else if (size == 0)
            {
                size = n - off; // box extends to EOF
            }
            if (size < hdr || off + size > n) break;

            // Drop movie-sequence / indexing boxes; keep everything else.
            bool drop = typ == "moov" || typ == "sidx" || typ == "mdat_seq";
            if (!drop)
                outBuf.AddRange(new ArraySegment<byte>(data, off, size));

            off += size;
        }

        // If parsing went off the rails and we kept nothing usable, return original.
        return outBuf.Count > 0 ? outBuf.ToArray() : data;
    }

    private static int ReadU32(byte[] b, int o) =>
        (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];

    private static long ReadU64(byte[] b, int o) =>
        ((long)ReadU32(b, o) << 32) | (uint)ReadU32(b, o + 4);

    private static string ReadType(byte[] b, int o) =>
        $"{(char)b[o]}{(char)b[o + 1]}{(char)b[o + 2]}{(char)b[o + 3]}";
}
