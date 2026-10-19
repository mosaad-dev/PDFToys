<p align="center">
  <img src="docs/assets/pdftoys-logo.png" alt="PDFToys logo" width="72">
</p>

<h1 align="center">PDFToys</h1>

<p align="center">
  Everyday PDF tools: merge, split, and manage PDFs from File Explorer.
</p>

<p align="center">
  <a href="https://github.com/MohammedSaad114/PDFToys/releases/latest">Download for Windows</a>
  ·
  <a href="#features-and-quick-start">Features</a>
  ·
  <a href="#building-from-source">Build from source</a>
</p>

<p align="center">
  <img src="docs/assets/pdftoys-home.png"
       alt="PDFToys main window showing its PDF tools"
       width="600">
</p>

## Installation

PDFToys runs on 64-bit Windows 10 or later. The installer and portable build are self-contained; a separate .NET runtime is not required. Microsoft Word, Excel, or PowerPoint is needed only for its corresponding Office documents.

Download a published Windows setup `.exe` from the [releases page](https://github.com/MohammedSaad114/PDFToys/releases/latest), then run the installer. It adds a Start menu entry, an optional desktop shortcut, and the classic Explorer context menu. On Windows 11, that menu is under **Show more options**. A portable ZIP can run the desktop app without installation; Explorer integration requires the installer or manual registration.

The 0.2.0 candidate is prepared from the local `release/0.2.0` branch. Building it does not upload or publish a GitHub release. See the [changelog](CHANGELOG.md) for its changes and the [implementation plan](docs/NEXT_RELEASE_IMPLEMENTATION_PLAN.md) for the release verification checklist.

## Features and quick start

Home groups the six PDF tools under **Document tools**, with **Convert & export** in its own section. Opening a tool starts with an empty input selection. A remembered folder is a preference, not permission to select or read every PDF in it. Files explicitly passed by Explorer or a startup command remain selected.

| Tool | Workflow |
| --- | --- |
| Merge PDFs | Add at least two PDFs; drag to reorder or use Move up / down, then choose **Merge files**. |
| Split PDF | Choose or change the PDF, check its page count, then split in half, into four parts, or by ranges such as `1-5, 8, 11-13`. Each range creates a file. |
| Compress PDF | Add PDFs and choose Standard or Maximum. Results show actual sizes, savings, changed/skipped images and explanations. |
| Organize pages | Reorder, rotate or exclude pages. Preview includes the pending rotation; saving preserves the source page's existing rotation. |
| Protect PDF | Enter a new opening password and choose a new copy or guarded replacement. |
| Unlock PDF | Enter the current password and choose a new copy or guarded replacement. |
| Convert / export | Convert images, text, Markdown, SVG or supported Office documents to PDF; combine inputs; or export PDFs as JPG, JPEG, PNG or extracted Markdown text. |

For a merge:

1. Choose **Merge PDFs**, then **Add PDF files**.
2. Select a file and move it into place using the buttons or drag-and-drop.
3. Choose **Merge files**. Files in one folder save beside the inputs; files from different folders prompt for a location. The result row contains the actual saved path.

All tools run processing in the background. Inputs and options are snapshotted when a job starts; conflicting controls stay disabled while it runs. Progress reports the available file, stage and page information. **Cancel** stops at a safe boundary, so a native call may finish its current step first. Closing the window during a job requests cancellation and waits for that safe boundary.

Completed outputs stay available after a later failure or cancellation. Result rows offer **Open**, **Show in folder** and **Copy path**. **Retry failed** keeps successful results and retries failed inputs; Protect and Unlock use the password and output choice currently shown when retry starts. Combining documents retries the complete group.

![PDFToys workflow demo](docs/assets/pdftoys-demo.gif)

## Output safety

- **Create new copy is the default.** Generated files are staged and validated before being committed. Existing outputs receive a numbered alternative name instead of being overwritten, even when a save dialog chooses an existing name.
- **Inputs remain intact.** Merge, Split, Compress, Organize and conversion/export create separate outputs. Inputs with the same filename in different folders retain separate identities during batch work.
- **Replace original is explicit.** Protect and Unlock validate a separate replacement PDF, check stable Windows file identities, and use a filesystem replacement operation that also keeps a distinct recovery backup. They refuse unsafe source aliases and do not fall back to deleting or overwriting the original if the required replacement behavior is unavailable. The result reports the backup location. If replacement fails after generation, the original and generated copy are reported separately.
- **Cancellation is per file.** A batch is not one transaction: previously committed outputs remain valid, and uncommitted candidates are cleaned up. Results distinguish failure, partial completion, cancellation and unchanged compression.

## Compression and document compatibility

Standard and Maximum perform **lossy image recompression** at JPEG quality 60 and 30. They preserve text and vector content as PDF content; they do not rasterize entire pages, downsample images, provide a lossless mode or promise a target size.

The supported path includes ordinary 8-bit RGB/grayscale JPEG, unfiltered images and Flate images with supported TIFF/PNG predictors. Resource traversal includes inherited resources and nested forms, with shared images processed once. Transparency masks, color-sensitive or unsupported image encodings are preserved with reasons. See the [compression support matrix](docs/COMPRESSION.md) for the exact boundaries.

A changed image is kept only when its stream becomes smaller. If the complete rewritten PDF does not shrink, or no supported image can improve, PDFToys saves an unchanged copy and labels it **Unchanged**. A successful file operation does not imply that compression reduced its size.

PDFToys refuses rewriting signed PDFs and PDFs with unsupported or conflicting XMP metadata. Merge, Split and Organize also refuse page-copy workflows involving encryption, interactive forms, bookmarks, named destinations/attachments, accessibility tags, layers or custom page labels that the current engine cannot safely retain. Unlock an encrypted copy first where appropriate. This protects originals rather than silently dropping those document features. It is not a claim of universal PDF feature preservation or PDF/A compliance.

Markdown export extracts text; it does not recreate page layout or recognize scanned text. SVG conversion uses the current rendering path and is not a vector-preservation guarantee. OCR, page thumbnails, undo/multiselection, effective-DPI compression, target-size compression and modern Windows 11 main-menu integration remain follow-up work.

## Office conversion

Supported extensions are `.doc`, `.docx`, `.xls`, `.xlsx`, `.ppt` and `.pptx`. Install the corresponding desktop Office application, and close that application before conversion: PDFToys refuses to automate an existing Office session.

Conversion runs through a separate PDFToys process on an STA thread, with a two-minute timeout per conversion. Macros are disabled; documents are opened read-only, and Word/Excel link updating is suppressed. Missing applications, failed automation and timeouts return errors instead of blocking the main interface indefinitely.

Cancellation waits for the current Office step or its timeout. A timeout stops the owned PDFToys worker, without killing unrelated Office processes; an Office process or unanswered prompt may still require manual attention before retrying. Actual Office behavior depends on the installed application and document, so automated worker tests do not replace real Office compatibility checks.

## Building from source

Developer requirements:

- .NET 10 SDK compatible with `global.json` for the app and tests.
- Visual Studio 2022 C++ build tools with ATL support for the native shell extension.
- Inno Setup 6 to compile the installer.

Build and test the managed projects:

```powershell
dotnet restore tests/PDFToys.Core.Tests/PDFToys.Core.Tests.csproj
dotnet restore tests/PDFToys.App.Tests/PDFToys.App.Tests.csproj

dotnet build src/PDFToys.App/PDFToys.App.csproj -c Release
dotnet test tests/PDFToys.Core.Tests/PDFToys.Core.Tests.csproj -c Release
dotnet test tests/PDFToys.App.Tests/PDFToys.App.Tests.csproj -c Release
```

CI uses project-scoped Windows builds and tests. Deterministic PDF fixtures cover compression, rendering/fidelity, page rotations, output safety, batch identities, background jobs and desktop workflows. Candidate readiness is established by the final integrated verification record, not by this feature list.

## Publishing and packaging

`VERSION` is the version source for managed/native binaries, installer and release artifacts. To prepare the 0.2.0 candidate locally:

```powershell
.\scripts\build-release.ps1
```

The script runs the tests, publishes the self-contained `win-x64` app, builds the native shell extension, stages the package, runs packaged smoke checks and compiles the installer. Supply `-MsBuildPath` or `-IsccPath` when the required tools are not discoverable. `-SkipTests` is available for an already verified build; it is recorded in the manifest and is not a substitute for release validation.

Outputs are prepared under `artifacts/releases/0.2.0`:

- `PDFToys-0.2.0-setup.exe`
- `PDFToys-0.2.0-win-x64.zip`
- `SHA256SUMS.txt`
- `release-manifest.json`, including source commit, file sizes/hashes, test-script selection and installer signing status.

Packaging does not sign or publish the release automatically. Configure and verify Authenticode signing separately before distribution; do not treat an unsigned artifact as signed. Installer install/upgrade/uninstall checks, desktop smoke, real Office coverage and signing status belong in the final release readiness evidence.

The lower-level scripts remain available:

```powershell
.\scripts\publish.ps1
.\scripts\build-shell-extension.ps1
.\scripts\package-layout.ps1
iscc installer\PDFToys.iss
```

## Developer shell-extension setup

The installer registers and unregisters the Explorer context menu. For a manual build, run the packaging scripts first; `PDFToys.App.exe` must remain beside the shell DLL. Register the packaged DLL from an elevated PowerShell session in the repository root:

```powershell
.\scripts\register-shell-extension.ps1 -DllPath ".\artifacts\package\PDFToys.ShellExtension.dll"
```

To unregister a manually installed extension:

```powershell
.\scripts\register-shell-extension.ps1 -Action unregister -DllPath ".\artifacts\package\PDFToys.ShellExtension.dll"
```

## Security and data handling

- **Local processing:** PDF and image work runs locally. Office conversion launches the installed Office application through the bounded worker described above.
- **Passwords:** Opening passwords stay in memory for the current workflow; they are excluded from settings and diagnostic logs.
- **Recovery files:** Guarded replacement leaves a distinct `.pdftoys.bak` recovery file beside the original; its exact path appears in the result warning. Keep it until the replacement has been checked.
- **Headless logs:** Context-menu headless runs write diagnostics to `%LocalAppData%\PDFToys\logs`. Logs contain file paths and error messages.
- **Settings:** User preferences are stored in `%LocalAppData%\PDFToys\settings.json`. A remembered workspace does not auto-select files when opening a tool from Home.

## Troubleshooting / reporting issues

- **Explorer menu missing:** On Windows 11, choose **Show more options**. If the classic PDFToys menu is still missing, reinstall or repair the installation. Developers can use the registration script above.
- **Compression unchanged:** Read the image skip reasons and sizes in Results. Text-only PDFs, already optimized images and unsupported image types may have no useful reduction.
- **Unsupported document features:** The original is preserved. Use a suitable unsigned/unlocked copy or a tool that explicitly supports those document features.
- **Office conversion fails:** Close the corresponding Office application, resolve any Office prompt, and retry. Confirm that the application is installed and licensed for use.
- **Context-menu operation fails:** Inspect `%LocalAppData%\PDFToys\logs` for the latest diagnostic log.
- **Report a bug:** Use [GitHub Issues](https://github.com/MohammedSaad114/PDFToys/issues). Include PDFToys/Windows versions, reproduction steps, expected/actual results and relevant errors. Remove private file paths before sharing logs.

## Changelog and license

See the [changelog](CHANGELOG.md) for release notes. PDFToys is distributed under the Apache License 2.0; see [LICENSE](LICENSE) and [NOTICE](NOTICE) for third-party attributions.
