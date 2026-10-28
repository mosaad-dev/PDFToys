# Third-party redistribution files

`PDFium-147.0.7690` contains the unmodified LICENSE, VERSION and licenses folder from the matching [PDFium Windows x64 archive](https://github.com/bblanchon/pdfium-binaries/releases/download/chromium%2F7690/pdfium-win-x64.tgz). These include PDFium's own third-party notices, which the NuGet binary package does not carry.

`Inter/LICENSE.txt` is the Inter font's [upstream SIL Open Font License](https://github.com/rsms/inter/blob/master/LICENSE.txt). `license-texts` contains the [SPDX MIT](https://github.com/spdx/license-list-data/blob/main/text/MIT.txt) and [MS-PL](https://github.com/spdx/license-list-data/blob/main/text/MS-PL.txt) texts used by expression-only NuGet packages. The root LICENSE supplies the Apache 2.0 text.

`scripts/collect-notices.ps1` additionally copies restored packages' own licenses and notices (including SkiaSharp, HarfBuzz, ANGLE and the self-contained .NET runtime) and produces a versioned package attribution inventory. The release does not download license files during packaging.
