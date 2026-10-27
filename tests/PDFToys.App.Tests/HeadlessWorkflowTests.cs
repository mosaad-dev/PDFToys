using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PDFToys.App.Models;
using PDFToys.App.Services;
using PDFToys.Core.Contracts;
using PDFToys.Core.Models;
using PDFToys.Core.Services;
using PDFToys.Core.Services.ExportStrategies;

namespace PDFToys.App.Tests;

public sealed class HeadlessWorkflowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pdftoys-cli-tests-" + Guid.NewGuid().ToString("N"));

    public HeadlessWorkflowTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void GuiAndCli_CombineSameNames_PreserveOrderAndContent()
    {
        var a = WriteInput("a", "same.txt", "111");
        var b = WriteInput("b", "same.txt", "222");
        var conversion = new ConversionService([new FixtureConversionStrategy()]);
        var merge = new MergeService(); var export = new ExportService([new MarkdownExportStrategy()]);
        var gui = new ConversionWorkflowService(conversion, merge, export).Execute(new(ConversionJobKind.CombineToPdf, [a, b]));
        var cli = new HeadlessConversionExecutor(conversion, merge, export).Execute(new(StartupRouteKind.HeadlessConversion,
            PdfToysOperation.CombineToPdf, [a, b], null, null, false));
        Assert.True(gui.IsSuccess, gui.ErrorMessage);
        Assert.True(cli.IsSuccess, string.Join("; ", cli.Errors));
        var cliOutput = Assert.Single(cli.Outputs);
        Assert.NotEqual(gui.OutputPath, cliOutput);
        Assert.Equal([111d, 222d], Widths(gui.OutputPath));
        Assert.Equal(Widths(gui.OutputPath), Widths(cliOutput));
        Assert.True(File.Exists(cli.LogPath));
    }

    [Fact]
    public void GuiAndCli_PartialBatch_RetainSuccessAndNameFailedInput()
    {
        var a = WriteInput("a", "same.txt", "111");
        var b = WriteInput("b", "failure.unknown", "cannot convert");
        var conversion = new ConversionService([new FixtureConversionStrategy()]);
        var merge = new MergeService(); var export = new ExportService([new MarkdownExportStrategy()]);
        var gui = new ConversionWorkflowService(conversion, merge, export).Execute(new(ConversionJobKind.ConvertEachToPdf, [a, b]));
        var cli = new HeadlessConversionExecutor(conversion, merge, export).Execute(new(StartupRouteKind.HeadlessConversion,
            PdfToysOperation.ConvertEachToPdf, [a, b], null, null, false));
        Assert.Equal(OperationOutcome.PartialSuccess, gui.Outcome);
        Assert.Equal(1, cli.ExitCode);
        Assert.False(cli.IsSuccess);
        Assert.Single(gui.Outputs);
        Assert.True(File.Exists(Assert.Single(cli.Outputs)));
        Assert.Contains("failure.unknown", Assert.Single(cli.Errors));
    }

    private string WriteInput(string subdirectory, string name, string content)
    {
        var directory = Path.Combine(_directory, subdirectory); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name); File.WriteAllText(path, content); return path;
    }
    private static double[] Widths(string path)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return Enumerable.Range(0, document.PageCount).Select(index => document.Pages[index].Width.Point).ToArray();
    }
    private sealed class FixtureConversionStrategy : IConversionStrategy
    {
        public bool CanHandle(string extension) => extension == ".txt";
        public OperationResult Execute(string inputFilePath, ConversionOptions options, string outputFilePath)
        {
            using var document = new PdfDocument();
            document.AddPage().Width = XUnit.FromPoint(int.Parse(File.ReadAllText(inputFilePath)));
            document.Save(outputFilePath);
            return new(true, outputFilePath, "");
        }
    }
    public void Dispose() { Directory.Delete(_directory, true); }
}
