# Photo Viewer (with HEIC support)

A native Windows desktop photo viewer built with **C# / WPF (.NET 9)**. Works like the classic
Windows Photo Viewer — open an image and page through the folder with the arrow keys — with
first-class **HEIC / HEIF / AVIF** support out of the box.

## Features

- Open any image by double-clicking (command-line arg), `Ctrl+O`, or drag & drop
- Page through the folder: `←` / `→` (or `PageUp` / `PageDown`), wraps at the ends
- Zoom: mouse wheel, `+` / `-`, `1` for 100%
- Fit-to-window: `F` (default on open, re-fits on window resize)
- Rotate: `End` (clockwise 90°), `Home` (counter-clockwise 90°)
- HEIC / HEIF / AVIF decoding via a **bundled libheif** — no system HEIF extension required

## Supported formats

JPEG, PNG, BMP, GIF, TIFF, WebP, ICO, HD Photo (WDP/JXR/HDP) via Windows Imaging Component,
plus **HEIC, HEIF, AVIF, HIF** via libheif.

## Build

Requires the .NET 9 SDK.

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

This produces a single self-contained executable at `publish/PhotoViewer.exe` (~60 MB) that
includes the .NET runtime and the native `libheif` decoder — copy it to any Windows 10/11
x64 machine and run, no install needed.

For a quick debug build:

```bash
dotnet build -c Release
```

## How HEIC decoding works

`ImageDecoder.cs` routes by extension:

- `.heic` / `.heif` / `.avif` / `.hif` → decoded through the managed
  [LibHeifSharp](https://github.com/0xC0000054/libheif-sharp) bindings, with the native
  `libheif.dll` (HEVC + AV1 decoders compiled in) supplied by the
  [LibHeif.Native.Runtime](https://www.nuget.org/packages/LibHeif.Native.Runtime) package.
  Images are decoded to interleaved RGBA (a byte layout identical to WPF's `Bgra32`) and
  wrapped in a `BitmapSource`. EXIF orientation is applied automatically.
- All other formats → WPF's built-in `BitmapImage` (Windows codecs).

## Notes

- `libheif` is LGPL-3.0 and is shipped as a separate shared library (bundled inside the
  single-file executable but extracted at runtime), which satisfies the LGPL for
  proprietary use.
- The exe is x64. Build with `-r win-x86` for 32-bit if needed.

## License

Application code: MIT. See libheif's LGPL-3.0 notice above for the bundled decoder.
