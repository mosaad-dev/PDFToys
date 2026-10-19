namespace PDFToys.Core.Models;

public sealed record OperationProgress(
    string Stage,
    string? CurrentFile = null,
    int CompletedFiles = 0,
    int TotalFiles = 0,
    int? CompletedPages = null,
    int? TotalPages = null);

/// <summary>
/// Job-scoped context for the compatible synchronous service APIs. AsyncLocal flows
/// through the application runner without sharing cancellation across independent jobs.
/// </summary>
public static class OperationContext
{
    private sealed record State(CancellationToken Token, IProgress<OperationProgress>? Progress);
    private static readonly AsyncLocal<State?> Ambient = new();

    public static CancellationToken CancellationToken => Ambient.Value?.Token ?? System.Threading.CancellationToken.None;

    public static IDisposable Begin(CancellationToken cancellationToken = default, IProgress<OperationProgress>? progress = null)
    {
        var previous = Ambient.Value;
        Ambient.Value = new State(cancellationToken, progress);
        return new Scope(previous);
    }

    public static void ThrowIfCancellationRequested() => CancellationToken.ThrowIfCancellationRequested();

    public static void Report(OperationProgress progress) => Ambient.Value?.Progress?.Report(progress);

    private sealed class Scope(State? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            Ambient.Value = previous;
            _disposed = true;
        }
    }
}
