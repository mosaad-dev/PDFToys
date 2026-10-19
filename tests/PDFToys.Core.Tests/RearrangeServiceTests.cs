using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PDFToys.Core.Models;
using PDFToys.Core.Services;

namespace PDFToys.Core.Tests;

public sealed class RearrangeServiceTests : PdfTestBase
{
    [Theory]
    [InlineData(90, 0, 90)]
    [InlineData(180, 90, 270)]
    [InlineData(270, 90, 0)]
    [InlineData(90, -90, 0)]
    public void Rearrange_ComposesSourceRotationAndDelta(int source, int delta, int expected)
    {
        var path = Path.Combine(TempDirectory, "rotated.pdf");
        using (var document = new PdfDocument())
        {
            document.AddPage().Elements.SetInteger("/Rotate", source);
            document.Save(path);
        }
        var result = new RearrangeService().Rearrange(new PdfFile(path),
            [new PageArrangementItem(0, delta), new PageArrangementItem(0)], new RearrangeOptions(TempDirectory));
        Assert.True(result.IsSuccess, result.ErrorMessage);
        using var output = PdfReader.Open(result.OutputPath, PdfDocumentOpenMode.Import);
        Assert.Equal(expected, output.Pages[0].Elements.GetInteger("/Rotate"));
        Assert.Equal(source, output.Pages[1].Elements.GetInteger("/Rotate"));
    }

    [Fact]
    public void Rearrange_PreservesInheritedRotation()
    {
        var path = Path.Combine(TempDirectory, "inherited.pdf");
        using (var document = new PdfDocument())
        {
            var page = document.AddPage();
            page.Elements.Remove("/Rotate");
            document.Pages.Elements.SetInteger("/Rotate", 180);
            document.Save(path);
        }
        var result = new RearrangeService().Rearrange(new PdfFile(path), [new PageArrangementItem(0, 90)], new RearrangeOptions(TempDirectory));
        Assert.True(result.IsSuccess, result.ErrorMessage);
        using var output = PdfReader.Open(result.OutputPath, PdfDocumentOpenMode.Import);
        Assert.Equal(270, output.Pages[0].Elements.GetInteger("/Rotate"));
    }

    private readonly RearrangeService _service;

    public RearrangeServiceTests()
    {
        _service = new RearrangeService();
    }

    [Fact]
    public void Rearrange_WithNullOptions_ReturnsFailure()
    {
        var inputPath = Path.Combine(TempDirectory, "pages.pdf");
        CreatePdf(inputPath, 3);

        var result = _service.Rearrange(
            new PdfFile(inputPath),
            [new PageArrangementItem(0), new PageArrangementItem(1), new PageArrangementItem(2)],
            null!);

        Assert.False(result.IsSuccess);
        Assert.Contains("Options are required", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rearrange_WithNullPages_ReturnsFailure()
    {
        var inputPath = Path.Combine(TempDirectory, "pages.pdf");
        CreatePdf(inputPath, 3);
        var options = new RearrangeOptions(TempDirectory);

        var result = _service.Rearrange(new PdfFile(inputPath), null!, options);

        Assert.False(result.IsSuccess);
        Assert.Contains("At least one page must be included", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rearrange_WithEmptyPages_ReturnsFailure()
    {
        var inputPath = Path.Combine(TempDirectory, "pages.pdf");
        CreatePdf(inputPath, 3);
        var options = new RearrangeOptions(TempDirectory);

        var result = _service.Rearrange(new PdfFile(inputPath), [], options);

        Assert.False(result.IsSuccess);
        Assert.Contains("At least one page must be included", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rearrange_ValidPageOrder_CreatesRearrangedCopy()
    {
        var inputPath = Path.Combine(TempDirectory, "pages.pdf");
        CreatePdf(inputPath, 3);
        var options = new RearrangeOptions(TempDirectory);

        var result = _service.Rearrange(
            new PdfFile(inputPath),
            [new PageArrangementItem(2), new PageArrangementItem(0), new PageArrangementItem(1)],
            options);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(File.Exists(result.OutputPath));
        Assert.EndsWith("_Rearranged.pdf", result.OutputPath);
    }

    [Fact]
    public void Rearrange_PageIndexOutOfRange_ReturnsFailure()
    {
        var inputPath = Path.Combine(TempDirectory, "pages.pdf");
        CreatePdf(inputPath, 2);
        var options = new RearrangeOptions(TempDirectory);

        var result = _service.Rearrange(
            new PdfFile(inputPath),
            [new PageArrangementItem(0), new PageArrangementItem(2)],
            options);

        Assert.False(result.IsSuccess);
        Assert.Contains("out of range", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rearrange_MissingInput_ReturnsFailure()
    {
        var missingPath = Path.Combine(TempDirectory, "ghost.pdf");
        var options = new RearrangeOptions(TempDirectory);

        var result = _service.Rearrange(new PdfFile(missingPath), [new PageArrangementItem(0)], options);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Rearrange_WithRotation_AppliesRotation()
    {
        var inputPath = Path.Combine(TempDirectory, "rotate.pdf");
        CreatePdf(inputPath, 2);
        var options = new RearrangeOptions(TempDirectory);

        var result = _service.Rearrange(
            new PdfFile(inputPath),
            [new PageArrangementItem(0, 90), new PageArrangementItem(1, 0)],
            options);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(File.Exists(result.OutputPath));

        using var outputDocument = PdfReader.Open(result.OutputPath, PdfDocumentOpenMode.Import);
        Assert.Equal(2, outputDocument.PageCount);
    }

    [Fact]
    public void Rearrange_DuplicatePageIndices_Allowed()
    {
        var inputPath = Path.Combine(TempDirectory, "duplicate.pdf");
        CreatePdf(inputPath, 2);
        var options = new RearrangeOptions(TempDirectory);

        var result = _service.Rearrange(
            new PdfFile(inputPath),
            [new PageArrangementItem(0), new PageArrangementItem(0)],
            options);

        Assert.True(result.IsSuccess, result.ErrorMessage);

        using var outputDocument = PdfReader.Open(result.OutputPath, PdfDocumentOpenMode.Import);
        Assert.Equal(2, outputDocument.PageCount);
    }

    [Fact]
    public void TryGetPageCount_ValidPdf_ReturnsPageCount()
    {
        var inputPath = Path.Combine(TempDirectory, "count.pdf");
        CreatePdf(inputPath, 3);

        var pageCount = _service.TryGetPageCount(new PdfFile(inputPath));

        Assert.Equal(3, pageCount);
    }

    [Fact]
    public void TryGetPageCount_MissingFile_ReturnsNull()
    {
        var missingPath = Path.Combine(TempDirectory, "missing.pdf");

        Assert.Null(_service.TryGetPageCount(new PdfFile(missingPath)));
    }

    [Fact]
    public void TryGetPageCount_NonPdfExtension_ReturnsNull()
    {
        var textPath = Path.Combine(TempDirectory, "notes.txt");
        File.WriteAllText(textPath, "not a pdf");

        Assert.Null(_service.TryGetPageCount(new PdfFile(textPath)));
    }

    [Fact]
    public void TryGetPageCount_NullOrEmptyPath_ReturnsNull()
    {
        Assert.Null(_service.TryGetPageCount(new PdfFile(string.Empty)));
    }
}
