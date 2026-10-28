using PDFToys.Core.Contracts;
using PDFToys.Core.Models;
using PDFToys.Core.Services;
using PDFToys.Core.Services.ExportStrategies;

namespace PDFToys.Core.Tests;

public sealed class ExportServiceTests : PdfTestBase
{
    [Theory]
    [InlineData(PdfExportFormat.Jpeg)]
    [InlineData(PdfExportFormat.Png)]
    public void Export_ImageValidation_ReleasesAllFileHandlesBeforeReturning(PdfExportFormat format)
    {
        var input = CreatePdf("handle-cleanup.pdf", 2);
        var result = new ExportService([new ImageExportStrategy()]).Export([new(input)], new(TempDirectory, format));
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2, result.Outputs.Count);
        foreach (var output in result.Outputs)
        {
            using (var exclusive = new FileStream(output.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.True(exclusive.Length > 0);
            var renamed = output.Path + ".renamed";
            File.Move(output.Path, renamed);
            File.Delete(renamed);
        }
    }

    private readonly ExportService _service;

    public ExportServiceTests()
    {
        _service = new ExportService(
        [
            new ImageExportStrategy(),
            new MarkdownExportStrategy()
        ]);
    }

    [Fact]
    public void Export_WithNullOptions_ReturnsFailure()
    {
        var inputPath = Path.Combine(TempDirectory, "input.pdf");
        CreatePdf(inputPath, 1);

        var result = _service.Export([new PdfFile(inputPath)], null!);

        Assert.False(result.IsSuccess);
        Assert.Contains("Options are required", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Export_EmptyInputList_ReturnsFailure()
    {
        var options = new ExportOptions(TempDirectory, PdfExportFormat.Png);

        var result = _service.Export([], options);

        Assert.False(result.IsSuccess);
        Assert.Contains("At least one input PDF is required", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Export_MissingOutputDirectory_ReturnsFailure(string? invalidDirectory)
    {
        var inputPath = Path.Combine(TempDirectory, "input.pdf");
        CreatePdf(inputPath, 1);
        var options = new ExportOptions(invalidDirectory!, PdfExportFormat.Png);

        var result = _service.Export([new PdfFile(inputPath)], options);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Export_MissingInput_ReturnsFailure()
    {
        var missingPath = Path.Combine(TempDirectory, "ghost.pdf");
        var options = new ExportOptions(TempDirectory, PdfExportFormat.Png);

        var result = _service.Export([new PdfFile(missingPath)], options);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Export_UnsupportedFormat_ReturnsFailure()
    {
        var inputPath = Path.Combine(TempDirectory, "input.pdf");
        CreatePdf(inputPath, 1);
        var options = new ExportOptions(TempDirectory, (PdfExportFormat)999);

        var result = _service.Export([new PdfFile(inputPath)], options);

        Assert.False(result.IsSuccess);
        Assert.Contains("Unsupported export format", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Export_Png_CreatesImageFiles()
    {
        var inputPath = Path.Combine(TempDirectory, "export.pdf");
        CreatePdf(inputPath, 2);
        var options = new ExportOptions(TempDirectory, PdfExportFormat.Png);

        var result = _service.Export([new PdfFile(inputPath)], options);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(Directory.Exists(result.OutputPath));
        var images = Directory.GetFiles(result.OutputPath, "page_*.png");
        Assert.Equal(2, images.Length);
    }

    [Fact]
    public void Export_Jpeg_CreatesImageFiles()
    {
        var inputPath = Path.Combine(TempDirectory, "export-jpeg.pdf");
        CreatePdf(inputPath, 2);
        var options = new ExportOptions(TempDirectory, PdfExportFormat.Jpeg);

        var result = _service.Export([new PdfFile(inputPath)], options);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(Directory.Exists(result.OutputPath));
        var images = Directory.GetFiles(result.OutputPath, "page_*.jpeg");
        Assert.Equal(2, images.Length);
    }

    [Fact]
    public void Export_Markdown_CreatesMarkdownFiles()
    {
        var inputPath = Path.Combine(TempDirectory, "export-md.pdf");
        CreatePdf(inputPath, 2);
        var options = new ExportOptions(TempDirectory, PdfExportFormat.Markdown);

        var result = _service.Export([new PdfFile(inputPath)], options);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(Directory.Exists(result.OutputPath));
        var markdownFiles = Directory.GetFiles(result.OutputPath, "page_*.md");
        Assert.Equal(2, markdownFiles.Length);
    }
}
