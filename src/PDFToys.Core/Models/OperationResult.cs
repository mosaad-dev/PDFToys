namespace PDFToys.Core.Models;

public enum OperationOutcome
{
    Succeeded,
    Unchanged,
    PartialSuccess,
    Cancelled,
    Failed
}

public sealed record OperationOutput(string Path, bool IsDirectory = false);

public sealed record FileOperationResult(string InputPath, OperationResult Result);

public sealed record CompressionStatistics(
    long OriginalBytes,
    long CandidateBytes,
    long FinalBytes,
    int ImagesProcessed,
    int ImagesChanged,
    int ImagesSkipped,
    IReadOnlyList<string> SkipReasons)
{
    public long BytesSaved => Math.Max(0, OriginalBytes - FinalBytes);
    public double PercentSaved => OriginalBytes == 0 ? 0 : BytesSaved * 100.0 / OriginalBytes;
}

/// <summary>Typed operation information with a compatible three-argument constructor.</summary>
public sealed record OperationResult(bool IsSuccess, string OutputPath, string ErrorMessage)
{
    public OperationOutcome Outcome { get; init; } = IsSuccess ? OperationOutcome.Succeeded : OperationOutcome.Failed;
    public string ErrorCode { get; init; } = string.Empty;
    public IReadOnlyList<OperationOutput> Outputs { get; init; } =
        IsSuccess && !string.IsNullOrWhiteSpace(OutputPath)
            ? [new OperationOutput(OutputPath, Directory.Exists(OutputPath))]
            : [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<FileOperationResult> Files { get; init; } = [];
    public CompressionStatistics? Compression { get; init; }

    public static OperationResult Cancelled(string message = "Operation cancelled.") =>
        new(false, string.Empty, message) { Outcome = OperationOutcome.Cancelled, ErrorCode = "cancelled" };
}
