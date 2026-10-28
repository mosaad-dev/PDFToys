using System.Diagnostics;
using System.Security.Cryptography;
using Avalonia.Headless;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using PDFToys.App;
using PDFToys.App.Models;
using PDFToys.App.Services;
using PDFToys.App.ViewModels;
using PDFToys.Core.Contracts;
using PDFToys.Core.Models;

internal static class LifecycleSmoke
{
    public static int Run(string output)
    {
        foreach (var closeWindow in new[] { false, true })
        {
            var name = closeWindow ? "close" : "cancel";
            var input = Path.Combine(output, $"lifecycle-{name}.pdf");
            using (var pdf = new PdfSharp.Pdf.PdfDocument()) { pdf.AddPage(); pdf.Save(input); }
            var originalHash = SHA256.HashData(File.ReadAllBytes(input));
            using var operation = new ControlledCompression();
            using var services = new ServiceCollection().AddPdfToysCore()
                .AddSingleton<StartupOperationRouter>()
                .AddSingleton<IUserSettingsStore>(new PreviewSettings())
                .AddSingleton<IFileDialogService, AvaloniaFileDialogService>()
                .AddSingleton<IPdfPagePreviewService, PdfPagePreviewService>()
                .AddSingleton<IPagePreviewDialogService, AvaloniaPagePreviewDialogService>()
                .AddSingleton<ICompressionService>(operation)
                .BuildServiceProvider();
            var main = new MainViewModel(services, new(PdfToysOperation.Compress, [input]));
            var viewModel = (CompressViewModel)main.CurrentPage;
            var window = new MainWindow { DataContext = main };
            window.Show();
            try
            {
                var uiThread = Environment.CurrentManagedThreadId;
                var execution = viewModel.ExecuteCompressCommand.ExecuteAsync(null);
                if (!operation.Started.Wait(TimeSpan.FromSeconds(3))) throw new Exception("Background job did not start.");
                if (!viewModel.IsBusy || operation.WorkerThread == uiThread) throw new Exception("Processing did not leave the UI thread.");
                using (var frame = window.CaptureRenderedFrame() ?? throw new Exception("Busy UI did not render."))
                    frame.Save(Path.Combine(output, $"lifecycle-{name}-busy.png"));
                if (closeWindow) window.Close();
                else viewModel.CancelCommand.Execute(null);
                if (!viewModel.IsCancelling || !window.IsVisible) throw new Exception("Cancellation did not wait for the current safe step.");
                operation.Release.Set();
                PumpUntil(() => execution.IsCompleted && !viewModel.IsBusy && (!closeWindow || !window.IsVisible));
                execution.GetAwaiter().GetResult();
                if (!viewModel.StatusMessage.StartsWith("Cancelled", StringComparison.Ordinal)) throw new Exception("Cancelled job was reported incorrectly.");
                if (!originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(input)))) throw new Exception("Cancellation changed the source.");
                if (!closeWindow && !window.IsVisible) throw new Exception("Cancel unexpectedly closed the window.");
            }
            finally { operation.Release.Set(); window.Close(); }
        }
        return 2;
    }

    private static void PumpUntil(Func<bool> completed)
    {
        var watch = Stopwatch.StartNew();
        while (!completed())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("UI lifecycle completion timed out.");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
    }

    private sealed class ControlledCompression : ICompressionService, IDisposable
    {
        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public int WorkerThread { get; private set; }
        public OperationResult Compress(PdfFile input, CompressionOptions options)
        {
            WorkerThread = Environment.CurrentManagedThreadId;
            Started.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Controlled step was never released.");
            OperationContext.ThrowIfCancellationRequested();
            return new(false, "", "Expected cancellation did not arrive.");
        }
        public void Dispose() { Started.Dispose(); Release.Dispose(); }
    }
}
