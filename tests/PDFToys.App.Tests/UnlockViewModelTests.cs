using PDFToys.App.Models;
using PDFToys.App.Services;
using PDFToys.App.ViewModels;
using PDFToys.Core.Contracts;
using PDFToys.Core.Models;
using PDFToys.Core.Services;

namespace PDFToys.App.Tests;

public sealed class UnlockViewModelTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"pdftoys-unlock-view-model-{Guid.NewGuid():N}");

    public UnlockViewModelTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task ExecuteUnlockCommand_WithoutFiles_ShowsValidationMessage()
    {
        var service = new ProtectServiceStub(CreateSuccessfulResult);
        var viewModel = CreateViewModel(service);
        viewModel.Password = "secret";

        await viewModel.ExecuteUnlockCommand.ExecuteAsync(null);

        Assert.Equal("Please select PDF files first.", viewModel.StatusMessage);
        Assert.Empty(service.UnlockCalls);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task ExecuteUnlockCommand_WithoutPassword_ShowsValidationMessage()
    {
        var inputPath = CreateInput("password-required.pdf");
        var service = new ProtectServiceStub(CreateSuccessfulResult);
        var viewModel = CreateViewModel(service, [inputPath]);

        await viewModel.ExecuteUnlockCommand.ExecuteAsync(null);

        Assert.Equal("Password is required.", viewModel.StatusMessage);
        Assert.Empty(service.UnlockCalls);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public void Constructor_IgnoresDuplicateInitialFiles()
    {
        var inputPath = CreateInput("duplicate.pdf");
        var service = new ProtectServiceStub(CreateSuccessfulResult);

        var viewModel = CreateViewModel(service, [inputPath, inputPath, ""]);

        var selectedFile = Assert.Single(viewModel.SelectedFiles);
        Assert.Equal(inputPath, selectedFile.FullPath);
        Assert.IsType<PdfFileItem>(selectedFile);
    }

    [Fact]
    public void Constructor_LoadsDefaultOutputModeFromSettings()
    {
        var service = new ProtectServiceStub(CreateSuccessfulResult);
        var settingsStore = new UserSettingsStoreStub(new UserSettings
        {
            DefaultPdfOutputMode = nameof(PdfOutputMode.ReplaceOriginal)
        });

        var viewModel = CreateViewModel(service, settingsStore: settingsStore);

        Assert.Equal(PdfOutputMode.ReplaceOriginal, viewModel.OutputMode.SelectedOutputMode);
        Assert.Equal(1, settingsStore.LoadCalls);
    }

    [Fact]
    public async Task ExecuteUnlockCommand_WithValidInput_ProducesCopyAndKeepsPassword()
    {
        var inputPath = CreateInput("protected.pdf");
        var expectedOutputPath = Path.Combine(_tempDirectory, "protected_Unlocked.pdf");
        var service = new ProtectServiceStub(CreateSuccessfulResult);
        var viewModel = CreateViewModel(service, [inputPath]);
        viewModel.Password = "secret";

        await viewModel.ExecuteUnlockCommand.ExecuteAsync(null);

        var call = Assert.Single(service.UnlockCalls);
        Assert.Equal(inputPath, call.Input.FilePath);
        Assert.Equal("secret", call.Options.Password);
        Assert.Equal(Path.GetDirectoryName(inputPath), call.Options.OutputDirectory);
        Assert.True(File.Exists(expectedOutputPath));
        Assert.Equal("Unlock: 1/1 files completed.", viewModel.StatusMessage);
        Assert.Equal("secret", viewModel.Password);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task ExecuteUnlockCommand_WithPartialFailure_ReportsFailureAndKeepsPassword()
    {
        var successfulInput = CreateInput("successful.pdf");
        var failedInput = CreateInput("failed.pdf");
        var service = new ProtectServiceStub((input, options) =>
            input.FilePath == failedInput
                ? new OperationResult(false, string.Empty, "Incorrect password.")
                : CreateSuccessfulResult(input, options));
        var viewModel = CreateViewModel(service, [successfulInput, failedInput]);
        viewModel.Password = "secret";

        await viewModel.ExecuteUnlockCommand.ExecuteAsync(null);

        Assert.Equal(2, service.UnlockCalls.Count);
        Assert.Contains("1 file(s) need attention", viewModel.StatusMessage);
        var failure = Assert.Single(viewModel.Results, r => !r.Result.IsSuccess);
        Assert.Equal(failedInput, failure.InputPath);
        Assert.Contains("Incorrect password.", failure.Details);
        Assert.Equal("secret", viewModel.Password);
    }

    [Fact]
    public async Task ExecuteUnlockCommand_WhenReplacementOutputIsMissing_ReportsFailure()
    {
        var inputPath = CreateInput("missing-output.pdf");
        var missingOutputPath = Path.Combine(_tempDirectory, "does-not-exist.pdf");
        var service = new ProtectServiceStub((_, _) =>
            new OperationResult(false, string.Empty, "Recovery copy could not be verified."));
        var viewModel = CreateViewModel(service, [inputPath]);
        viewModel.Password = "secret";
        viewModel.OutputMode.ApplyMode(PdfOutputMode.ReplaceOriginal);

        await viewModel.ExecuteUnlockCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(service.UnlockCalls).Options.ReplaceOriginal);
        Assert.Contains("Recovery copy could not be verified.", Assert.Single(viewModel.Results).Details);
        Assert.Equal("secret", viewModel.Password);
        Assert.True(File.Exists(inputPath));
    }

    [Fact]
    public async Task ExecuteUnlockCommand_AfterSuccessfulRun_PersistsOutputMode()
    {
        var inputPath = CreateInput("settings.pdf");
        var service = new ProtectServiceStub(CreateSuccessfulResult);
        var settingsStore = new UserSettingsStoreStub(new UserSettings());
        var viewModel = CreateViewModel(service, [inputPath], settingsStore);
        viewModel.Password = "secret";
        viewModel.OutputMode.ApplyMode(PdfOutputMode.ReplaceOriginal);

        await viewModel.ExecuteUnlockCommand.ExecuteAsync(null);

        var savedSettings = Assert.Single(settingsStore.SavedSettings);
        Assert.Equal(nameof(PdfOutputMode.ReplaceOriginal), savedSettings.DefaultPdfOutputMode);
        Assert.Equal("Unlock: 1/1 files completed.", viewModel.StatusMessage);
        Assert.True(File.Exists(inputPath));
        Assert.True(Assert.Single(service.UnlockCalls).Options.ReplaceOriginal);
    }

    [Fact]
    public async Task ExecuteUnlockCommand_WhileRunning_DisablesExecuteCommand()
    {
        var inputPath = CreateInput("busy.pdf");
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var service = new ProtectServiceStub((input, options) =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            return CreateSuccessfulResult(input, options);
        });
        var viewModel = CreateViewModel(service, [inputPath]);
        viewModel.Password = "secret";

        var execution = viewModel.ExecuteUnlockCommand.ExecuteAsync(null);
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));

        try
        {
            Assert.True(viewModel.IsBusy);
            Assert.False(viewModel.ExecuteUnlockCommand.CanExecute(null));
        }
        finally
        {
            release.Set();
        }

        await execution.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.ExecuteUnlockCommand.CanExecute(null));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private UnlockViewModel CreateViewModel(
        ProtectServiceStub service,
        IEnumerable<string>? initialFiles = null,
        IUserSettingsStore? settingsStore = null)
    {
        return new UnlockViewModel(
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
        UnlockOptions options)
    {
        var outputPath = Path.Combine(
            options.OutputDirectory,
            $"{Path.GetFileNameWithoutExtension(input.FilePath)}_Unlocked.pdf");
        File.Copy(input.FilePath, outputPath, overwrite: true);
        return new OperationResult(true, outputPath, string.Empty);
    }

    private sealed class ProtectServiceStub(
        Func<PdfFile, UnlockOptions, OperationResult> unlockHandler) : IProtectService
    {
        public List<(PdfFile Input, UnlockOptions Options)> UnlockCalls { get; } = [];

        public OperationResult Protect(PdfFile input, ProtectOptions options)
        {
            throw new NotSupportedException();
        }

        public OperationResult Unlock(PdfFile input, UnlockOptions options)
        {
            UnlockCalls.Add((input, options));
            return unlockHandler(input, options);
        }
    }

    private sealed class UserSettingsStoreStub(UserSettings settings) : IUserSettingsStore
    {
        public int LoadCalls { get; private set; }

        public List<UserSettings> SavedSettings { get; } = [];

        public UserSettings Load()
        {
            LoadCalls++;
            return settings;
        }

        public void Save(UserSettings savedSettings)
        {
            SavedSettings.Add(new UserSettings
            {
                LastWorkspace = savedSettings.LastWorkspace,
                DefaultPdfOutputMode = savedSettings.DefaultPdfOutputMode,
                DefaultCompressionLevel = savedSettings.DefaultCompressionLevel
            });
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
}
