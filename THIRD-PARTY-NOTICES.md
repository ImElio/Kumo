# Third-party notices

Kumo itself is MIT-licensed (see [LICENSE](LICENSE)). It redistributes the following components, each under its own
license. Versions are those referenced by the projects at the time of writing.

| Component | Version | License | Use |
| --- | --- | --- | --- |
| [SkiaSharp](https://github.com/mono/SkiaSharp) and SkiaSharp.NativeAssets.Win32 | 3.119.4 | MIT | Image decoding, encoding and resampling |
| [Skia](https://skia.org) (inside `libSkiaSharp.dll`) | bundled with SkiaSharp | BSD-3-Clause | Graphics library used by SkiaSharp |
| [PDFsharp](https://www.pdfsharp.com) | 6.2.4 | MIT | PDF merge, split, page extraction, encryption, images to PDF |
| Microsoft.Extensions.Logging.Abstractions and Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.x (dependencies of PDFsharp) | MIT | Required by PDFsharp |
| System.Security.Cryptography.Pkcs | 10.0.13 | MIT | Required by PDFsharp; pinned to a patched version |
| Microsoft.Windows.SDK.NET and WinRT.Runtime (C#/WinRT) | from the .NET Windows SDK targeting pack | MIT | Access to `Windows.Data.Pdf` for rendering PDF pages |

PDFsharp 6.5.0 and later are distributed under the AGPL-3.0. Kumo deliberately stays on 6.2.4; upgrading requires a
licensing decision.

The installers are built with [NSIS](https://nsis.sourceforge.io) (zlib/libpng license); the installer stub it
embeds carries no attribution requirement.

Release builds are self-contained and redistribute the .NET runtime and WPF, provided by Microsoft under the MIT
License ([license](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT),
[third-party notices](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT)).

The Kumo mascot and icon are original artwork created for this project and are covered by Kumo's license.
