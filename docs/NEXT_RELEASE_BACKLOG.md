# PDFToys next-release analysis and branch backlog

Reviewed 19 October 2026. Proposed next version: **0.2.0**; this is a planning suggestion, not an existing release commitment.

## Recommendation

Make the next release about **correct compression, safe output files, and a complete desktop workflow**. PDFToys already has a useful feature set: merge, split, organize, protect/unlock, image/text/SVG/Office conversion, PDF image/text export, and Explorer integration. The largest opportunity is making those tools reliable, responsive, and consistent before adding more tools.

The backlog below contains **48 separately scoped branch proposals**. Branch names are suggestions; no branches or issues were created. Each item has a completion condition and dependencies so work can be assigned without treating everything as independent.

The [0.2.0 implementation plan](NEXT_RELEASE_IMPLEMENTATION_PLAN.md) selects 18 of these branches and defines agent assignments, file ownership, execution waves, and release gates.

## Scope and validation

- Primary checkout: `C:\Users\mohme\Desktop\Coding\Public projects\PDFToys`, HEAD `8c67ff6`, including its existing uncommitted SDK/package/CI changes. Those changes were left intact.
- Secondary checkout: `C:\Users\mohme\Desktop\final\final`, HEAD `fd6ae14`. It is another PDFToys checkout, not a separate product. A source/configuration comparison found differences in UI, packaging, Office conversion, shell code, and dependency versions. The compression implementation and the core merge/split/organize/protection services match. The recommendations use the primary checkout; this was not a complete independent audit of the secondary checkout.
- Reviewed Core, Avalonia views/view models, command routing, headless processing, tests, native Explorer integration, packaging, and the supplied home screenshot. No live desktop interaction, installer installation, or native shell build was performed.
- Restored dependencies from the existing local NuGet cache. The .NET 10 builds succeeded. The existing suites passed: **58 Core tests + 20 App tests = 78 tests**. The restricted test runner could not connect to its test host; the same built tests passed with normal local process access.
- Some Office tests return early when Office is absent, so the passing count does not establish real Office conversion coverage. Existing tests also do not establish visual fidelity or metadata preservation.
- A temporary diagnostic program under ignored `artifacts/release-audit/` generated PDF fixtures and exercised the current Core code. Its observations are listed below. This is an audit aid, not a shipped implementation or a substitute for a regression corpus.

## Findings that determine priority

| Finding | Evidence and consequence | Confidence |
| --- | --- | --- |
| PDF image streams are treated as standalone image files | `CompressionService.cs:70-72` passes the stored stream to `SKBitmap.Decode`. In the generated PNG-based fixture, two decode attempts returned no image; input and output were both **789,722 bytes**, while the operation returned success. PDF filters, predictors, and image dictionaries need explicit handling. | Reproduced for the fixture; broader format coverage needs tests |
| Compression removes transparency | `CompressionService.cs:126` removes `/SMask` after a successful replacement. A generated JPEG with a soft mask had that mask before compression and lacked it afterward. Smaller output alone does not establish correct output. | Reproduced |
| Compression processes images by page, without deduplication | The service only visits image references directly in each page's XObject dictionary. It has no visited-object set or Form XObject traversal. The two-page JPEG fixture caused two compression calls. Shared objects can be recompressed repeatedly; nested and inline images need coverage. | Source finding; two calls observed |
| Organize resets existing page rotation | `RearrangeService.cs:78` assigns the requested rotation, while UI items start at zero. A source page at **90 degrees became 0 degrees** when arranged with no requested rotation. | Reproduced |
| Split cannot select a file from the normal home flow | `SplitView.axaml` has no picker/drop target and `SplitViewModel` has no file dialog dependency. Only startup/workspace routing supplies a file. An ordinary launch leaves the tool without a way to select input. | Confirmed in source |
| Heavy work blocks the UI thread | Compress/Protect/Unlock/Split/Organize call synchronous services directly. Merge does so after file dialogs. Convert uses `Task.Yield()` and then synchronous work on the captured context. There is no cancellation contract. | Confirmed execution paths; no live freeze timing measured |
| Output files can overwrite prior results | `ServiceBase.cs:135` produces fixed names; `OutputFileHelper.cs:29,70` moves with overwrite enabled. Merge's normal same-folder path is a useful exception: it already chooses a unique name. | Confirmed in source |
| Replace-original can proceed without its backup | `OutputFileHelper.cs:62-70` catches backup failure and continues with replacement; the fixed backup name can itself overwrite an older backup. | Confirmed in source |
| Same-name inputs can corrupt batch results | GUI and CLI combine all conversions in a shared temporary directory. Two inputs such as `A\report.png` and `B\report.png` both become `report_Converted.pdf`; the second can replace the first and both merge entries then point to that same file. Image/Markdown exports also use basename-only prefixes. | Deterministic source path collision; not separately executed |
| Organize preview does not represent pending edits | `OrganizePagesViewModel.cs:225` renders the original source page and passes the pending rotation only as text. A failed final output move uses `result.ErrorMessage`, which is empty after a successful core operation. | Confirmed in source |
| Document fidelity needs a separate contract | Merge, split, rearrange, and unlock create new documents by copying pages. Document-level bookmarks, attachments, forms, tags, and metadata have no explicit preservation policy. Unlock should be assessed for modification of the original document structure instead of page reconstruction. | Compatibility risk; losses must be established with fixtures |
| UI does not adapt to supported window sizes | The window permits 400-pixel width, but the home grid is fixed at 600 pixels. Organize has an unwrapped eight-button toolbar. Several views use fixed colors and long inline status messages. | Confirmed layout constraints; full DPI/accessibility audit remains |
| Tests can pass without demonstrating useful compression | Compression assertions check output size `<=` input and page count. A byte-for-byte fallback copy satisfies them. The test named `Rearrange_WithRotation_AppliesRotation` only checks page count. | Confirmed in tests |

