# LibHeif.Native.Runtime

Native libheif binaries for .NET projects, packaged for NuGet.

Targets:
- Windows x64
- Linux x64 (glibc ≥ 2.17, i.e. Ubuntu 18.04+)

Built from [strukturag/libheif](https://github.com/strukturag/libheif) using the
`release-noplugins` CMake preset — a self-contained shared library with HEVC
(libde265) and AV1 (dav1d/libaom) codecs compiled in. No separate codec plugin
files are required at runtime.

## Usage

This package is intended to be used alongside
[LibHeifSharp](https://www.nuget.org/packages/LibHeifSharp/), which provides the
.NET P/Invoke bindings:

```xml
<PackageReference Include="LibHeifSharp" Version="3.*" />
<PackageReference Include="LibHeif.Native.Runtime" Version="1.20.2" />
```

`LibHeifSharp` uses `[DllImport("libheif")]`. The .NET runtime resolves this to:
- `libheif.dll` on Windows
- `libheif.so` on Linux

This package places the correct binary in `runtimes/{rid}/native/` so the .NET
SDK copies it next to your output assembly automatically.

### AnyCPU projects

If your project doesn't set a `RuntimeIdentifier`, the included MSBuild
`.targets` file (auto-imported via NuGet) copies the correct binary to your
output directory based on the build machine's OS and architecture.

For RID-specific builds (`dotnet publish -r win-x64` etc.), the standard NuGet
`runtimes/` convention handles everything without the `.targets` file.

## LGPL Notice

libheif is licensed under the
[GNU Lesser General Public License v3.0](https://www.gnu.org/licenses/lgpl-3.0.html).

If your application **dynamically links** libheif (which is what this package
does — it ships a shared library), the LGPL allows use in proprietary software
**provided** you allow users to replace the libheif shared library. Shipping it
as a separate `.dll`/`.so` (as this package does) satisfies this requirement.

The codec libraries (libde265, dav1d) are also LGPL. libaom (AV1 encoder/decoder)
is BSD-2-Clause.

## Package structure

```
runtimes/
  win-x64/native/libheif.dll
  linux-x64/native/libheif.so
build/
  LibHeif.Native.Runtime.targets       (AnyCPU copy helper)
buildTransitive/
  LibHeif.Native.Runtime.targets
```

## Building from source / CI

See `.github/workflows/main.yml`. The workflow:
1. Builds libheif on `windows-latest` and `ubuntu-22.04` (x64).
2. Downloads both artifacts and packs a single `.nupkg`.
3. Publishes to NuGet.org when a `v*` tag is pushed.

To bump the libheif version, update `LIBHEIF_VERSION` in the workflow and
`<Version>` in `src/LibHeif.Native.Runtime.csproj`.
