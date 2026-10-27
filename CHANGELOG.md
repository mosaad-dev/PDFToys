# Changelog

## 0.2.0 (Unreleased)

### PDF correctness and safe outputs

- Decode supported RGB/grayscale JPEG and ordinary 8-bit Flate/predictor images before recompression; traverse nested/inherited resources and process shared objects once.
- Preserve masked, color-sensitive and unsupported image streams with explicit reasons. Keep a byte-identical unchanged output when image changes cannot reduce the complete PDF.
- Report original/candidate/final sizes, savings, changed/skipped image counts and typed unchanged/partial/cancelled outcomes.
- Commit validated candidates to unique names without overwriting existing inputs or outputs; separate equal-basename inputs during batch conversions and exports.
- Guard Protect/Unlock replacements with staged PDF validation, stable Windows file identity checks and an atomic recovery backup. Report the generated copy when replacement cannot finish safely.
- Preserve existing/inherited page rotations and compose requested rotation deltas. Organize preview displays pending rotation.
- Refuse signed PDFs, unsupported/conflicting XMP metadata, and page-copy operations that cannot preserve forms, bookmarks, attachments/named destinations, tags, layers or custom page labels.

### Desktop workflows

- Add a single-PDF picker, input switching and page count to Split, including custom-range validation.
- Run every tool through background jobs with immutable inputs/options, progress, safe cancellation and duplicate-run protection. Window close waits for the current safe cancellation boundary.
- Share conversion/export orchestration between GUI and CLI; preserve partial outputs and expose per-file results.
- Group Home into Document tools and Convert & export, with a secondary About action. Adapt layouts to the 400-logical-pixel minimum using wrapping controls and scrolling.
- Use consistent input/options/output/action/status layouts and theme-aware colors. Results expose Open, Show in folder, Copy path and Retry failed.
- Preserve successful results during failed-only retries. Protect/Unlock retry captures the corrected password and current output choice.
- Start Home tools with empty selections; remembered folders are no longer implicitly enumerated or selected. Explicit Explorer/startup inputs still load.

### Office and delivery

- Isolate Office automation in an STA PDFToys child process with a two-minute timeout. Refuse existing Office sessions; disable macros and suppress Word/Excel link updates.
- Keep the UI responsive while Office finishes its current step; timeout stops the owned PDFToys worker without killing unrelated Office processes. Office itself may need manual attention afterward.
- Align the SDK/runtime build on .NET 10 and use `VERSION` for the 0.2.0 managed/native/installer version.
- Add a local release pipeline for the self-contained app, native extension, setup executable, portable ZIP, checksums and manifest under `artifacts/releases/0.2.0`.
- Add deterministic PDF fixtures and regression coverage for decoding/fidelity, safety, rotations, collisions, job lifecycle, GUI/CLI parity and desktop results.

## 0.1.0

- Merge, split, compress, protect, unlock, and organize pages
- Convert to PDF (images, text, SVG, Office via COM)
- Export PDF to JPG, JPEG, PNG, and Markdown (text extraction)
- Windows shell extension context menu
- Headless CLI for context-menu operations
- User settings persistence (workspace, output mode, compression level)
