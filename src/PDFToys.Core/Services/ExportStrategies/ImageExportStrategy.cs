using PDFToys.Core.Models;
using SkiaSharp;

namespace PDFToys.Core.Services.ExportStrategies;

public sealed class ImageExportStrategy : ExportStrategyBase
{
    public override bool CanHandle(PdfExportFormat format) => format is PdfExportFormat.Png or PdfExportFormat.Jpg or PdfExportFormat.Jpeg;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416", Justification = "Runtime desktop platform guards precede the PDFtoImage iterator and its deferred enumeration.")]
    public override OperationResult Execute(IReadOnlyList<PdfFile> inputs, ExportOptions options, string outputFolder)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return new(false, "", "Image export is unavailable on this platform.");
        var (format, extension) = options.Format switch
        {
            PdfExportFormat.Png => (SKEncodedImageFormat.Png, "png"),
            PdfExportFormat.Jpg => (SKEncodedImageFormat.Jpeg, "jpg"),
            PdfExportFormat.Jpeg => (SKEncodedImageFormat.Jpeg, "jpeg"),
            _ => throw new InvalidOperationException("Unsupported image format.")
        };
        return ExportInputs(inputs, outputFolder, (input, inputIndex, outputs) =>
        {
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
                throw new PlatformNotSupportedException("Image export is unavailable on this platform.");
            OperationContext.ThrowIfCancellationRequested();
            var bytes = File.ReadAllBytes(input.FilePath);
            var pageIndex = 1;
            foreach (var bitmap in PDFtoImage.Conversion.ToImages(bytes))
            {
                using (bitmap)
                {
                    OperationContext.ThrowIfCancellationRequested();
                    OperationContext.Report(new("Rendering page", input.FilePath, inputIndex, inputs.Count, pageIndex, null));
                    using var transaction = OutputFileTransaction.Create(Path.Combine(outputFolder, PageOutputName(inputs, inputIndex, pageIndex, extension)));
                    using (var stream = File.Create(transaction.CandidatePath))
                    {
                        if (!bitmap.Encode(stream, format, 100)) throw new InvalidDataException("Could not encode the rendered page.");
                    }
                    var path = transaction.Commit(candidate =>
                    {
                        // Decode a memory snapshot so native codecs cannot retain an
                        // OS file handle across the immediate candidate move/cleanup.
                        using var decoded = SKBitmap.Decode(File.ReadAllBytes(candidate));
                        return decoded is not null && decoded.Width > 0 && decoded.Height > 0;
                    });
                    outputs.Add(new(path));
                    pageIndex++;
                }
            }
        });
    }
}
