using PDFToys.Core.Contracts;
using PDFToys.Core.Models;

namespace PDFToys.Core.Services;

/// <summary>One bounded application job at a time. Services remain synchronous and share an ambient operation scope.</summary>
public sealed class ApplicationJobRunner : IApplicationJobRunner
{
    private int _running;
    public static ApplicationJobRunner Instance { get; } = new();

    public async Task<OperationResult> RunAsync(Func<OperationResult> operation, CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return new(false, string.Empty, "Another operation is already running. Wait for it to finish or cancel it.") { ErrorCode = "Busy" };
        try
        {
            return await Task.Run(() =>
            {
                using var scope = OperationContext.Begin(cancellationToken, progress is null ? null : new ThrottledProgress(progress));
                try
                {
                    OperationContext.ThrowIfCancellationRequested();
                    return operation();
                }
                catch (OperationCanceledException) { return OperationResult.Cancelled(); }
                catch (Exception ex) { return new(false, string.Empty, ex.Message) { ErrorCode = "OperationFailed" }; }
            }).ConfigureAwait(false);
        }
        finally { Volatile.Write(ref _running, 0); }
    }

    private sealed class ThrottledProgress(IProgress<OperationProgress> target) : IProgress<OperationProgress>
    {
        private long _lastTicks;
        private string? _lastStage;
        private string? _lastFile;
        public void Report(OperationProgress value)
        {
            var now = Environment.TickCount64;
            if (value.Stage == _lastStage && value.CurrentFile == _lastFile && now - _lastTicks < 80 && value.CompletedPages != value.TotalPages) return;
            _lastTicks = now;
            _lastStage = value.Stage;
            _lastFile = value.CurrentFile;
            target.Report(value);
        }
    }
}
