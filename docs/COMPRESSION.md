# PDF compression

The Standard/Normal and Maximum presets use **lossy image recompression**, at JPEG quality 60 and 30 respectively. They do not promise a target file size, lossless processing or image downsampling. Text, vectors and page rotations remain document content; pages are not rasterized.

| Image/content | Behavior |
| --- | --- |
| 8-bit DeviceRGB / DeviceGray JPEG | Decode and recompress if JPEG sample components and dimensions match the PDF dictionary |
| 8-bit DeviceRGB / DeviceGray Flate | Decode bounded sample data, apply TIFF predictor 2 or PNG predictors 10–15 when specified, then recompress |
| Unfiltered 8-bit DeviceRGB / DeviceGray | Decode the declared sample layout, then recompress |
| Soft masks, explicit masks, stencil masks, `/Decode`, `/Matte`, external streams and alternates | Preserve with a specific reason |
| CMYK, ICCBased, indexed, named/other color spaces, embedded JPEG ICC profiles, unusual bit depths | Preserve with a reason |
| Default RGB/grayscale color-space substitutions in a resource scope | Conservatively preserve all image objects in that document; a shared image may occur in multiple scopes |
| JPX, CCITT and other filters or multi-filter chains | Preserve with a reason |
| Inline images | Preserve; content inspection warns about them and excludes them from XObject counts |
| Nested form XObjects, inherited page resources | Traverse; shared images are inspected once and cycles terminate |
| Malformed/oversized sample layouts | Preserve with a reason before allocating unbounded sample buffers |
| Encrypted documents | Refuse compression; unlock a separate copy first |
| Digital signatures or signature fields | Refuse rewriting; use an unsigned copy |

An image stream is replaced only if its JPEG encoding is smaller. The complete PDF is then written to an owned candidate and validated before a unique output name is committed. If the complete candidate is equal in size or larger, the output is an exact unchanged copy of the original. Existing outputs and the input are never automatically overwritten. If no image stream can improve, the unchanged copy is made directly, avoiding unnecessary document serialization.
