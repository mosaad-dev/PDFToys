using PDFToys.Core.Models;
using PDFToys.Core.Services;
using System.Diagnostics;

namespace PDFToys.Core.Tests;

[Collection("Filesystem link integration")]
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

    [WindowsFact]
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

    [WindowsFact]
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

    [WindowsFact]
    public void ReplaceOriginal_DirectoryJunctionAlias_IsRejectedWithoutDeletingOriginalOrPriorBackup()
    {
        var real = Path.Combine(TempDirectory, "real");
        Directory.CreateDirectory(real);
        var original = CreatePdf(Path.Combine("real", "original.pdf"), 1);
        var originalBytes = File.ReadAllBytes(original);
        var priorBackup = original + ".prior.pdftoys.bak";
        File.WriteAllText(priorBackup, "previous recovery copy");
        var priorBytes = File.ReadAllBytes(priorBackup);
        var aliasDirectory = Path.Combine(TempDirectory, "alias");
        CreateWindowsLink("Junction", aliasDirectory, real);
        try
        {
            var alias = Path.Combine(aliasDirectory, "original.pdf");
            Assert.Equal(originalBytes, File.ReadAllBytes(alias));
            Assert.False(SafeOutputFiles.TryReplaceOriginal(alias, original, out var backup, out var error));
            Assert.Empty(backup);
            Assert.Contains("different file", error);
            Assert.Equal(originalBytes, File.ReadAllBytes(original));
            Assert.Equal(originalBytes, File.ReadAllBytes(alias));
            Assert.Equal(priorBytes, File.ReadAllBytes(priorBackup));
            Assert.Empty(Directory.GetFiles(real, ".pdftoys-*"));
        }
        finally { Directory.Delete(aliasDirectory); }
    }

    [WindowsFact]
    public void ReplaceOriginal_HardLinkAlias_IsRejectedAndBothNamesRemain()
    {
        var original = CreatePdf("hardlink-original.pdf", 1);
        var alias = Path.Combine(TempDirectory, "hardlink-alias.pdf");
        var bytes = File.ReadAllBytes(original);
        CreateWindowsLink("HardLink", alias, original);
        Assert.False(SafeOutputFiles.TryReplaceOriginal(alias, original, out var backup, out var error));
        Assert.Empty(backup);
        Assert.Contains("different file", error);
        Assert.Equal(bytes, File.ReadAllBytes(original));
        Assert.Equal(bytes, File.ReadAllBytes(alias));
    }

    [WindowsFact]
    public void ReplaceOriginal_RepeatedReplacement_RetainsEveryPriorBackup()
    {
        var original = CreatePdf("original.pdf", 1);
        var first = CreatePdf("first.pdf", 2);
        var second = CreatePdf("second.pdf", 3);
        var originalBytes = File.ReadAllBytes(original);
        var firstBytes = File.ReadAllBytes(first);
        var secondBytes = File.ReadAllBytes(second);
        Assert.True(SafeOutputFiles.TryReplaceOriginal(first, original, out var firstBackup, out var firstError), firstError);
        Assert.True(SafeOutputFiles.TryReplaceOriginal(second, original, out var secondBackup, out var secondError), secondError);
        Assert.NotEqual(firstBackup, secondBackup);
        Assert.Equal(originalBytes, File.ReadAllBytes(firstBackup));
        Assert.Equal(firstBytes, File.ReadAllBytes(secondBackup));
        Assert.Equal(secondBytes, File.ReadAllBytes(original));
        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
    }

    private static void CreateWindowsLink(string type, string path, string target)
    {
        // Junction creation needs no developer-mode symlink privilege on Windows.
        // Pass generated paths as environment values rather than embedding shell text.
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("$ErrorActionPreference='Stop'; New-Item -ItemType $env:PDFTOYS_LINK_TYPE -Path $env:PDFTOYS_LINK_PATH -Target $env:PDFTOYS_LINK_TARGET | Out-Null");
        start.Environment["PDFTOYS_LINK_TYPE"] = type;
        start.Environment["PDFTOYS_LINK_PATH"] = path;
        start.Environment["PDFTOYS_LINK_TARGET"] = target;
        using var process = Process.Start(start)!;
        Assert.True(process.WaitForExit(15000), "Timed out creating the generated link fixture.");
        Assert.True(process.ExitCode == 0, $"Creating the generated {type} fixture failed with exit code {process.ExitCode}.");
    }

    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows()) Skip = "Windows filesystem identity and junction integration test.";
        }
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

// Keep link-fixture child processes separate from concurrent native PDF rendering.
[CollectionDefinition("Filesystem link integration", DisableParallelization = true)]
public sealed class FilesystemLinkIntegrationCollection;
