# Generated Office worker fixtures

These tiny documents contain only the text `PDFToys ... worker fixture`. They are generated test data, with no user documents or external media.

- `sample.docx`: minimal OpenXML ZIP package written by the test audit.
- `sample.xlsx`: generated with openpyxl.
- `sample.pptx`: generated with python-pptx and its bundled blank slide layout.

`OfficeWorkerTests` starts the real PDFToys apphost, which starts its own STA child worker. Build `PDFToys.App` before running these tests. Integration tests explicitly skip when Windows, the matching Office application, or the apphost is unavailable, or when that Office app is already open. The separate missing-Office test explicitly skips when PowerPoint is installed.

The isolated worker has a two-minute default timeout. It terminates only its own child host on timeout, never Office processes or a process tree. A timed-out Office COM server may remain open; the app reports this and asks the user to inspect Office before retrying. Normal conversion releases documents, collection COM objects, and its new application instance. Conversion refuses pre-existing Office sessions.
