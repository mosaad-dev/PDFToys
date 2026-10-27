using PDFToys.Core.Contracts;
using PDFToys.Core.Models;
using PDFToys.Core.OS.Windows;
using PDFToys.Core.Services;
using PDFToys.Core.Services.ConversionStrategies;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace PDFToys.Core.Tests;

public sealed class ConversionServiceTests : PdfTestBase
{
    private readonly IConversionService _service;

    public ConversionServiceTests()
    {
        if (OperatingSystem.IsWindows())
        {
            PdfSharp.Fonts.GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }

        var strategies = new IConversionStrategy[]
        {
            new ImageToPdfStrategy(),
            new TextToPdfStrategy(),
            new SvgToPdfStrategy(),
            new OfficeComPdfConverter()
        };

        _service = new ConversionService(strategies);
    }

    [Fact]
    public void Convert_UnsupportedExtension_ReturnsFailure()
    {
        var inputPath = Path.Combine(TempDirectory, "file.xyz");
        File.WriteAllText(inputPath, "content");

        var result = _service.Convert(inputPath, new ConversionOptions(TempDirectory));

        Assert.False(result.IsSuccess);
        Assert.Contains("Unsupported file type", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("sample.txt")]
    [InlineData("sample.md")]
    public void Convert_TextFile_CreatesPdf(string fileName)
    {
        var inputPath = Path.Combine(TempDirectory, fileName);
        File.WriteAllText(inputPath, "# Title\n\nHello from PDFToys.");

        var result = _service.Convert(inputPath, new ConversionOptions(TempDirectory));

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(result.OutputPath));
        AssertValidPdf(result.OutputPath);
    }

    [Fact]
    public void Convert_Png_CreatesPdf()
    {
        var inputPath = Path.Combine(TempDirectory, "sample.png");
        CreatePng(inputPath, 120, 80);

        var result = _service.Convert(inputPath, new ConversionOptions(TempDirectory));

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(result.OutputPath));
        AssertValidPdf(result.OutputPath);
    }

    [Fact]
    public void Convert_Jpg_CreatesPdf()
    {
        var inputPath = Path.Combine(TempDirectory, "sample.jpg");
        CreateJpeg(inputPath, 100, 60);

        var result = _service.Convert(inputPath, new ConversionOptions(TempDirectory));

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(result.OutputPath));
        AssertValidPdf(result.OutputPath);
    }

    [Fact]
    public void Convert_Svg_CreatesPdf()
    {
        var inputPath = Path.Combine(TempDirectory, "sample.svg");
        File.WriteAllText(
            inputPath,
            """
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="80">
              <rect x="10" y="10" width="80" height="60" fill="blue"/>
            </svg>
            """);

        var result = _service.Convert(inputPath, new ConversionOptions(TempDirectory));

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(result.OutputPath));
        AssertValidPdf(result.OutputPath);
    }

    private static void AssertValidPdf(string outputPath)
    {
        using var document = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
        Assert.True(document.PageCount > 0);
    }

    private static void CreatePng(string path, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Coral);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data!.SaveTo(stream);
    }

    private static void CreateJpeg(string path, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.LightGreen);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        using var stream = File.Create(path);
        data!.SaveTo(stream);
    }

}

