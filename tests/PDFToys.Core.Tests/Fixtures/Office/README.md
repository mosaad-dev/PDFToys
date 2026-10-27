# Office conversion test fixtures

These generated documents contain only `PDFToys ... worker fixture` text and no user documents or external media.

- `sample.docx`: a minimal OpenXML ZIP package.
- `sample.xlsx`: generated with openpyxl.
- `sample.pptx`: generated with python-pptx and its bundled blank slide layout.

Build `PDFToys.App` before running `OfficeWorkerTests`. The tests require Windows and the corresponding desktop Office application. They skip when the apphost or Office is unavailable, or the Office application is already open.
