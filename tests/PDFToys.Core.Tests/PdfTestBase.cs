using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace PDFToys.Core.Tests;

public abstract class PdfTestBase : IDisposable
{
    protected readonly string TempDirectory;

    protected PdfTestBase()
    {
        TempDirectory = Path.Combine(Path.GetTempPath(), $"pdftoys-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(TempDirectory);
    }

    protected string CreatePdf(string fileName, int pageCount)
    {
        var fullPath = Path.Combine(TempDirectory, fileName);
        using var document = new PdfDocument();

        for (var i = 0; i < pageCount; i++)
        {
            document.AddPage();
        }

        document.Save(fullPath);
        return fullPath;
    }

    protected string CreatePdfWithEmbeddedImage(string fileName, int width, int height)
    {
        var fullPath = Path.Combine(TempDirectory, fileName);
        var imagePath = Path.Combine(TempDirectory, $"{Path.GetFileNameWithoutExtension(fileName)}-source.png");
        CreatePng(imagePath, width, height);

        using var document = new PdfDocument();
        using var image = XImage.FromFile(imagePath);
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(width);
        page.Height = XUnit.FromPoint(height);

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            gfx.DrawImage(image, 0, 0, width, height);
        }

        document.Save(fullPath);
        return fullPath;
    }

    public void Dispose()
    {
        if (!Directory.Exists(TempDirectory))
        {
            return;
        }

        const int maxRetries = 5;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                Directory.Delete(TempDirectory, recursive: true);
                return;
            }
            catch (IOException) when (attempt < maxRetries)
            {
                Thread.Sleep(50 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < maxRetries)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }

    protected static void AssertPageCounts(IReadOnlyList<string> filePaths, IReadOnlyList<int> expectedCounts)
    {
        Assert.Equal(expectedCounts.Count, filePaths.Count);
        for (var i = 0; i < filePaths.Count; i++)
        {
            using var document = PdfReader.Open(filePaths[i], PdfDocumentOpenMode.Import);
            Assert.Equal(expectedCounts[i], document.PageCount);
        }
    }

    private static void CreatePng(string path, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Coral);

        using var paint = new SKPaint { Color = SKColors.DarkBlue, StrokeWidth = 2 };
        for (var x = 0; x < width; x += 10)
        {
            canvas.DrawLine(x, 0, x, height, paint);
        }

        for (var y = 0; y < height; y += 10)
        {
            canvas.DrawLine(0, y, width, y, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data!.SaveTo(stream);
    }
}
