using System;
using System.IO;
using PDFtoImage;
using PDFToys.App.Models;
using SkiaSharp;

namespace PDFToys.App.Services;

public sealed class PdfPagePreviewService : IPdfPagePreviewService
{
    public PagePreviewResult RenderPage(string filePath, int pageNumber)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return new PagePreviewResult(false, null, "PDF page previews are available on Windows.");
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                return new PagePreviewResult(false, null, "PDF file path is required.");
            }

            if (!File.Exists(filePath))
            {
                return new PagePreviewResult(false, null, $"Input PDF not found: {filePath}");
            }

            if (!Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return new PagePreviewResult(false, null, "Input file must be a PDF.");
            }

            if (pageNumber < 1)
            {
                return new PagePreviewResult(false, null, $"Page number out of range: {pageNumber}");
            }

            var pdfBytes = File.ReadAllBytes(filePath);
            var pageCount = Conversion.GetPageCount(pdfBytes);
            if (pageNumber > pageCount)
            {
                return new PagePreviewResult(false, null, $"Page number out of range: {pageNumber}");
            }

            using var bitmap = Conversion.ToImage(pdfBytes, new Index(pageNumber - 1));
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            if (data is null)
            {
                return new PagePreviewResult(false, null, "Failed to encode page preview.");
            }

            return new PagePreviewResult(true, data.ToArray(), string.Empty);
        }
        catch (Exception ex)
        {
            return new PagePreviewResult(false, null, ex.Message);
        }
    }
}
