using PDFToys.App.Models;
using PDFToys.App.Services;
using PDFToys.App.ViewModels;
using PDFToys.Core.Contracts;
using PDFToys.Core.Models;
using PDFToys.Core.Services;

namespace PDFToys.App.Tests;

public sealed class CompressViewModelTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"pdftoys-compress-view-model-{Guid.NewGuid():N}");

    public CompressViewModelTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task ExecuteCompress_WithoutFiles_ShowsValidationMessage()
    {
        var service = new CompressionServiceStub(CreateSuccessfulResult);
        var viewModel = CreateViewModel(service);

        await viewModel.ExecuteCompressCommand.ExecuteAsync(null);

        Assert.Equal("Please select PDF files first.", viewModel.StatusMessage);
        Assert.Empty(service.Calls);
    }

    [Fact]
    public void Constructor_IgnoresDuplicateInitialFiles()
    {
        var inputPath = CreateInput("duplicate.pdf");
        var service = new CompressionServiceStub(CreateSuccessfulResult);

        var viewModel = CreateViewModel(service, [inputPath, inputPath, ""]);

        var selectedFile = Assert.Single(viewModel.SelectedFiles);
        Assert.Equal(inputPath, selectedFile.FullPath);
        Assert.IsType<PdfFileItem>(selectedFile);
    }

    [Theory]
    [InlineData(CompressionLevel.Normal)]
    [InlineData(CompressionLevel.Maximum)]
    public async Task ExecuteCompress_UsesSelectedCompressionLevel(
        CompressionLevel level)
    {
        var inputPath = CreateInput($"{level}.pdf");
        var service = new CompressionServiceStub(CreateSuccessfulResult);
        var viewModel = CreateViewModel(service, [inputPath]);
        viewModel.SelectedCompressionLevel = level == CompressionLevel.Maximum
            ? "Maximum"
            : "Standard";

        await viewModel.ExecuteCompressCommand.ExecuteAsync(null);

        var call = Assert.Single(service.Calls);
        Assert.Equal(level, call.Options.Level);
        Assert.Equal(Path.GetDirectoryName(inputPath), call.Options.OutputDirectory);
        Assert.Equal("Compression: 1/1 files completed.", viewModel.StatusMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task ExecuteCompress_WithPartialFailure_ReportsFailedFile()
    {
        var successfulInput = CreateInput("successful.pdf");
        var failedInput = CreateInput("failed.pdf");
        var service = new CompressionServiceStub((input, options) =>
            input.FilePath == failedInput
                ? new OperationResult(false, string.Empty, "Test failure.")
                : CreateSuccessfulResult(input, options));
        var viewModel = CreateViewModel(service, [successfulInput, failedInput]);

        await viewModel.ExecuteCompressCommand.ExecuteAsync(null);

        Assert.Equal(2, service.Calls.Count);
        Assert.Contains("1 file(s) need attention", viewModel.StatusMessage);
        var failure = Assert.Single(viewModel.Results, r => !r.Result.IsSuccess);
        Assert.Equal(failedInput, failure.InputPath);
        Assert.Contains("Test failure.", failure.Details);
    }

    [Fact]
    public async Task ExecuteCompress_WhileRunning_DisablesExecuteCommand()
    {
        var inputPath = CreateInput("busy.pdf");
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var service = new CompressionServiceStub((input, options) =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            return CreateSuccessfulResult(input, options);
        });
        var viewModel = CreateViewModel(service, [inputPath]);

        var execution = viewModel.ExecuteCompressCommand.ExecuteAsync(null);
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        try
        {
            Assert.True(viewModel.IsBusy);
            Assert.False(viewModel.ExecuteCompressCommand.CanExecute(null));
        }
        finally
        {
            release.Set();
        }

        await execution;
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.ExecuteCompressCommand.CanExecute(null));
    }

    [Fact]
    public void Constructor_LoadsSavedCompressionLevel()
    {
        var service = new CompressionServiceStub(CreateSuccessfulResult);
        var settingsStore = new UserSettingsStoreStub(new UserSettings
        {
            DefaultCompressionLevel = "Maximum"
        });

        var viewModel = CreateViewModel(service, settingsStore: settingsStore);

        Assert.Equal("Maximum", viewModel.SelectedCompressionLevel);
        Assert.Equal(1, settingsStore.LoadCallCount);
    }

    [Fact]
    public async Task ExecuteCompress_AfterSuccess_SavesSelectedCompressionLevel()
    {
        var inputPath = CreateInput("settings.pdf");
        var service = new CompressionServiceStub(CreateSuccessfulResult);
        var settingsStore = new UserSettingsStoreStub(new UserSettings());
        var viewModel = CreateViewModel(service, [inputPath], settingsStore);
        viewModel.SelectedCompressionLevel = "Maximum";

        await viewModel.ExecuteCompressCommand.ExecuteAsync(null);

        Assert.Equal(1, settingsStore.SaveCallCount);
        Assert.Equal("Maximum", settingsStore.SavedSettings?.DefaultCompressionLevel);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private CompressViewModel CreateViewModel(
        CompressionServiceStub service,
        IEnumerable<string>? initialFiles = null,
        IUserSettingsStore? settingsStore = null)
    {
        return new CompressViewModel(
            service,
            new FileDialogStub(),
            () => { },
            initialFiles,
            settingsStore,
            runner: new ApplicationJobRunner());
    }

    private string CreateInput(string fileName)
    {
        var path = Path.Combine(_tempDirectory, fileName);
        File.WriteAllBytes(path, [0x25, 0x50, 0x44, 0x46]);
        return path;
    }

    private static OperationResult CreateSuccessfulResult(
        PdfFile input,
        CompressionOptions options)
    {
        var outputPath = Path.Combine(
            options.OutputDirectory,
            $"{Path.GetFileNameWithoutExtension(input.FilePath)}_compressed.pdf");
        File.Copy(input.FilePath, outputPath, overwrite: true);
        return new OperationResult(true, outputPath, string.Empty);
    }

    private sealed class CompressionServiceStub(
        Func<PdfFile, CompressionOptions, OperationResult> handler) : ICompressionService
    {
        public List<(PdfFile Input, CompressionOptions Options)> Calls { get; } = [];

        public OperationResult Compress(PdfFile input, CompressionOptions options)
        {
            Calls.Add((input, options));
            return handler(input, options);
        }
    }

    private sealed class FileDialogStub : IFileDialogService
    {
        public Task<IReadOnlyList<string>> PickPdfFilesAsync()
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        public Task<IReadOnlyList<string>> PickConvertibleFilesAsync(bool allowMultiple = true)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        public Task<string?> ShowSaveFileDialogAsync(
            string title,
            string defaultFileName,
            string extension)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class UserSettingsStoreStub(UserSettings settings) : IUserSettingsStore
    {
        public int LoadCallCount { get; private set; }

        public int SaveCallCount { get; private set; }

        public UserSettings? SavedSettings { get; private set; }

        public UserSettings Load()
        {
            LoadCallCount++;
            return settings;
        }

        public void Save(UserSettings savedSettings)
        {
            SaveCallCount++;
            SavedSettings = savedSettings;
        }
    }
}