Useful behavior to retain: individual replacement images are accepted only if smaller; the compression service falls back to the original bytes if its output is larger; batch tools already report some failures; output mode and compression preference persistence exist; the app follows the system theme. Improve these behaviors instead of reimplementing them inconsistently.

## How compression should work

1. **Inspect the document.** Detect encryption, signatures, image types, masks, object reuse, page geometry, and whether useful image reduction is possible. Return clear unsupported/skipped reasons.
2. **Offer an explicit lossless path.** Optimize eligible streams and document structure without changing image samples or rasterizing pages. An already optimized PDF can correctly produce no reduction.
3. **Use PDF-aware image processing for lossy profiles.** Resolve filter chains and predictors; respect color spaces, `/Decode`, masks, transparency, and bit depth. Skip unsupported images with a diagnostic until support is tested. Do not silently discard a mask to make a JPEG replacement fit.
4. **Visit resources safely.** Recurse through Form XObjects, resolve relevant inherited resources, guard cycles, and process each indirect image object once. Treat inline images as a deliberate supported case or a reported limitation.
5. **Downsample from effective placement resolution.** Account for how large an image is drawn on the page and its largest relevant placement if shared. Do not upsample. Preserve lossless treatment for line art and one-bit scanned text where lossy JPEG would harm readability.
6. **Write a candidate to a unique temporary path.** Reopen and validate it; commit only a useful valid result through the common output service. Keep the original if there is no worthwhile improvement.
7. **Report what happened.** Show original size, output size, bytes/percentage saved, changed and skipped images, quality profile, and `Reduced`, `Unchanged`, `Cancelled`, or `Failed`. The unchanged case should say “Already optimized / no useful reduction,” with the actual reason where known.

Suggested starting profiles, to be calibrated against fixtures rather than treated as guaranteed settings:

| Profile | Proposed behavior |
| --- | --- |
| Lossless | No image quality loss or downsampling; structural/stream optimization only |
| Balanced | Conservative photo recompression and a possible 150–200 effective DPI target; retain text, vectors, transparency, and line-art fidelity |
| Small file | More aggressive photo recompression and a possible 100–150 DPI target, with a visible quality tradeoff |
| Custom, later | DPI, JPEG quality, optional grayscale, and an attainable target-size workflow |

