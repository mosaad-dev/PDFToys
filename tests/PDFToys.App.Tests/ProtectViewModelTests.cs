using PDFToys.App.Services;
using PDFToys.App.ViewModels;
using PDFToys.Core.Contracts;
using PDFToys.Core.Models;
using PDFToys.Core.Services;

namespace PDFToys.App.Tests;

public sealed class ProtectViewModelTests
{
    [Fact]
    public async Task ExecuteProtect_WithoutFiles_ShowsValidationMessage()
    {
        var service = new ProtectServiceStub();
        var viewModel = CreateViewModel(service);

        viewModel.Password = "secret";
        await viewModel.ExecuteProtectCommand.ExecuteAsync(null);

        Assert.Equal("Please select PDF files first.", viewModel.StatusMessage);
        Assert.Equal(0, service.ProtectCallCount);
    }

    [Fact]
    public async Task ExecuteProtect_WithoutPassword_ShowsValidationMessage()
    {
        var service = new ProtectServiceStub();
        var viewModel = CreateViewModel(service, ["document.pdf"]);

        await viewModel.ExecuteProtectCommand.ExecuteAsync(null);

        Assert.Equal("Password is required.", viewModel.StatusMessage);
        Assert.Equal(0, service.ProtectCallCount);
    }

    [Fact]
    public async Task ExecuteProtect_WithValidInput_CreatesProtectedCopy()
    {
        var inputPath = Path.Combine(
            Path.GetTempPath(),
            $"pdftoys-protect-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(inputPath, []);

        try
        {
            var service = new ProtectServiceStub();
            var viewModel = CreateViewModel(service, [inputPath]);
            viewModel.Password = "secret";

            await viewModel.ExecuteProtectCommand.ExecuteAsync(null);

            Assert.Equal(1, service.ProtectCallCount);
            Assert.Equal(inputPath, service.LastInput?.FilePath);
            Assert.Equal("secret", service.LastOptions?.Password);
            Assert.Equal(Path.GetDirectoryName(inputPath), service.LastOptions?.OutputDirectory);
            Assert.Equal("Protect: 1/1 files completed.", viewModel.StatusMessage);
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    private static ProtectViewModel CreateViewModel(
        ProtectServiceStub service,
        IEnumerable<string>? initialFiles = null)
    {
        return new ProtectViewModel(
            service,
            new FileDialogStub(),
            () => { },
            initialFiles,
            runner: new ApplicationJobRunner());
    }

    private sealed class ProtectServiceStub : IProtectService
    {
        public int ProtectCallCount { get; private set; }

        public PdfFile? LastInput { get; private set; }

        public ProtectOptions? LastOptions { get; private set; }

        public OperationResult Protect(PdfFile input, ProtectOptions options)
        {
            ProtectCallCount++;
            LastInput = input;
            LastOptions = options;
            return new OperationResult(true, input.FilePath + ".protected.pdf", string.Empty);
        }

        public OperationResult Unlock(PdfFile input, UnlockOptions options)
        {
            throw new NotSupportedException();
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
