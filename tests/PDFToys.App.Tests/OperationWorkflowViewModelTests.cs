using CommunityToolkit.Mvvm.Input;
using PDFToys.App.Models;
using PDFToys.App.Services;
using PDFToys.App.ViewModels;
using PDFToys.Core.Contracts;
using PDFToys.Core.Models;
using PDFToys.Core.Services;
using SkiaSharp;

namespace PDFToys.App.Tests;

public sealed class OperationWorkflowViewModelTests
{
    [Fact]
    public async Task Cancellation_StopsBeforeNextFileAndKeepsCommittedOutput()
    {
        using var started = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        var calls = new List<string>();
        var vm = new Harness();
        var execution = vm.Run(["a.pdf", "b.pdf"], input =>
        {
            calls.Add(input); started.Set(); finish.Wait(TimeSpan.FromSeconds(5));
            return new(true, "saved.pdf", "");
        });
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            Assert.True(vm.IsBusy);
            Assert.False(vm.GoBackCommand.CanExecute(null));
            vm.CancelCommand.Execute(null);
            Assert.True(vm.IsCancelling);
            Assert.Contains("current step", vm.StatusMessage);
        }
        finally { finish.Set(); }
        await execution;
        Assert.Equal(["a.pdf"], calls);
        Assert.Contains(vm.Results, r => r.Path == "saved.pdf");
        Assert.Contains(vm.Results, r => r.InputPath == "b.pdf" && r.Result.Outcome == OperationOutcome.Cancelled);
        Assert.Contains("Cancelled", vm.StatusMessage);
        Assert.True(vm.GoBackCommand.CanExecute(null));
    }

    [Fact]
    public async Task DuplicateRun_DoesNotStartAnotherJobOrDiscardResults()
    {
        using var started = new ManualResetEventSlim(); using var finish = new ManualResetEventSlim();
        var vm = new Harness(); var calls = 0;
        var execution = vm.Run(["a.pdf"], _ => { Interlocked.Increment(ref calls); started.Set(); finish.Wait(TimeSpan.FromSeconds(5)); return new(true, "out.pdf", ""); });
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        try { await vm.Run(["b.pdf"], _ => { Interlocked.Increment(ref calls); return new(true, "other.pdf", ""); }); }
        finally { finish.Set(); }
        await execution;
        Assert.Equal(1, calls); Assert.Equal("out.pdf", Assert.Single(vm.Results).Path);
    }

    [Fact]
    public async Task AggregateResults_IncludeEveryOutputAndFailedInput()
    {
        var vm = new Harness();
        var good = new OperationResult(true, "page1.png", "") { Outputs = [new("page1.png"), new("page2.png")] };
        var bad = new OperationResult(false, "", "Incorrect password.");
        await vm.Run(["batch"], _ => new(false, "page1.png", "Partial failure")
        { Outcome = OperationOutcome.PartialSuccess, Outputs = good.Outputs, Files = [new("good.pdf", good), new("bad.pdf", bad)] });
        Assert.Equal(3, vm.Results.Count);
        Assert.Equal(2, vm.Results.Count(r => r.HasOutput));
        Assert.Equal("bad.pdf", Assert.Single(vm.Results, r => !r.Result.IsSuccess).InputPath);
        Assert.True(vm.CanRetry);
    }

    [Fact]
    public async Task CombineResults_ExposeFinalOutputWhenChildrenAreOnlyTemporary()
    {
        var vm = new Harness();
        await vm.Run(["first.txt"], _ => new(true, "combined.pdf", "")
        { Files = [new("first.txt", new(true, "", "")), new("second.txt", new(true, "", ""))] });
        Assert.Equal("combined.pdf", Assert.Single(vm.Results).Path);
    }

    [Fact]
    public async Task CompressionUnchanged_ShowsActualSizeAndSkipReason()
    {
        var vm = new Harness();
        await vm.Run(["scan.pdf"], _ => new(true, "unchanged.pdf", "")
        {
            Outcome = OperationOutcome.Unchanged,
            Compression = new(2048, 3000, 2048, 1, 0, 1, ["Transparency mask preserved."])
        });
        var result = Assert.Single(vm.Results);
        Assert.Equal("Unchanged", result.OutcomeLabel);
        Assert.Contains("0% saved", result.Details);
        Assert.Contains("Transparency mask preserved.", result.Details);
        Assert.Contains("Unchanged", vm.StatusMessage);
        Assert.DoesNotContain("reduced", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelledAggregate_CountsCompletedOriginalInputs()
    {
        var vm = new Harness();
        await vm.Run(["batch"], _ => new(false, "saved.png", "Cancelled")
        {
            Outcome = OperationOutcome.Cancelled,
            Outputs = [new("saved.png")],
            Files = [new("first.pdf", new(true, "saved.png", "")), new("second.pdf", OperationResult.Cancelled())]
        });
        Assert.Contains("1/2 files", vm.StatusMessage);
        Assert.Contains(vm.Results, r => r.Path == "saved.png");
        Assert.Contains(vm.Results, r => r.InputPath == "second.pdf" && r.Result.Outcome == OperationOutcome.Cancelled);
    }

    [Fact]
    public async Task FailedResultActions_SurfaceErrorInRow()
    {
        var actions = new ResultActionsStub { Throw = true };
        var row = new OperationResultItemViewModel("a.pdf", new(true, "saved.pdf", ""), "saved.pdf", actions);
        row.OpenCommand.Execute(null); Assert.Contains("Unavailable", row.Details);
        row.ShowInFolderCommand.Execute(null); Assert.Contains("Unavailable", row.Details);
        await row.CopyPathCommand.ExecuteAsync(null); Assert.Contains("Unavailable", row.Details);
    }

    [Fact]
    public async Task ResultActions_UseExactCommittedPath()
    {
        var actions = new ResultActionsStub();
        var row = new OperationResultItemViewModel("a.pdf", new(true, "saved_3.pdf", ""), "saved_3.pdf", actions);
        row.OpenCommand.Execute(null); row.ShowInFolderCommand.Execute(null); await row.CopyPathCommand.ExecuteAsync(null);
        Assert.Equal(["saved_3.pdf", "saved_3.pdf", "saved_3.pdf"], actions.Paths);
        var failure = new OperationResultItemViewModel("a.pdf", new(false, "", "bad"), "", actions);
        Assert.False(failure.OpenCommand.CanExecute(null)); Assert.False(failure.CopyPathCommand.CanExecute(null));
    }

    [Fact]
    public async Task SplitPicker_CancelKeepsSelectionAndInvalidPdfDoesNotReplaceIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pdftoys-picker-{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(path, "fixture");
        try
        {
            var dialog = new DialogStub { Selection = [path] }; var pages = new RearrangeStub { PageCount = 12 };
            var vm = new SplitViewModel(new SplitStub(), () => { }, dialog, pages, new ApplicationJobRunner());
            await vm.SelectPdfCommand.ExecuteAsync(null);
            Assert.Equal(path, vm.SelectedFilePath); Assert.Equal("12 pages", vm.PageCountLabel);
            dialog.Selection = []; await vm.SelectPdfCommand.ExecuteAsync(null); Assert.Equal(path, vm.SelectedFilePath);
            pages.PageCount = null; await vm.LoadInputAsync(path); Assert.Equal(path, vm.SelectedFilePath); Assert.Contains("Could not read", vm.StatusMessage);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SplitCustomRanges_RejectsPagesOutsideDocumentAndExposesAllFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pdftoys-split-ui-{Guid.NewGuid():N}.pdf"); await File.WriteAllTextAsync(path, "fixture");
        try
        {
            var split = new SplitStub();
            var vm = new SplitViewModel(split, () => { }, new DialogStub(), new RearrangeStub { PageCount = 3 }, new ApplicationJobRunner());
            await vm.LoadInputAsync(path); vm.CustomRangeText = "1-4"; await vm.ExecuteCustomSplitCommand.ExecuteAsync(null);
            Assert.Equal(0, split.Calls); Assert.Contains("between 1 and 3", vm.StatusMessage);
            vm.CustomRangeText = "1-2,3"; await vm.ExecuteCustomSplitCommand.ExecuteAsync(null);
            Assert.Equal(1, split.Calls); Assert.Equal(2, vm.Results.Count); Assert.All(vm.Results, r => Assert.True(r.HasOutput));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task OrganizePreview_AppliesPendingClockwiseRotationAndSaveUsesChosenName()
    {
        var source = MakeTwoColorPng(); var dialog = new PreviewDialogStub(); var rearrange = new RearrangeStub { PageCount = 1 };
        var vm = new OrganizePagesViewModel(rearrange, new DialogStub(), new PreviewStub(source), dialog, () => { }, runner: new ApplicationJobRunner());
        await vm.LoadInputAsync(Path.Combine(Path.GetTempPath(), "source.pdf"));
        vm.RotateRightCommand.Execute(null); await vm.ViewPageCommand.ExecuteAsync(null);
        using var rotated = SKBitmap.Decode(dialog.Request!.ImagePngBytes);
        Assert.Equal(1, rotated.Width); Assert.Equal(2, rotated.Height);
        Assert.Equal(SKColors.Red, rotated.GetPixel(0, 0)); Assert.Equal(SKColors.Blue, rotated.GetPixel(0, 1));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(90, Assert.Single(rearrange.SavedPages!).RotationDegrees);
        Assert.Equal("source_organized.pdf", rearrange.Options!.OutputFileName);
        Assert.Equal("actual_unique.pdf", vm.OutputFilePath);
    }

    [Fact]
    public async Task PreferencesFailure_KeepsCommittedSuccessVisible()
    {
        var vm = new CompressViewModel(new CompressionStub((_, _) => new(true, "saved.pdf", "")), new DialogStub(), () => { }, ["a.pdf"], new BrokenSettings(), new ApplicationJobRunner());
        await vm.ExecuteCompressCommand.ExecuteAsync(null);
        Assert.Equal("saved.pdf", Assert.Single(vm.Results).Path);
        Assert.Contains("Preferences could not be saved", vm.StatusMessage);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task CompressionJob_SnapshotsInputsAndQualityBeforeBackgroundDispatch()
    {
        using var started = new ManualResetEventSlim(); using var finish = new ManualResetEventSlim();
        var calls = new List<(string, CompressionLevel)>();
        var vm = new CompressViewModel(new CompressionStub((input, options) =>
        { calls.Add((input.FilePath, options.Level)); started.Set(); finish.Wait(TimeSpan.FromSeconds(5)); return new(true, input.FilePath + "_out", ""); }),
            new DialogStub(), () => { }, ["a.pdf", "b.pdf"], runner: new ApplicationJobRunner());
        vm.SelectedCompressionLevel = "Maximum"; var execution = vm.ExecuteCompressCommand.ExecuteAsync(null);
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            vm.SelectedFiles.Clear(); vm.SelectedCompressionLevel = "Standard";
            Assert.False(vm.AddFilesCommand.CanExecute(null)); Assert.False(vm.RemoveFileCommand.CanExecute(null));
        }
        finally { finish.Set(); }
        await execution;
        Assert.Equal([("a.pdf", CompressionLevel.Maximum), ("b.pdf", CompressionLevel.Maximum)], calls);
    }

    [Fact]
    public async Task RetryFailed_CompressionProcessesOnlyFailuresAndRetainsSuccessfulResults()
    {
        var calls = new List<string>(); var fail = true;
        var vm = new CompressViewModel(new CompressionStub((input, _) =>
        { calls.Add(input.FilePath); return fail && input.FilePath == "bad.pdf" ? new(false, "", "bad") : new(true, input.FilePath + "_out", ""); }),
            new DialogStub(), () => { }, ["good.pdf", "bad.pdf"], runner: new ApplicationJobRunner());
        await vm.ExecuteCompressCommand.ExecuteAsync(null); fail = false;
        await vm.RetryFailedCommand.ExecuteAsync(null);
        Assert.Equal(["good.pdf", "bad.pdf", "bad.pdf"], calls); Assert.Equal(2, vm.Results.Count); Assert.All(vm.Results, r => Assert.True(r.Result.IsSuccess));
    }

    [Fact]
    public async Task RetryFailed_ConversionUsesOnlyFailedOriginalInputs()
    {
        var conversion = new ConversionStub();
        var vm = new ConvertExportViewModel(conversion, new MergeStub(), new ExportStub(), new DialogStub(), () => { }, ["good.txt", "bad.txt"], new ApplicationJobRunner());
        vm.SelectedOperationItem = vm.OperationItems.Single(i => i.Operation == ConvertExportOperation.ConvertEachToPdf);
        await vm.ExecuteCommand.ExecuteAsync(null); conversion.Fail = false;
        await vm.RetryFailedCommand.ExecuteAsync(null);
        Assert.Equal(["good.txt", "bad.txt", "bad.txt"], conversion.Calls); Assert.Equal(2, vm.Results.Count); Assert.All(vm.Results, r => Assert.True(r.Result.IsSuccess));
    }

    [Fact]
    public async Task ConversionSummary_CountsEveryOriginalInput()
    {
        var conversion = new ConversionStub { Fail = false };
        var vm = new ConvertExportViewModel(conversion, new MergeStub(), new ExportStub(), new DialogStub(), () => { }, ["first.txt", "second.txt"], new ApplicationJobRunner());
        vm.SelectedOperationItem = vm.OperationItems.Single(i => i.Operation == ConvertExportOperation.ConvertEachToPdf);
        await vm.ExecuteCommand.ExecuteAsync(null);
        Assert.Contains("2/2 files completed", vm.StatusMessage);
    }

    [Fact]
    public void BackgroundCompletion_NotifiesOnTheCapturedUiContext()
    {
        using var context = new PumpContext();
        var uiThread = Environment.CurrentManagedThreadId;
        var workerThread = uiThread;
        var notifications = new List<int>();
        context.Run(async () =>
        {
            var vm = new Harness();
            vm.PropertyChanged += (_, _) => notifications.Add(Environment.CurrentManagedThreadId);
            await vm.Run(["source.pdf"], _ =>
            {
                workerThread = Environment.CurrentManagedThreadId;
                OperationContext.Report(new("Rendering", "source.pdf", 0, 1));
                return new(true, "saved.pdf", "");
            });
            Assert.Equal("saved.pdf", Assert.Single(vm.Results).Path);
        });
        Assert.NotEqual(uiThread, workerThread);
        Assert.NotEmpty(notifications);
        Assert.All(notifications, thread => Assert.Equal(uiThread, thread));
    }

    private static byte[] MakeTwoColorPng()
    {
        using var bitmap = new SKBitmap(2, 1); bitmap.SetPixel(0, 0, SKColors.Red); bitmap.SetPixel(1, 0, SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray();
    }
    private sealed class PumpContext : SynchronizationContext, IDisposable
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));
        public void Run(Func<Task> action)
        {
            var prior = Current;
            SetSynchronizationContext(this);
            try
            {
                var task = action();
                _ = task.ContinueWith(_ => _queue.CompleteAdding(), TaskScheduler.Default);
                foreach (var work in _queue.GetConsumingEnumerable()) work.Callback(work.State);
                task.GetAwaiter().GetResult();
            }
            finally { SetSynchronizationContext(prior); }
        }
        public void Dispose() => _queue.Dispose();
    }
    private sealed class Harness() : OperationViewModelBase(() => { }, new ApplicationJobRunner())
    {
        public Task Run(string[] inputs, Func<string, OperationResult> handler) => RunBatchAsync("Test", inputs, handler, failed => Run(failed.ToArray(), handler));
    }
    private sealed class DialogStub : IFileDialogService
    {
        public IReadOnlyList<string> Selection { get; set; } = [];
        public Task<IReadOnlyList<string>> PickPdfFilesAsync() => Task.FromResult(Selection);
        public Task<IReadOnlyList<string>> PickConvertibleFilesAsync(bool allowMultiple = true) => Task.FromResult(Selection);
        public Task<string?> ShowSaveFileDialogAsync(string title, string defaultFileName, string extension) => Task.FromResult<string?>(null);
    }
    private sealed class SplitStub : ISplitService
    {
        public int Calls { get; private set; }
        public OperationResult Split(PdfFile input, SplitOptions options) { Calls++; return new(true, "part1.pdf", "") { Outputs = [new("part1.pdf"), new("part2.pdf")] }; }
    }
    private sealed class RearrangeStub : IRearrangeService
    {
        public int? PageCount { get; set; }
        public PageArrangementItem[]? SavedPages { get; private set; }
        public RearrangeOptions? Options { get; private set; }
        public int? TryGetPageCount(PdfFile input) => PageCount;
        public OperationResult Rearrange(PdfFile input, IReadOnlyList<PageArrangementItem> pages, RearrangeOptions options)
        { SavedPages = pages.ToArray(); Options = options; return new(true, "actual_unique.pdf", ""); }
    }
    private sealed class PreviewStub(byte[] png) : IPdfPagePreviewService
    { public PagePreviewResult RenderPage(string filePath, int pageNumber) => new(true, png, ""); }
    private sealed class PreviewDialogStub : IPagePreviewDialogService
    { public PagePreviewRequest? Request { get; private set; } public Task ShowAsync(PagePreviewRequest request) { Request = request; return Task.CompletedTask; } }
    private sealed class ResultActionsStub : IResultActionService
    {
        public bool Throw { get; init; } public List<string> Paths { get; } = [];
        public void Open(string path) { if (Throw) throw new IOException("Unavailable"); Paths.Add(path); }
        public void ShowInFolder(string path) => Open(path);
        public Task CopyPathAsync(string path) { Open(path); return Task.CompletedTask; }
    }
    private sealed class BrokenSettings : IUserSettingsStore
    { public UserSettings Load() => new(); public void Save(UserSettings settings) => throw new IOException("Read-only settings file."); }
    private sealed class CompressionStub(Func<PdfFile, CompressionOptions, OperationResult> handler) : ICompressionService
    { public OperationResult Compress(PdfFile input, CompressionOptions options) => handler(input, options); }
    private sealed class ConversionStub : IConversionService
    {
        public bool Fail { get; set; } = true; public List<string> Calls { get; } = [];
        public OperationResult Convert(string input, ConversionOptions options) { Calls.Add(input); return Fail && input == "bad.txt" ? new(false, "", "bad") : new(true, input + ".pdf", ""); }
    }
    private sealed class MergeStub : IMergeService
    { public OperationResult Merge(IReadOnlyList<PdfFile> inputs, MergeOptions options) => new(true, "combined.pdf", ""); }
    private sealed class ExportStub : IExportService
    { public OperationResult Export(IReadOnlyList<PdfFile> inputs, ExportOptions options) => new(true, "export.png", ""); }
}
