# Exact NuGet package provenance

The `LibHeif.Native.Runtime` upstream repository URL from its nuspec currently
returns 404. These local copies preserve the exact package files, metadata,
README files, and MSBuild targets used by the build. They accompany
`LIBHEIF-BUILD-INFO.md`, `THIRD-PARTY-NOTICES.txt`, and `SOURCE-OFFER.txt`.

| File | SHA-256 |
| --- | --- |
| `LibHeif.Native.Runtime.nuspec` | `a3a3ff7d532a994c1d55015eec9f0b32ee7c0547ed1d765b242c8049dcbcaacd` |
| `LibHeif.Native.Runtime-README.md` | `f6dff47d17fb5ed8a35d8bf846ebb76fe96e9e23278c79abae836c61393a23af` |
| `LibHeif.Native.Runtime-build.targets` | `21516a3bf22c77b4e6db3f76e1980a939ac77c696e84c2038a641aa0421c2125` |
| `LibHeif.Native.Runtime-buildTransitive.targets` | `21516a3bf22c77b4e6db3f76e1980a939ac77c696e84c2038a641aa0421c2125` |
| `libheif.native.runtime.1.20.2.nupkg` | `fcd59e16e4a59871d811fe73b7bc0503f17ebe8f3de28ddd2f626172cdacd989` |
| `LibHeifSharp.nuspec` | `686717e970fb9dbcb02fad7a16fcddc59416e883003367b8a540170809791ec8` |
| `LibHeifSharp-README.md` | `965e3a980f8cce83ab49752757e73d62e1840d428ba5738f15dbed12f6f8a712` |
| `libheifsharp.3.2.0.nupkg` | `0c182fd29404a28f8b244864bbc0f73cd14dafebf76ab05ccb406327a6b8e77e` |

The `.nupkg` files are exact package copies retained for provenance; the
application build does not execute code from this directory.
