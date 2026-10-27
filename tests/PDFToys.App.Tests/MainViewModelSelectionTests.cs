using Microsoft.Extensions.DependencyInjection;
using PDFToys.App.Models;
using PDFToys.App.Services;
using PDFToys.App.ViewModels;
using PDFToys.Core.Contracts;
using PDFToys.Core.Models;

namespace PDFToys.App.Tests;

public sealed class MainViewModelSelectionTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"pdftoys-home-selection-{Guid.NewGuid():N}");
    public MainViewModelSelectionTests()
    {
        Directory.CreateDirectory(_workspace);
        File.WriteAllText(Path.Combine(_workspace, "private-document.pdf"), "sentinel");
    }

    [Theory]
    [InlineData("merge")]
    [InlineData("split")]
    [InlineData("compress")]
    [InlineData("protect")]
    [InlineData("organize")]
    [InlineData("unlock")]
    [InlineData("convert")]
    public void HomeToolSelection_DoesNotSelectOrReadRememberedWorkspaceFiles(string tool)
    {
        var services = new ServiceStubs();
        using var provider = BuildProvider(services);
        var main = new MainViewModel(provider, new(PdfToysOperation.Home, []));
        var home = Assert.IsType<HomeViewModel>(main.CurrentPage);
        Assert.Equal(_workspace, home.CurrentWorkspace);
        Assert.Empty(home.AvailableFiles);
        var command = tool switch
        {
            "merge" => home.GoToMergeCommand,
            "split" => home.GoToSplitCommand,
            "compress" => home.GoToCompressCommand,
            "protect" => home.GoToProtectCommand,
            "organize" => home.GoToOrganizePagesCommand,
            "unlock" => home.GoToUnlockCommand,
            _ => home.GoToConvertExportCommand
        };
        command.Execute(null);
        switch (main.CurrentPage)
        {
            case MergeViewModel vm: Assert.Empty(vm.MergeItems); break;
            case SplitViewModel vm: Assert.Empty(vm.SelectedFilePath); break;
            case CompressViewModel vm: Assert.Empty(vm.SelectedFiles); break;
            case ProtectViewModel vm: Assert.Empty(vm.SelectedFiles); break;
            case OrganizePagesViewModel vm: Assert.Empty(vm.SelectedFilePath); Assert.Empty(vm.Pages); break;
            case UnlockViewModel vm: Assert.Empty(vm.SelectedFiles); break;
            case ConvertExportViewModel vm: Assert.Empty(vm.SelectedFiles); break;
            default: Assert.Fail("Expected a document tool."); break;
        }
        Assert.Equal(0, services.ReadCalls);
    }

    [Theory]
    [InlineData(PdfToysOperation.Split)]
    [InlineData(PdfToysOperation.Compress)]
    [InlineData(PdfToysOperation.OrganizePages)]
    public async Task ExplicitStartupInputs_RemainSelected(PdfToysOperation operation)
    {
        var input = Path.Combine(_workspace, "explicit.pdf");
        await File.WriteAllTextAsync(input, "explicit fixture");
        var services = new ServiceStubs();
        using var provider = BuildProvider(services);
        var main = new MainViewModel(provider, new(operation, [input]));
        switch (main.CurrentPage)
        {
            case SplitViewModel vm:
                Assert.Equal(input, vm.SelectedFilePath);
                await vm.LoadInputAsync(vm.SelectedFilePath);
                Assert.Equal("1 page", vm.PageCountLabel);
                Assert.Equal(1, services.ReadCalls);
                break;
            case CompressViewModel vm:
                Assert.Equal(input, Assert.Single(vm.SelectedFiles).FullPath);
                Assert.Equal(0, services.ReadCalls);
                break;
            case OrganizePagesViewModel vm:
                Assert.Equal(input, vm.SelectedFilePath);
                await vm.LoadInputAsync(vm.SelectedFilePath);
                Assert.Single(vm.Pages);
                Assert.Equal(1, services.ReadCalls);
                break;
            default: Assert.Fail("Expected the requested tool."); break;
        }
    }

    private ServiceProvider BuildProvider(ServiceStubs stubs)
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<StartupOperationRouter>();
        collection.AddSingleton<IUserSettingsStore>(new SettingsStub(_workspace));
        collection.AddSingleton<IFileDialogService>(stubs);
        collection.AddSingleton<IMergeService>(stubs);
        collection.AddSingleton<ISplitService>(stubs);
        collection.AddSingleton<ICompressionService>(stubs);
        collection.AddSingleton<IProtectService>(stubs);
        collection.AddSingleton<IRearrangeService>(stubs);
        collection.AddSingleton<IConversionService>(stubs);
        collection.AddSingleton<IExportService>(stubs);
        collection.AddSingleton<IPdfPagePreviewService>(stubs);
        collection.AddSingleton<IPagePreviewDialogService>(stubs);
        return collection.BuildServiceProvider();
    }
    public void Dispose() => Directory.Delete(_workspace, true);
    private sealed class SettingsStub(string workspace) : IUserSettingsStore
    {
        public UserSettings Load() => new() { LastWorkspace = workspace };
        public void Save(UserSettings settings) { }
    }
    private sealed class ServiceStubs : IFileDialogService, IMergeService, ISplitService, ICompressionService,
        IProtectService, IRearrangeService, IConversionService, IExportService, IPdfPagePreviewService, IPagePreviewDialogService
    {
        public int ReadCalls { get; private set; }
        private OperationResult Read() { ReadCalls++; return new(false, "", "Unexpected file processing."); }
        public Task<IReadOnlyList<string>> PickPdfFilesAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<string>> PickConvertibleFilesAsync(bool allowMultiple = true) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> ShowSaveFileDialogAsync(string title, string defaultFileName, string extension) => Task.FromResult<string?>(null);
        public OperationResult Merge(IReadOnlyList<PdfFile> inputs, MergeOptions options) => Read();
        public OperationResult Split(PdfFile input, SplitOptions options) => Read();
        public OperationResult Compress(PdfFile input, CompressionOptions options) => Read();
        public OperationResult Protect(PdfFile input, ProtectOptions options) => Read();
        public OperationResult Unlock(PdfFile input, UnlockOptions options) => Read();
        public int? TryGetPageCount(PdfFile input) { ReadCalls++; return 1; }
        public OperationResult Rearrange(PdfFile input, IReadOnlyList<PageArrangementItem> pages, RearrangeOptions options) => Read();
        public OperationResult Convert(string input, ConversionOptions options) => Read();
        public OperationResult Export(IReadOnlyList<PdfFile> inputs, ExportOptions options) => Read();
        public PagePreviewResult RenderPage(string filePath, int pageNumber) { ReadCalls++; return new(false, null, "Unexpected preview."); }
        public Task ShowAsync(PagePreviewRequest request) => Task.CompletedTask;
    }
}
