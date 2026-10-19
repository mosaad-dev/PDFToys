using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PDFToys.Core.Models;

namespace PDFToys.Core.Services.ConversionStrategies;

public sealed class TextToPdfStrategy : BaseConversionStrategy
{
    private const double MarginPoints = 50;
    private const double FontSizePoints = 11;
    private const string FontFamily = "Arial";

    public override bool CanHandle(string inputExtension) =>
        inputExtension.ToLowerInvariant() is ".txt" or ".md";

    public override OperationResult Execute(string inputFilePath, ConversionOptions options, string outputFilePath)
    {
        var text = File.ReadAllText(inputFilePath, Encoding.UTF8);

        using var document = new PdfDocument();

        ApplyStandardMetadata(document);

        var font = new XFont(FontFamily, FontSizePoints);
        var brush = XBrushes.Black;

        var contentWidth = A4WidthPoints - (MarginPoints * 2);
        var lineHeight = font.GetHeight() * 1.2;
        var lines = WrapText(text, font, contentWidth);

        var page = document.AddPage();
        page.Width = XUnit.FromPoint(A4WidthPoints);
        page.Height = XUnit.FromPoint(A4HeightPoints);
        var gfx = XGraphics.FromPdfPage(page);
        var y = MarginPoints;

        try
        {
            foreach (var line in lines)
            {
                OperationContext.ThrowIfCancellationRequested();
                if (y + lineHeight > A4HeightPoints - MarginPoints)
                {
                    gfx.Dispose();
                    page = document.AddPage();
                    page.Width = XUnit.FromPoint(A4WidthPoints);
                    page.Height = XUnit.FromPoint(A4HeightPoints);
                    gfx = XGraphics.FromPdfPage(page);
                    y = MarginPoints;
                }

                gfx.DrawString(line, font, brush, new XRect(MarginPoints, y, contentWidth, lineHeight), XStringFormats.TopLeft);
                y += lineHeight;
            }
        }
        finally
        {
            gfx.Dispose();
        }

        OperationContext.ThrowIfCancellationRequested();
        document.Save(outputFilePath);
        return new OperationResult(true, Path.GetFullPath(outputFilePath), string.Empty);
    }

    /// <summary>
    /// Ensures the text dimensions fit within pdf boundaries
    /// </summary>
    private static List<string> WrapText(string text, XFont font, double maxWidth)
    {
        var result = new List<string>();
        var paragraphs = text.Replace("\r\n", "\n").Split('\n');

        using var measureGfx = XGraphics.CreateMeasureContext(
            new XSize(A4WidthPoints, A4HeightPoints), 
            XGraphicsUnit.Point,
            XPageDirection.Downwards);

        foreach (var paragraph in paragraphs)
        {
            if (string.IsNullOrEmpty(paragraph))
            {
                result.Add(string.Empty);
                continue;
            }

            var words = paragraph.Split(' ');
            var currentLine = new StringBuilder();

            foreach (var word in words)
            {
                var candidate = currentLine.Length == 0 ? word : $"{currentLine} {word}";
                var size = measureGfx.MeasureString(candidate, font);

                if (size.Width > maxWidth && currentLine.Length > 0)
                {
                    result.Add(currentLine.ToString());
                    currentLine.Clear();
                    currentLine.Append(word);
                }
                else
                {
                    if (currentLine.Length > 0) currentLine.Append(' ');
                    currentLine.Append(word);
                }
            }

            if (currentLine.Length > 0)
            {
                result.Add(currentLine.ToString());
            }
        }

        return result;
    }
}
