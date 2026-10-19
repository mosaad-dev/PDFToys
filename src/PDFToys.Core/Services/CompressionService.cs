using PDFToys.Core.Contracts;
using PDFToys.Core.Models;
using PDFToys.Core.Services.ImageProcessing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PDFToys.Core.Services;

public sealed class CompressionService : ServiceBase, ICompressionService
{
    private const int NormalJpegQuality = 60;
    private const int MaximumJpegQuality = 30;
    private readonly IImageCompressor _imageCompressor;

    public CompressionService(IImageCompressor imageCompressor) => _imageCompressor = imageCompressor;

    public OperationResult Compress(PdfFile input, CompressionOptions options) => ExecuteSafe(() =>
    {
        var optionsError = ValidateOptionsNotNull(options);
        if (optionsError is not null) return optionsError;
        var validationError = ValidateStandardInputs(input, options.OutputDirectory);
        if (validationError is not null) return validationError;
        if (!Enum.IsDefined(options.Level))
            return new(false, string.Empty, "Select a supported compression level.") { ErrorCode = "invalid-options" };

        OperationContext.Report(new("Inspecting PDF", input.FilePath, 0, 1));
        using var document = PdfReader.Open(input.FilePath, PdfDocumentOpenMode.Modify);
        if (document.SecuritySettings.IsEncrypted)
            return new(false, string.Empty, "Unlock this PDF before compressing it; encryption settings cannot be retained by this operation.")
                { ErrorCode = "encrypted-pdf" };
        var documentSafetyError = PdfDocumentSafety.CheckRewrite(document);
        if (documentSafetyError is not null) return documentSafetyError;

        var originalSize = new FileInfo(input.FilePath).Length;
        var reasons = new List<string>();
        var warnings = new List<string>();
        var processed = 0;
        var changed = 0;
        var supported = 0;
        var walker = new PdfImageResourceWalker(message => AddDistinct(warnings, message));
        var quality = options.Level == CompressionLevel.Maximum ? MaximumJpegQuality : NormalJpegQuality;
        var images = new List<(PdfDictionary Image, int Page)>();
        for (var pageIndex = 0; pageIndex < document.Pages.Count; pageIndex++)
        {
            OperationContext.ThrowIfCancellationRequested();
            OperationContext.Report(new("Inspecting image resources", input.FilePath, 0, 1, pageIndex, document.Pages.Count));
            foreach (var image in walker.ImagesOnPage(document.Pages[pageIndex]))
            {
                OperationContext.ThrowIfCancellationRequested();
                images.Add((image, pageIndex));
            }
        }
        var maskImages = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        foreach (var image in document.Internals.GetAllObjects().OfType<PdfDictionary>().Concat(images.Select(item => item.Image)))
        foreach (var key in new[] { "/SMask", "/Mask" })
            if (PdfImageDecoder.Resolve(image.Elements[key]) is PdfDictionary mask) maskImages.Add(mask);
        foreach (var (image, pageIndex) in images)
        {
            OperationContext.ThrowIfCancellationRequested();
            OperationContext.Report(new("Recompressing supported images", input.FilePath, 0, 1, pageIndex, document.Pages.Count));
            processed++;
            if (maskImages.Contains(image))
            {
                AddDistinct(reasons, "Images used as transparency or explicit masks are preserved.");
                continue;
            }
            // A shared image can be rendered in multiple resource scopes. Inspect the
            // complete graph before mutation so a later default profile is not missed.
            if (walker.HasDefaultColorSpaces)
            {
                AddDistinct(reasons, "Images in a PDF with default RGB/grayscale colour-space substitutions are preserved.");
                continue;
            }
            if (!PdfImageDecoder.TryDecode(image, out var encoded, out var reason))
            {
                AddDistinct(reasons, reason);
                continue;
            }
            supported++;
            var result = _imageCompressor.Compress(encoded, quality);
            if (result is null || result.Width != image.Elements.GetInteger("/Width") || result.Height != image.Elements.GetInteger("/Height"))
            {
                AddDistinct(reasons, "An image could not be encoded safely; its original stream is preserved.");
                continue;
            }
            if (result.Bytes.Length >= image.Stream!.Value.Length)
            {
                AddDistinct(reasons, "An image was already smaller than the selected JPEG encoding and was preserved.");
                continue;
            }
            ApplyCompressedImage(image, result);
            changed++;
        }

        using var output = OutputFileTransaction.CreatePdf(input.FilePath, options.OutputDirectory, "Compressed");
        long candidateSize;
        if (changed == 0)
        {
            AddDistinct(warnings, supported == 0
                ? "No supported image objects could be recompressed. An unchanged copy was saved."
                : "The supported images are already smaller than the selected encoding. An unchanged copy was saved.");
            File.Copy(input.FilePath, output.CandidatePath, overwrite: false);
            candidateSize = originalSize;
        }
        else
        {
            OperationContext.ThrowIfCancellationRequested();
            OperationContext.Report(new("Writing and validating compressed PDF", input.FilePath, 0, 1, document.Pages.Count, document.Pages.Count));
            document.Options.CompressContentStreams = true;
            document.Options.NoCompression = false;
            document.Options.FlateEncodeMode = options.Level == CompressionLevel.Maximum
                ? PdfFlateEncodeMode.BestCompression : PdfFlateEncodeMode.Default;
            document.Save(output.CandidatePath);
            candidateSize = new FileInfo(output.CandidatePath).Length;
            if (candidateSize >= originalSize)
            {
                AddDistinct(warnings, "The recompressed PDF was not smaller. An unchanged copy of the original was saved.");
                AddDistinct(reasons, "Image changes were discarded because the complete PDF did not become smaller.");
                File.Copy(input.FilePath, output.CandidatePath, overwrite: true);
                changed = 0;
            }
        }
        OperationContext.ThrowIfCancellationRequested();
        var outputPath = output.CommitPdf();
        var finalSize = new FileInfo(outputPath).Length;
        OperationContext.Report(new("Completed", input.FilePath, 1, 1, document.Pages.Count, document.Pages.Count));
        foreach (var reason in reasons) AddDistinct(warnings, reason);
        return new OperationResult(true, outputPath, string.Empty)
        {
            Outcome = changed == 0 ? OperationOutcome.Unchanged : OperationOutcome.Succeeded,
            Warnings = warnings.ToArray(),
            Compression = new(originalSize, candidateSize, finalSize, processed, changed, processed - changed, reasons.ToArray())
        };
    });

    private static void AddDistinct(List<string> messages, string message)
    {
        if (!messages.Contains(message, StringComparer.Ordinal)) messages.Add(message);
    }

    private static void ApplyCompressedImage(PdfDictionary image, CompressedImageResult result)
    {
        image.Stream!.Value = result.Bytes;
        image.Elements.SetName("/Filter", "/DCTDecode");
        image.Elements.SetInteger("/Length", result.Bytes.Length);
        image.Elements.SetInteger("/BitsPerComponent", 8);
        image.Elements.SetName("/ColorSpace", "/DeviceRGB");
        image.Elements.Remove("/DecodeParms");
        // Masks, decode arrays and colour-sensitive images are rejected before mutation.
        // Keep all unrelated image metadata and page/document structures intact.
    }
}
