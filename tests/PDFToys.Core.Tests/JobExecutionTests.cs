using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PDFToys.Core.Contracts;
using PDFToys.Core.Models;
using PDFToys.Core.Services;
using PDFToys.Core.Services.ExportStrategies;

namespace PDFToys.Core.Tests;

public sealed class JobExecutionTests : PdfTestBase
{
    [Fact]
    public void ConversionJob_SnapshotsMutableSelection()
    {
        var inputs = new List<string> { "first.txt" };
        var job = new ConversionJob(ConversionJobKind.ConvertEachToPdf, inputs);
        inputs[0] = "changed.txt";
        inputs.Add("second.txt");
        Assert.Equal(["first.txt"], job.Inputs);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)job.Inputs)[0] = "mutated.txt");
    }

    [Fact]
    public async Task Runner_ReturnsBeforeWorkCompletes_RejectsDuplicateAndReleasesBusyState()
    {
        var runner = new ApplicationJobRunner();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var job = runner.RunAsync(() => { started.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); return new(true, "", ""); });
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(job.IsCompleted);
        var duplicate = await runner.RunAsync(() => throw new InvalidOperationException("must not run"));
        Assert.Equal("Busy", duplicate.ErrorCode);
        release.Set();
        Assert.True((await job).IsSuccess);
        Assert.True((await runner.RunAsync(() => new(true, "", ""))).IsSuccess);
    }

    [Fact]
    public async Task Runner_PreCancelled_DoesNotStartWork_AndMapsExceptions()
    {
        var runner = new ApplicationJobRunner();
        var cancelled = await runner.RunAsync(() => throw new InvalidOperationException("should not start"), new CancellationToken(true));
        Assert.Equal(OperationOutcome.Cancelled, cancelled.Outcome);
        var failed = await runner.RunAsync(() => throw new IOException("fixture failure"));
        Assert.False(failed.IsSuccess);
        Assert.Contains("fixture failure", failed.ErrorMessage);
    }

    [Fact]
    public void Workflow_CancelledBatch_KeepsCommittedOutputsAndMarksPendingInputs()
    {
        using var cancellation = new CancellationTokenSource();
        var output = CreatePdf("committed.pdf", 1);
        var conversion = new DelegateConverter((_, _) => { cancellation.Cancel(); return new(true, output, ""); });
        using var scope = OperationContext.Begin(cancellation.Token);
        var result = Workflow(conversion).Execute(new(ConversionJobKind.ConvertEachToPdf, ["one.txt", "two.txt"], TempDirectory));
        Assert.Equal(OperationOutcome.Cancelled, result.Outcome);
        Assert.Single(result.Outputs);
        Assert.True(File.Exists(result.Outputs[0].Path));
        Assert.Equal(2, result.Files.Count);
        Assert.Equal(OperationOutcome.Cancelled, result.Files[1].Result.Outcome);
    }

    [Fact]
    public void Workflow_PartialFailure_ReportsEachOriginalAndCommittedOutput()
    {
        var output = CreatePdf("committed.pdf", 1);
        var converter = new DelegateConverter((path, _) => path == "one.txt" ? new(true, output, "") : new(false, "", "unsupported"));
        var result = Workflow(converter).Execute(new(ConversionJobKind.ConvertEachToPdf, ["one.txt", "two.txt"], TempDirectory));
        Assert.Equal(OperationOutcome.PartialSuccess, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Equal(2, result.Files.Count);
        Assert.Single(result.Outputs);
        Assert.Equal("two.txt", result.Files[1].InputPath);
    }

    [Fact]
    public void Workflow_InvalidJob_DoesNotInvokeServices()
    {
        var service = Workflow(new DelegateConverter((_, _) => throw new InvalidOperationException("must not invoke")));
        Assert.False(service.Execute(new(ConversionJobKind.ConvertToPdf, ["one.txt", "two.txt"])).IsSuccess);
        Assert.False(service.Execute(new((ConversionJobKind)999, ["one.txt"])).IsSuccess);
        Assert.False(service.Execute(new(ConversionJobKind.Export, ["one.pdf"], format: (PdfExportFormat)999)).IsSuccess);
        Assert.False(service.Execute(new(ConversionJobKind.ConvertEachToPdf, [" "])).IsSuccess);
    }

    [Fact]
    public void Combine_IdenticalBasenames_KeepsDistinctPagesAndOrder()
    {
        var first = Path.Combine(TempDirectory, "a", "same.txt");
        var second = Path.Combine(TempDirectory, "b", "same.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        File.WriteAllText(first, "111"); File.WriteAllText(second, "222");
        var converter = new DelegateConverter((input, options) =>
        {
            Directory.CreateDirectory(options.OutputDirectory);
            var path = Path.Combine(options.OutputDirectory, "same_Converted.pdf");
            using var pdf = new PdfDocument();
            pdf.AddPage().Width = XUnit.FromPoint(int.Parse(File.ReadAllText(input)));
            pdf.Save(path);
            return new(true, path, "");
        });
        var result = Workflow(converter).Execute(new(ConversionJobKind.CombineToPdf, [first, second], TempDirectory));
        Assert.True(result.IsSuccess, result.ErrorMessage);
        using var combined = PdfReader.Open(result.OutputPath, PdfDocumentOpenMode.Import);
        Assert.Equal(2, combined.PageCount);
        Assert.Equal(111, combined.Pages[0].Width.Point);
        Assert.Equal(222, combined.Pages[1].Width.Point);
        Assert.All(result.Outputs, output => Assert.True(File.Exists(output.Path)));
    }

    [Fact]
    public void Combine_Failure_DoesNotClaimPreparedInputsSucceeded()
    {
        var count = 0;
        var service = Workflow(new DelegateConverter((_, _) => ++count == 1 ? new(true, "intermediate.pdf", "") : new(false, "", "cannot convert")));
        var result = service.Execute(new(ConversionJobKind.CombineToPdf, ["one.txt", "two.txt"], TempDirectory));
        Assert.False(result.IsSuccess);
        Assert.Empty(result.Outputs);
        Assert.All(result.Files, file => Assert.False(file.Result.IsSuccess));
    }

    [Fact]
    public void Split_CancelAfterFirstPart_KeepsOnlyCommittedPartAndOriginal()
    {
        var input = CreatePdf("source.pdf", 3);
        using var cancellation = new CancellationTokenSource();
        var progress = new ImmediateProgress(value => { if (value.CompletedFiles == 1) cancellation.Cancel(); });
        using var scope = OperationContext.Begin(cancellation.Token, progress);
        var result = new SplitService().Split(new(input), new(TempDirectory, 1));
        Assert.Equal(OperationOutcome.Cancelled, result.Outcome);
        Assert.Single(result.Outputs);
        Assert.True(File.Exists(result.Outputs[0].Path));
        Assert.True(File.Exists(input));
        Assert.Empty(Directory.GetFiles(TempDirectory, ".pdftoys-*"));
    }

    [Fact]
    public void Split_RepeatedRun_PreservesPreviousResults()
    {
        var input = CreatePdf("source.pdf", 2);
        var service = new SplitService();
        var first = service.Split(new(input), new(TempDirectory, 1));
        var originalBytes = first.Outputs.Select(output => File.ReadAllBytes(output.Path)).ToArray();
        var second = service.Split(new(input), new(TempDirectory, 1));
        Assert.True(second.IsSuccess, second.ErrorMessage);
        Assert.Empty(first.Outputs.Select(output => output.Path).Intersect(second.Outputs.Select(output => output.Path)));
        for (var i = 0; i < first.Outputs.Count; i++) Assert.Equal(originalBytes[i], File.ReadAllBytes(first.Outputs[i].Path));
    }

    [Fact]
    public void Split_LargeChunkSize_DoesNotOverflow_AndInvalidPresetFails()
    {
        var input = CreatePdf("large-chunk.pdf", 2);
        var service = new SplitService();
        var result = service.Split(new(input), new(TempDirectory, int.MaxValue));
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Single(result.Outputs);
        using var output = PdfReader.Open(result.Outputs[0].Path, PdfDocumentOpenMode.Import);
        Assert.Equal(2, output.PageCount);
        Assert.False(service.Split(new(input), new(TempDirectory, 1, Preset: (SplitPreset)999)).IsSuccess);
    }

    [Fact]
    public void Export_IdenticalBasenames_ExportsEveryPageToDistinctFiles()
    {
        var first = Path.Combine(TempDirectory, "a"); var second = Path.Combine(TempDirectory, "b");
        Directory.CreateDirectory(first); Directory.CreateDirectory(second);
        var a = CreatePdf(Path.Combine(first, "same.pdf"), 1);
        var b = CreatePdf(Path.Combine(second, "same.pdf"), 2);
        var export = new ExportService([new MarkdownExportStrategy()]);
        var result = export.Export([new(a), new(b)], new(TempDirectory, PdfExportFormat.Markdown));
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(3, result.Outputs.Count);
        Assert.Equal(3, result.Outputs.Select(output => output.Path).Distinct().Count());
        Assert.Equal(3, Directory.GetFiles(result.OutputPath, "*.md").Length);
        var repeated = export.Export([new(a), new(b)], new(TempDirectory, PdfExportFormat.Markdown));
        Assert.NotEqual(result.OutputPath, repeated.OutputPath);
        Assert.All(result.Outputs, output => Assert.True(File.Exists(output.Path)));
    }

    [Fact]
    public void DirectExport_Cancellation_RecordsPendingFilesAndCommittedPages()
    {
        var first = CreatePdf("one.pdf", 2); var second = CreatePdf("two.pdf", 1);
        using var cancellation = new CancellationTokenSource();
        using var scope = OperationContext.Begin(cancellation.Token, new ImmediateProgress(value =>
        {
            if (value.CompletedPages == 2) cancellation.Cancel();
        }));
        var result = new ExportService([new MarkdownExportStrategy()]).Export([new(first), new(second)], new(TempDirectory, PdfExportFormat.Markdown));
        Assert.Equal(OperationOutcome.Cancelled, result.Outcome);
        Assert.Single(result.Outputs);
        Assert.Equal(2, result.Files.Count);
        Assert.Equal(second, result.Files[1].InputPath);
        Assert.Equal(OperationOutcome.Cancelled, result.Files[1].Result.Outcome);
        Assert.All(result.Outputs, output => Assert.True(File.Exists(output.Path)));
    }

    private ConversionWorkflowService Workflow(IConversionService converter) => new(converter, new MergeService(), new ExportService([new MarkdownExportStrategy()]));
    private sealed class DelegateConverter(Func<string, ConversionOptions, OperationResult> convert) : IConversionService
    {
        public OperationResult Convert(string inputFilePath, ConversionOptions options) => convert(inputFilePath, options);
    }
    private sealed class ImmediateProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }
}
