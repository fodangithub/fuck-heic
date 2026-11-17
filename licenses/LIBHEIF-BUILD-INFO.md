# libheif build provenance

This file records the exact third-party binaries conveyed in the published
PhotoViewer folder and where their Corresponding Source can be obtained.

## Shipped binaries

| File | Origin | SHA-256 |
| --- | --- | --- |
| `libheif.dll` | `LibHeif.Native.Runtime` 1.20.2, `runtimes/win-x64/native/libheif.dll` | `d7cb4f5ab61dec6c2619a502763608cf69ec73ed421700c4fef03d60adc4b621` |
| `LibHeifSharp.dll` | `LibHeifSharp` 3.2.0, `lib/netstandard2.1/LibHeifSharp.dll` | `215252072b98b5b15b2b6db525ee2df06545ecf6f1b7fd29c593ecb7024f575b` |

Both files in `publish/` are byte-identical to the NuGet package originals; they
are unmodified. The NuGet package metadata declares `LGPL-3.0-or-later` for
both `LibHeif.Native.Runtime` and `LibHeifSharp`.

## Source locations

- libheif 1.20.2: <https://github.com/strukturag/libheif/tree/v1.20.2>  
  Source archive: <https://github.com/strukturag/libheif/archive/refs/tags/v1.20.2.tar.gz>
- LibHeif.Native.Runtime build repository: <https://github.com/borka-s/LibHeif.Native.Runtime>  
  Exact commit: `797a1d64036ca7a9aed612c194e958a2306bdab1`
- LibHeifSharp 3.2.0: <https://github.com/0xC0000054/libheif-sharp>  
  Exact commit: `d208cd6f3ba9023c6c3692678a7c421c3e33c7b9`
- libde265: <https://github.com/strukturag/libde265>
- dav1d: <https://code.videolan.org/videolan/dav1d>
- libaom: <https://aomedia.googlesource.com/aom>
- zlib: <https://zlib.net/>
- .NET runtime 9.0.23: <https://github.com/dotnet/runtime/tree/v9.0.23>

## Native build information

The `LibHeif.Native.Runtime` 1.20.2 README states that `libheif.dll` is built
from `strukturag/libheif` using the `release-noplugins` CMake preset. It is a
self-contained shared library with HEVC decoding through `libde265` and AV1
decoding/encoding through `dav1d`/`libaom` compiled in; no separate codec
plugin files are required at runtime.

The package build workflow is referenced by the package README as
`.github/workflows/main.yml` in the `LibHeif.Native.Runtime` repository. The
workflow builds on `windows-latest` and `ubuntu-22.04`, downloads the artifacts,
and packs the NuGet package.

Known bundled codec/license evidence from the package README and binary strings:

- `libde265` — LGPL; upstream COPYING supplied in `third-party/libde265-COPYING.txt`.
- `dav1d` — upstream BSD-2-Clause; the package README incorrectly calls it LGPL.
- `libaom` — BSD-2-Clause; upstream LICENSE supplied in `third-party/libaom-LICENSE.txt`.
- `zlib`/deflate — zlib License.
- No x265/GPL encoder was detected in the bundled binary. Recheck this if the
  native DLL is replaced.

## Local provenance copies

The `LibHeif.Native.Runtime` GitHub repository URL from the nuspec currently
returns 404, so the exact package contents are retained locally under
`package-metadata/`:

- `LibHeif.Native.Runtime.nuspec`, `LibHeif.Native.Runtime-README.md`
- `LibHeif.Native.Runtime-build.targets` and `-buildTransitive.targets`
- `libheif.native.runtime.1.20.2.nupkg`
- `LibHeifSharp.nuspec`, `LibHeifSharp-README.md`
- `libheifsharp.3.2.0.nupkg`

Local source archives are under `source/`:

- `libheif-1.20.2-source.tar.gz` — exact v1.20.2 tag
- `LibHeifSharp-3.2.0-source.tar.gz` — exact commit `d208cd6f...`
- `libde265-source.tar.gz` — current upstream source snapshot; the exact
  compiled revision is covered by `SOURCE-OFFER.txt`

SHA-256 hashes for these local artifacts are recorded in `source/README.md`
and `package-metadata/README.md`.

## Rebuilding / relinking

See `RELINKING.md`. The folder publish keeps `libheif.dll` and
`LibHeifSharp.dll` separate and replaceable. See `SOURCE-OFFER.txt` for the
written Corresponding Source offer.
