# Replacing / relinking the LGPL libraries

PhotoViewer is published as a folder, not as a single-file executable. The LGPL
components are ordinary separate DLLs:

- `libheif.dll` — native libheif 1.20.2
- `LibHeifSharp.dll` — managed .NET binding 3.2.0

To use a modified or differently built LGPL library:

1. Stop `PhotoViewer.exe`.
2. Build or obtain a compatible `libheif.dll` for `win-x64`. The managed binding
   expects the native library to be named `libheif` and resolves to
   `libheif.dll` on Windows.
3. Replace `libheif.dll` in the published folder. If you also rebuild the
   managed binding, replace `LibHeifSharp.dll` with a compatible
   `netstandard2.1` build.
4. Restart `PhotoViewer.exe`.

No hash checks, signature checks, anti-tamper logic, or single-file extraction
prevent replacement of these DLLs. The application assembly remains separate.

Corresponding Source and build information for the shipped binaries are in
`SOURCE-OFFER.txt` and `LIBHEIF-BUILD-INFO.md`.
