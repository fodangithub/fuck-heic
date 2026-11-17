# LGPL source archives

These archives are local convenience copies of the source for LGPL components
shipped with PhotoViewer. They accompany `SOURCE-OFFER.txt`; they do not replace
the written offer. The archives were downloaded verbatim and were not extracted
or executed as part of this repository's build.

| File | Source | SHA-256 |
| --- | --- | --- |
| `libheif-1.20.2-source.tar.gz` | libheif v1.20.2 tag | `b70340395d84184bb8dfc833dd51c95ae049435f7ff9abc7b505a08b5ee2bd2a` |
| `LibHeifSharp-3.2.0-source.tar.gz` | LibHeifSharp commit `d208cd6f3ba9023c6c3692678a7c421c3e33c7b9` | `0db64e3a106043f464329fab5ccff756c70705a7a0078ebee8ff004b9dba8bde` |
| `libde265-source.tar.gz` | current upstream `master` snapshot | `d77d57876c36cf778b046ec6c5411c2e35a6630b45568a4abbf32c671f0633e0` |

`libde265` is compiled into `libheif.dll`, but the NuGet package metadata does
not pin its exact compiled revision. The current upstream snapshot is included
as a convenience; the exact revision for the shipped binary is covered by
`SOURCE-OFFER.txt`.
