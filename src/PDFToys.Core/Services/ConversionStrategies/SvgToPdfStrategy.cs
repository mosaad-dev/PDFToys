using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SkiaSharp;
using Svg.Skia;
using PDFToys.Core.Models;

namespace PDFToys.Core.Services.ConversionStrategies;

public sealed class SvgToPdfStrategy : BaseConversionStrategy
{
    public override bool CanHandle(string inputExtension) =>
        inputExtension.ToLowerInvariant() is ".svg";

    public override OperationResult Execute(string inputFilePath, ConversionOptions options, string outputFilePath)
    {
        using var svg = new SKSvg();
        if (svg.Load(inputFilePath) is null)
        {
            return new OperationResult(false, string.Empty, "The SVG file could not be loaded.");
        }

        var bounds = svg.Picture?.CullRect ?? SKRect.Empty;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return new OperationResult(false, string.Empty, "The SVG file has no drawable content.");
        }

        var width = Math.Max(1, (int)Math.Ceiling(bounds.Width));
        var height = Math.Max(1, (int)Math.Ceiling(bounds.Height));

        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        canvas.DrawPicture(svg.Picture, SKMatrix.CreateTranslation(-bounds.Left, -bounds.Top));

        using var skImage = SKImage.FromBitmap(bitmap);
        using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
        if (data is null)
        {
            return new OperationResult(false, string.Empty, "Failed to render the SVG to an image.");
        }

        var tempPng = Path.Combine(Path.GetTempPath(), $"pdftoys-svg-{Guid.NewGuid():N}.png");
        try
        {
            using (var stream = File.Create(tempPng))
            {
                data.SaveTo(stream);
            }

            using var document = new PdfDocument();

            ApplyStandardMetadata(document);

            using var pdfImage = XImage.FromFile(tempPng);

            var widthPt = pdfImage.PointWidth <= 0 ? A4WidthPoints : pdfImage.PointWidth;
            var heightPt = pdfImage.PointHeight <= 0 ? A4HeightPoints : pdfImage.PointHeight;

            var page = document.AddPage();
            page.Width = XUnit.FromPoint(widthPt);
            page.Height = XUnit.FromPoint(heightPt);

            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawImage(pdfImage, 0, 0, widthPt, heightPt);

            OperationContext.ThrowIfCancellationRequested();
        document.Save(outputFilePath);
        }
        finally
        {
            CleanupTempFile(tempPng);
        }

        return new OperationResult(true, Path.GetFullPath(outputFilePath), string.Empty);
    }
}
