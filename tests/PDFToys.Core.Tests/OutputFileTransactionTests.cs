using PDFToys.Core.Models;
using PDFToys.Core.Services;

namespace PDFToys.Core.Tests;

public sealed class OutputFileTransactionTests : PdfTestBase
{
    [Fact]
    public void CommitPdf_KeepsExistingDestinationAndUsesDistinctName()
    {
        var source = CreatePdf("source.pdf", 2);
        var desired = CreatePdf("output.pdf", 1);
        var original = File.ReadAllBytes(desired);
        using var transaction = OutputFileTransaction.Create(desired, source);
        File.Copy(source, transaction.CandidatePath);
        var result = transaction.CommitPdf();
        Assert.NotEqual(desired, result);
        Assert.Equal(original, File.ReadAllBytes(desired));
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(result));
    }

    [Fact]
    public void InvalidCandidate_PreservesPreviousOutputAndCleansUp()
    {
        var desired = CreatePdf("output.pdf", 1);
        var original = File.ReadAllBytes(desired);
        string temporary;
        using (var transaction = OutputFileTransaction.Create(desired))
        {
            temporary = transaction.CandidatePath;
            File.WriteAllText(temporary, "invalid pdf");
            Assert.ThrowsAny<Exception>(() => transaction.CommitPdf());
        }
        Assert.Equal(original, File.ReadAllBytes(desired));
        Assert.False(File.Exists(temporary));
    }

    [Fact]
    public void CancellationBeforeCommit_PreservesExistingFiles()
    {
        var source = CreatePdf("source.pdf", 1);
        var desired = CreatePdf("output.pdf", 2);
        var original = File.ReadAllBytes(desired);
        using var cancellation = new CancellationTokenSource();
        using var context = OperationContext.Begin(cancellation.Token);
        using var transaction = OutputFileTransaction.Create(desired, source);
        File.Copy(source, transaction.CandidatePath);
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => transaction.CommitPdf());
        Assert.Equal(original, File.ReadAllBytes(desired));
    }

    [Fact]
    public void CommitToSourcePath_AllocatesCopyWithoutReplacingSource()
    {
        var source = CreatePdf("source.pdf", 1);
        var original = File.ReadAllBytes(source);
        using var transaction = OutputFileTransaction.Create(source, source);
        File.Copy(source, transaction.CandidatePath);
        var result = transaction.CommitPdf();
        Assert.NotEqual(source, result);
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [Fact]
    public void ReplaceOriginal_ProducesVerifiedDistinctBackup()
    {
        var original = CreatePdf("original.pdf", 1);
        var replacement = CreatePdf("replacement.pdf", 2);
        var originalBytes = File.ReadAllBytes(original);
        var newBytes = File.ReadAllBytes(replacement);
        Assert.True(SafeOutputFiles.TryReplaceOriginal(replacement, original, out var backup, out var error), error);
        Assert.Equal(originalBytes, File.ReadAllBytes(backup));
        Assert.Equal(newBytes, File.ReadAllBytes(original));
        Assert.False(File.Exists(replacement));
    }

    [Fact]
    public void ReplaceOriginal_InvalidPdfLeavesOriginalUntouched()
    {
        var original = CreatePdf("original.pdf", 1);
        var replacement = Path.Combine(TempDirectory, "replacement.pdf");
        var originalBytes = File.ReadAllBytes(original);
        File.WriteAllText(replacement, "not a PDF");
        Assert.False(SafeOutputFiles.TryReplaceOriginal(replacement, original, out _, out var error));
        Assert.NotEmpty(error);
        Assert.Equal(originalBytes, File.ReadAllBytes(original));
    }

    [Fact]
    public void ReplaceOriginal_LockedFileFailsWithoutLosingEitherFile()
    {
        var original = CreatePdf("original.pdf", 1);
        var replacement = CreatePdf("replacement.pdf", 2);
        var originalBytes = File.ReadAllBytes(original);
        using (var locked = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False(SafeOutputFiles.TryReplaceOriginal(replacement, original, out _, out var error));
            Assert.NotEmpty(error);
            Assert.True(File.Exists(replacement));
        }
        Assert.Equal(originalBytes, File.ReadAllBytes(original));
    }

    [Fact]
    public async Task ConcurrentCommits_GetDifferentNames()
    {
        var source = CreatePdf("source.pdf", 1);
        var desired = Path.Combine(TempDirectory, "output.pdf");
        var paths = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
        {
            using var transaction = OutputFileTransaction.Create(desired, source);
            File.Copy(source, transaction.CandidatePath);
            return transaction.CommitPdf();
        })));
        Assert.Equal(12, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(paths, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public async Task Context_SeparatesConcurrentJobsAndRestoresPreviousScope()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using (OperationContext.Begin(cancelled.Token))
        {
            Assert.True(OperationContext.CancellationToken.IsCancellationRequested);
            await Task.Run(() =>
            {
                using var inner = OperationContext.Begin();
                Assert.False(OperationContext.CancellationToken.IsCancellationRequested);
            });
            Assert.True(OperationContext.CancellationToken.IsCancellationRequested);
        }
        Assert.False(OperationContext.CancellationToken.IsCancellationRequested);
    }
}
