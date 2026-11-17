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
- Right-click the picture for copy image/path, open containing folder, rotate, flip, print, and system file properties
- HEIC / HEIF / AVIF decoding via a **bundled libheif** — no system HEIF extension required

## Supported formats

JPEG, PNG, BMP, GIF, TIFF, WebP, ICO, HD Photo (WDP/JXR/HDP) via Windows Imaging Component,
plus **HEIC, HEIF, AVIF, HIF** via libheif.

## Build

Requires the .NET 9 SDK.

```bash
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

This produces a self-contained **folder** at `publish/` containing
`PhotoViewer.exe`, the .NET and WPF runtimes, `libheif.dll`,
`LibHeifSharp.dll`, and the license/notice files. Copy the whole folder to any
Windows 10/11 x64 machine and run `PhotoViewer.exe`; no install is needed. The
LGPL libraries remain separate, replaceable DLLs.

For a quick debug build:

```bash
dotnet build -c Debug
```

## How HEIC decoding works

`ImageDecoder.cs` routes by extension:

- `.heic` / `.heif` / `.avif` / `.hif` → decoded through the managed
  [LibHeifSharp](https://github.com/0xC0000054/libheif-sharp) bindings, with the native
  `libheif.dll` (HEVC + AV1 decoders compiled in) supplied by the
  [LibHeif.Native.Runtime](https://www.nuget.org/packages/LibHeif.Native.Runtime) package.
  Images are decoded to interleaved RGB24 (matching WPF's `PixelFormats.Rgb24`)
  and wrapped in a `BitmapSource`. EXIF orientation is applied automatically.
- All other formats → WPF's built-in `BitmapImage` (Windows codecs).

## Third-party licenses and LGPL compliance

PhotoViewer redistributes LGPL-3.0-or-later components, including `libheif.dll`
and `LibHeifSharp.dll`. The `licenses/` folder is copied into the published
folder and contains:

- verbatim `GPL-3.0.txt` and `LGPL-3.0.txt`
- `THIRD-PARTY-NOTICES.txt`
- `SOURCE-OFFER.txt` with the written Corresponding Source offer
- `LIBHEIF-BUILD-INFO.md` with exact versions, commits, build flags, and hashes
- `RELINKING.md` with instructions for replacing the shared libraries
- upstream license texts under `licenses/third-party/` and .NET notices under
  `licenses/dotnet/`

The publish output is deliberately a folder rather than a single-file bundle so
the LGPL libraries remain replaceable. See `licenses/README.md` for the full
index.

## Notes

- The exe is x64. Build with `-r win-x86` for 32-bit if needed.
- Generated 512×512 icon reference images are kept in
  `PhotoViewer/Icons/Originals/`; the normalized runtime icons are embedded WPF
  resources in `PhotoViewer/Icons/`.

## License

Application code: MIT. See `LICENSE` and the third-party notices/source offer in
`licenses/` for the bundled libraries.
