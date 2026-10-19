using PDFToys.Core.Models;

namespace PDFToys.Core.Contracts;

public interface IApplicationJobRunner
{
    Task<OperationResult> RunAsync(Func<OperationResult> operation, CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null);
}