**Backend decision:** evaluate PDFsharp's capabilities and a qpdf adapter before committing to a large custom optimizer. PDFsharp already enables compression by default, so changing its compression switches alone will not solve this implementation's decoding gaps. [PDFsharp FAQ](https://docs.pdfsharp.net/PDFsharp/Overview/FAQ.html).

qpdf can recompress streams and generate object streams; its image optimization does not resample images. It is a candidate for structural optimization, not a complete replacement for a DPI-aware image pipeline. [qpdf optimization documentation](https://qpdf.readthedocs.io/en/stable/cli.html#optimizing-file-size).

Ghostscript is worth benchmarking as an optional backend, but its PDF rewrite can change content and omit information even where pages look similar. Treat fidelity and redistribution terms as explicit decision criteria before adopting it. [Ghostscript optimization notes](https://ghostscript.com/blog/optimizing-pdfs.html).

## Branch backlog

Priority: **P0** = fix before the next public release; **P1** = high-value release candidate; **P2** = following release; **P3** = larger future work. Sizes are relative: **S** = localized, **M** = several components, **L** = substantial implementation/testing, **XL** = separate initiative. They are not delivery estimates. Dependencies identify required integration order, not a prohibition on developing against agreed interfaces.

### Correctness and file safety

| ID | Suggested branch | Priority / size | Scope and acceptance criteria | Depends on |
| --- | --- | --- | --- | --- |
| F01 | `fix/safe-output-writes` | P0 / L | One output service for unique names, explicit overwrite, validated temporary writes, cleanup, and safe replacement. Originals survive write/validation/backup failures; replacing an original requires a successful recoverable backup or an explicitly defined alternative. Avoid source/output aliasing and overwriting the only backup. | None |
| F02 | `fix/batch-name-collisions` | P0 / M | Assign a unique intermediate/output identity per source. Combine and export equal basenames from different folders without losing, repeating, or replacing content; preserve selected input order. Cover GUI and CLI paths. | F01 interface |
| F03 | `fix/existing-page-rotation` | P0 / S | Store source rotation and apply requested rotation as a delta. Saving without changes retains appearance; cover 90/180/270-degree source pages, inherited rotations, and reordered/duplicated pages. | None |
| F04 | `fix/split-file-selection` | P0 / S | Add Select PDF and change-file actions using the dialog service. Home launch, Explorer launch, cancelled selection, invalid input, and switching input all work; show page count. | None |
| F05 | `fix/organize-preview-and-errors` | P1 / S | Preview applies pending rotation and states page inclusion/order clearly. A failed output move shows a useful error and recoverable output location, never an empty success message. | F03; integrate F01 |

### Compression

| ID | Suggested branch | Priority / size | Scope and acceptance criteria | Depends on |
| --- | --- | --- | --- | --- |
| C01 | `fix/pdf-image-decoding` | P0 / L | Replace raw-stream assumptions with supported PDF image decoding; retain masks and compatible dictionary semantics. Handle or explicitly skip Flate/predictor images, JPEG, gray, indexed, CMYK/ICC, one-bit, and transparency cases. Render comparisons verify appearance. | Q01 fixtures; F01 |
| C02 | `fix/compression-resource-traversal` | P1 / M | Traverse nested forms and relevant inherited resources with cycle guards and a visited-object cache. A shared image is processed once, nested images are found, and inline-image support/limitations are explicit. | Agree C01 decoder interface |
| C03 | `spike/lossless-pdf-optimizer` | P1 / M | Benchmark PDFsharp and a qpdf adapter on the same corpus. Record size, fidelity, runtime, deployment size, licenses/notices, and preservation behavior; choose a backend with a written decision. This branch is an evaluation, followed by a separate production adapter if selected. | Q01 |
| C04 | `feat/compression-profiles` | P1 / L | Introduce Lossless/Balanced/Small file profiles with calibrated JPEG settings, effective-DPI downsampling, grayscale as an explicit choice, and safe line-art handling. Text and vectors stay searchable/scalable. | C01, C02; C03 decision for lossless path |
| C05 | `feat/compression-results` | P1 / M | Return and display before/after bytes, percentage saved, changed/skipped counts, warnings, and unchanged reasons. The PNG diagnostic cannot be presented as a successful reduction. Preserve the no-larger-output guard. | A01 result contract |
| C06 | `feat/compression-quality-preview` | P2 / L | Render an original/candidate comparison with zoom and sampled-page navigation before committing a lossy result. Pending settings and the final output must match. | C04, A06 |
| C07 | `feat/compression-target-size` | P2 / L | Offer “try to fit below X MB” with bounded attempts, a minimum acceptable quality, cancellation, and an honest unattainable result. Keep the best validated candidate; never promise an arbitrary size. | C04, C05, A02 |

### Architecture and reliability

| ID | Suggested branch | Priority / size | Scope and acceptance criteria | Depends on |
| --- | --- | --- | --- | --- |
| A01 | `refactor/operation-contracts` | P1 / M | Define immutable job inputs, typed status/error codes, warnings, all output paths, per-file results, progress, and cancellation. Distinguish success, partial success, unchanged, cancellation, and failure without parsing status strings. | None; agree before dependent branches |
| A02 | `refactor/background-operations` | P0 / L | Run PDF work off the UI thread through a shared runner. Show stage/file/page progress, cancel between safe units, prevent duplicate runs and conflicting edits, and handle close/navigation while working. Office operations require the dedicated STA/worker design in D03. | A01; F01 for cleanup/commit |
| A03 | `refactor/shared-gui-cli-workflows` | P1 / M | Extract duplicated combine/convert/export orchestration from the view model and headless executor. The same input job produces the same ordering, output policy, validation, and partial-failure behavior from both entry points. | A01, F02 |
| A04 | `refactor/file-selection-and-commands` | P1 / M | Reuse file-list models, normalized path deduplication, validation, selection rules, and the existing MVVM toolkit's commands. Remove duplicated private command implementations; awaited errors reach the common result path. | A01/A02 command conventions |
| A05 | `refactor/durable-user-settings` | P1 / M | Atomic settings writes, a schema version, corruption recovery, guarded loading/saving, and defined multi-instance updates. Settings failures cannot crash a completed operation. Add a settings page for output defaults, theme choice, compression defaults, and reset. | F01 conventions; U02 styles |
| A06 | `refactor/pdf-rendering-lifecycle` | P1 / M | Reuse document/render sessions where supported, bound caches and resolution, render asynchronously, cancel obsolete requests, and dispose preview bitmaps/native objects. Repeated previews avoid rereading the entire file and do not steadily grow native memory. | A02 |

### UI and UX

| ID | Suggested branch | Priority / size | Scope and acceptance criteria | Depends on |
| --- | --- | --- | --- | --- |
| U01 | `ui/responsive-app-shell` | P1 / M | Replace the fixed 600-pixel grid with adaptive tool cards and scrolling; group document tools and conversion tools; move About into secondary navigation. Wrap toolbars and keep primary actions reachable at 400-pixel width and 100/150/200% scaling. | None; coordinate U02 |
| U02 | `ui/shared-tool-layout` | P1 / M | Shared title/help/input/options/output/action/status components, spacing and semantic theme resources. Replace hard-coded status colors and “Execute” wording; distinguish errors, warnings, and success consistently in light/dark themes. | None; publish components early |
| U03 | `feat/explorer-file-drop` | P1 / M | Drop supported Explorer files onto Home or a tool. Show accepted/rejected inputs with reasons; preserve order and prevent duplicate entries. Existing merge row dragging is retained as a separate interaction. | A04, U02 |
| U04 | `ui/operation-results` | P1 / M | Result rows show each input, outcome, output path, warnings, and relevant statistics. Add Open file, Show in folder, Copy path, and Retry failed. Buttons resolve the actual committed output; successes remain visible during partial failures. | A01, F01; C05 for compression data |
| U05 | `ui/keyboard-and-accessibility` | P1 / M | Keyboard file selection/reordering, clear focus, sensible tab order, labeled icon buttons, screen-reader names and announced progress/results. Audit contrast/high-contrast and large-text behavior across tool views. | U01, U02 |
| U06 | `feat/retain-tool-workspace` | P1 / M | Preserve selected files/options when returning Home; show the active workspace rather than silently selecting all PDFs from a hidden folder. Allow using completed outputs in another tool and prompt only for meaningful unsaved edits. | A01, A04, U04 |
| U07 | `ui/organize-page-thumbnails` | P1 / L | Virtualized thumbnails replace the number-only page list. Add drag reordering, zoom, original/new page labels, inclusion state, and correctly rotated previews. Large documents remain usable through lazy rendering. | F03, F05, A06 |
| U08 | `feat/organize-undo-and-multiselect` | P2 / L | Undo/redo, range/multi-selection, batch rotation/deletion, duplicate pages, and extract selection. Commands preserve source mapping and have predictable selection/keyboard behavior. | U07; A01/F01 output conventions |
| U09 | `ui/merge-file-workflow` | P1 / M | Add remove/clear, keyboard Move Up/Down, natural-sort choices, visible order/page counts, input warnings, and explicit output location/name. Preview ordered content and disable Merge until valid. | A04, F01, U02 |
| U10 | `ui/split-range-workflow` | P1 / M | Live range validation against page count, planned output preview, every-N-pages and individual-page modes, keep/extract selected ranges, and naming/location controls. Explain overlaps and out-of-range pages before writing files. | F04, F01, U02 |

### Document capabilities and conversion

| ID | Suggested branch | Priority / size | Scope and acceptance criteria | Depends on |
| --- | --- | --- | --- | --- |
| D01 | `fix/document-feature-preservation` | P1 / L | Define preservation by operation for metadata, bookmarks, links, annotations, forms, attachments, page labels, and tags. Test and preserve what the backend supports, remap page references, and surface unsupported changes. Detect signed PDFs before transformations that would invalidate their signatures. | Q01, F01; A01 warnings |
| D02 | `feat/password-and-encryption-controls` | P1 / M | Add show/hide and confirmation for new passwords, precise wrong-password/already-unlocked handling, optional per-file retry in batches, and an explicit supported encryption/permission policy. Verify encryption by inspecting output and opening with/without passwords; do not infer strength from library defaults. | A01, F01, D01 unlock behavior |
| D03 | `fix/office-conversion-worker` | P1 / L | Use an owned STA worker/process with bounded execution, explicit automation macro policy, disabled automatic external-link updates where supported, clear Office availability checks, and complete COM cleanup including collection objects. Fail/cancel without terminating a user's unrelated Office instances. | A01; integrate A02 |
| D04 | `feat/configurable-pdf-export` | P2 / M | Separate To PDF and From PDF flows, treat JPG/JPEG as the same format with extension preference, and add page ranges, DPI, image quality, background/color choices, plus text export naming/packaging options. Output preview estimates page/file count. | F02, A03, U02 |
| D05 | `feat/image-to-pdf-layout` | P2 / M | Allow mixed JPEG/PNG input ordering, A4/Letter/original page sizes, margins, fit/fill, orientation, and EXIF orientation. Add TIFF/WebP/BMP only where decoding is verified. A thumbnail/size preview matches output. | A03, F02, U02 |
| D06 | `feat/vector-svg-conversion` | P2 / M | Evaluate a vector-preserving SVG-to-PDF path instead of the current bitmap intermediate. Verify viewBox, scale, clipping, fonts, gradients, and large artwork; state unsupported SVG features. | Q01, F01 |
| D07 | `feat/text-and-markdown-conversion` | P2 / L | Handle long unbroken lines, Unicode/font fallback and encoding choices. Make plain text extraction clear in the UI; separate it from optional layout-aware Markdown and rendered Markdown input. Add multi-column/reading-order fixtures. | Q01, D01, U02 |
| D08 | `feat/local-ocr` | P2 / XL | Optional local OCR with language packs, selected pages, progress, and a searchable text layer. Keep scanned page appearance and explain confidence/limitations; manage language-pack download/storage explicitly. | A02, A06, Q01 |
| D09 | `feat/document-properties` | P2 / M | Inspect/edit title, author, subject, keywords, page sizes, protection, and embedded-feature inventory; allow deliberate metadata removal. Define exactly what “remove metadata” removes. | D01, F01 |
| D10 | `feat/watermarks-and-page-numbers` | P2 / M | Page numbers, headers/footers, and text/image watermarks with page ranges, opacity, placement, and preview. Respect page boxes and rotations; preserve searchable content. | F03, D01, A06 |
| D11 | `feat/scan-cleanup` | P3 / L | Explicit crop/deskew and blank-page detection with adjustable confidence and preview before deletion. Keep processing reversible until save; distinguish cropping from removal of hidden content. | U07, U08, Q01 |
| D12 | `spike/true-pdf-redaction` | P3 / XL | Evaluate a backend that removes underlying text/images and relevant residual data. Gate any production feature on extraction/search/object-inspection tests. A painted rectangle is not an acceptable implementation. | D01, Q01; separate backend decision |

Office macro configuration is a concrete part of D03: the current converter sets visibility/alerts but does not set `AutomationSecurity`. Microsoft's documented API controls how Excel handles programmatically opened files; alert suppression is not a substitute. This finding does not establish that a particular user document executed a macro. [Microsoft Excel AutomationSecurity](https://learn.microsoft.com/en-us/office/vba/api/excel.application.automationsecurity).

### Explorer and CLI integration

| ID | Suggested branch | Priority / size | Scope and acceptance criteria | Depends on |
| --- | --- | --- | --- | --- |
| W01 | `fix/shell-paths-and-command-contract` | P1 / M | Replace fixed `MAX_PATH` buffers with sized handling and validate actual path lengths. Support large selections via a versioned manifest/IPC instead of an unbounded command line. Validate contract versions, missing values, unsupported verbs, Unicode/UNC/long paths, and command-ID allocation. | A03; versioned schema agreed early |
| W02 | `feat/windows11-context-menu` | P2 / L | Evaluate `IExplorerCommand` plus package/sparse-package registration for the main Windows 11 menu while preserving the Windows 10 classic path. Verify registration, selection behavior, upgrade/uninstall, and latency. | W01, Q04 |
| W03 | `feat/cli-results-and-shell-feedback` | P1 / M | Document CLI options, provide help/validation and machine-readable results, and expose consistent job exit codes. Explorer-initiated headless work gets visible completion/failure with a way to locate outputs. Add bounded diagnostic logs; log-write failure cannot turn an otherwise completed job into an unhandled crash. | A01, A03, U04 |

Microsoft documents the modern menu route through `IExplorerCommand` and `windows.fileExplorerContextMenus`; it requires a registration/deployment design, not just replacing an interface name. [Explorer integration documentation](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer).

### Tests, delivery, and maintenance

| ID | Suggested branch | Priority / size | Scope and acceptance criteria | Depends on |
| --- | --- | --- | --- | --- |
| Q01 | `test/pdf-regression-corpus` | P0 / M | Add synthetic/redistributable fixtures for filters, masks, color spaces, shared/nested images, rotations, fonts, encryption, forms, attachments, and same-name paths. Assert structure, extraction and meaningful size reduction where expected; use render comparisons with explicit lossy tolerances. Retain exact/semantic checks where render differences could hide damage. | None; fixes add their regression cases |
| Q02 | `chore/sdk-and-build-consistency` | P1 / S | Finish/review the existing .NET 10 migration as its own change. Align SDK/CI/publish/test package policy; fix obsolete PDFsharp unit conversions and Avalonia placeholders; mark/isolate Windows-only code. Fresh restore/build/test succeeds without relying on old `bin/obj` content. | Existing local work; do not overwrite it |
| Q03 | `ci/release-packaging` | P1 / M | A release job builds app and shell extension, stages a clean package, builds the installer, runs smoke checks, and publishes versioned artifacts/checksums. One version source feeds app/About/installer; include native dependencies and notices. Support signing all distributed binaries and installer when signing credentials are configured. | Q02; release credentials for signing |
| Q04 | `fix/installer-upgrade-and-shell-options` | P2 / M | Test install/upgrade/repair/uninstall and locked Explorer DLL handling. Offer shell integration as an explicit install option and evaluate a portable/no-shell distribution. Detect registration failure clearly and retain user document files. | Q03; coordinate W02 |
| Q05 | `perf/large-document-benchmarks` | P1 / M | Establish reproducible benchmarks for scanned PDFs, many-page documents, large images, and batches. Track elapsed time, peak managed/native memory, UI responsiveness, and size/fidelity outcomes. Enforce practical page/pixel/cache limits and prevent regressions. | Q01; benchmark A02/A06/C04 as delivered |

## Suggested release scope and merge order

**0.2.0 implementation scope:** Q02, Q01, A01, F01, F03, F04, C01, C02, F02, A03, A02, D03, C05, F05, U02, U01, U04, and Q03: 18 branches. The [implementation plan](NEXT_RELEASE_IMPLEMENTATION_PLAN.md) is the detailed execution schedule. A03 and D03 are included because shared GUI/CLI behavior and safe Office execution support the background-operation migration. Complete the SDK migration before establishing the shared worker baseline.

**Selected UX work:** F05, U01, U02, and U04 complete the existing tool workflows. Defer the full U09 merge redesign, thumbnail editing, OCR, redaction, and target-size compression. Treat focused D01 preservation checks and Q05 performance checks as release validation without marking their complete roadmap branches finished.

**Following release:** C03/C04, A04/A05/A06, U03/U05/U06/U07/U09/U10, the wider D01/D02 work, W01/W03, and the full Q05 benchmark initiative. C03 may be brought forward as a bounded contingency if common-format compression cannot be implemented safely with the installed libraries. Promote any preservation or Office handling issue that is demonstrated to lose data or execute unwanted document behavior before shipping affected workflows.

Recommended sequencing:

1. Integrate the reviewed SDK baseline (Q02) and fixture conventions (Q01).
2. Agree the operation/result/progress contracts (A01) and output service (F01).
3. Develop F02, F03, F04, C01, and C02 in separate branches. C01/C02 share a small decoder/resource-walker interface; agree it before both branches edit `CompressionService`.
4. Integrate A02 and C05. Complete F05 against the rotation/output fixes.
5. Integrate shared UI components (U02), then shell layout (U01) and results (U04). Avoid having every tool branch restyle shared files independently.
6. Complete the release pipeline and regression pass; ship only profiles/features actually supported by fidelity evidence.

## Keeping branches independently developable

- Use one ID per pull request. Use category prefixes directly: `fix/`, `refactor/`, `feat/`, `ui/`, `test/`, `chore/`, `ci/`, `perf/`, and `spike/`.
- Agree shared interfaces first and keep adapters backward compatible while migrations are in progress. Add new result fields in one contract branch; migrate callers in small follow-ups.
- Suggested ownership boundaries: F01 owns output commits; A01/A02 own job contracts/execution; C01 owns image decoding; C02 owns PDF resource traversal; U02 owns shared view components; A03 owns GUI/CLI orchestration; W01 owns the launch contract; Q03 owns packaging.
- C03 and D12 are explicitly spikes. Their completion is a measured decision, not an unreviewed engine replacement.
- Feature branches should modify their own view/strategy/fixtures. Touch shared navigation, DI, project files, and styles only for a minimal integration patch after the shared branch lands.
- Rebase dependent work after its prerequisite merges. Parallel development does not remove dependencies or merge conflicts.
- Keep regression tests with the behavior fix; Q01 supplies fixtures/harness rather than becoming a long-lived separate testing branch.

## Release acceptance checklist

- All existing tests plus added regressions pass on the intended Windows/.NET configuration.
- The diagnostic PNG/JPEG/mask/rotation cases have explicit expected outcomes; a fallback copy cannot masquerade as reduction.
- Visual and structural checks cover transparency, color, text extraction, rotations, and advertised document-feature preservation.
- Cancelling or failing an operation leaves originals and pre-existing outputs intact; replace-original recovery is verified.
- Duplicate basenames, existing outputs, read-only folders, invalid/encrypted inputs, Unicode/long paths, and partial batches have usable outcomes.
- Home → select input → configure → run → locate/open output works for every tool, including Split.
- UI remains interactive with a large document and all primary controls are reachable at the supported minimum width/scaling.
- A freshly installed packaged build can run representative operations, includes native dependencies/notices, and registers/unregisters Explorer integration correctly when selected.

## Starting code references

- [Compression implementation](../src/PDFToys.Core/Services/CompressionService.cs) and [Skia image adapter](../src/PDFToys.Core/Services/ImageProcessing/SkiaImageCompressor.cs).
- [Output helper](../src/PDFToys.App/Services/OutputFileHelper.cs) and [Core output naming](../src/PDFToys.Core/Services/ServiceBase.cs).
- [Split UI](../src/PDFToys.App/Views/SplitView.axaml), [Organize view model](../src/PDFToys.App/ViewModels/OrganizePagesViewModel.cs), and [rearrange service](../src/PDFToys.Core/Services/RearrangeService.cs).
- [Convert/export view model](../src/PDFToys.App/ViewModels/ConvertExportViewModel.cs) and [headless executor](../src/PDFToys.App/Services/HeadlessConversionExecutor.cs).
- [Native context menu](../src/PDFToys.ShellExtension/PDFContextMenu.cpp), [CI](../.github/workflows/ci.yml), and [installer](../installer/PDFToys.iss).
