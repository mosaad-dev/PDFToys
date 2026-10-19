using PdfSharp.Pdf.IO;
using PDFToys.Core.Models;

namespace PDFToys.Core.Services;

/// <summary>Writes a validated candidate before committing to a new, non-overwriting path.</summary>
public sealed class OutputFileTransaction : IDisposable
{
    private readonly string _desiredPath;
    private readonly string? _sourcePath;
    private bool _committed;

    private OutputFileTransaction(string desiredPath, string? sourcePath)
    {
        _desiredPath = Path.GetFullPath(desiredPath);
        _sourcePath = string.IsNullOrWhiteSpace(sourcePath) ? null : Path.GetFullPath(sourcePath);
        var directory = Path.GetDirectoryName(_desiredPath)
            ?? throw new ArgumentException("An output directory is required.", nameof(desiredPath));
        Directory.CreateDirectory(directory);
        CandidatePath = Path.Combine(directory, $".pdftoys-{Guid.NewGuid():N}{Path.GetExtension(_desiredPath)}");
    }

    public string CandidatePath { get; }

    public static OutputFileTransaction Create(string desiredPath, string? sourcePath = null) => new(desiredPath, sourcePath);

    public static OutputFileTransaction CreatePdf(string inputPath, string outputDirectory, string suffix) =>
        Create(Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(inputPath)}_{suffix}.pdf"), inputPath);

    public string CommitPdf(string? password = null) => Commit(path =>
    {
        using var document = PdfReader.Open(path, password ?? string.Empty, PdfDocumentOpenMode.Import);
        return document.PageCount > 0;
    });

    public string Commit(Func<string, bool>? validator = null)
    {
        if (_committed) throw new InvalidOperationException("Output has already been committed.");
        OperationContext.ThrowIfCancellationRequested();
        if (!File.Exists(CandidatePath) || new FileInfo(CandidatePath).Length == 0)
            throw new InvalidDataException("The operation did not create a non-empty output file.");
        if (validator is not null && !validator(CandidatePath))
            throw new InvalidDataException("The generated output could not be validated.");
        OperationContext.ThrowIfCancellationRequested();

        for (var attempt = 0; attempt < 10000; attempt++)
        {
            var finalPath = SafeOutputFiles.GetUniquePath(_desiredPath);
            if (_sourcePath is not null && string.Equals(finalPath, _sourcePath, StringComparison.OrdinalIgnoreCase))
                finalPath = SafeOutputFiles.GetUniquePath(SafeOutputFiles.AddSuffix(_desiredPath, "_copy"));
            try
            {
                File.Move(CandidatePath, finalPath, overwrite: false);
                _committed = true;
                return finalPath;
            }
            catch (IOException) when (File.Exists(finalPath) || Directory.Exists(finalPath))
            {
                OperationContext.ThrowIfCancellationRequested();
            }
        }
        throw new IOException("Could not reserve a unique output filename.");
    }

    public void Dispose()
    {
        if (_committed) return;
        try { File.Delete(CandidatePath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

public static class SafeOutputFiles
{
    public static string GetUniquePath(string desiredPath)
    {
        var absolute = Path.GetFullPath(desiredPath);
        if (!File.Exists(absolute) && !Directory.Exists(absolute)) return absolute;
        for (var number = 1; number < 100000; number++)
        {
            var candidate = AddSuffix(absolute, $"_{number}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        throw new IOException("Could not find an available output name.");
    }

    public static string AddSuffix(string path, string suffix) =>
        Path.Combine(Path.GetDirectoryName(path) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(path)}{suffix}{Path.GetExtension(path)}");

    public static string MoveToUniquePath(string actualPath, string desiredPath)
    {
        var actual = Path.GetFullPath(actualPath);
        var desired = Path.GetFullPath(desiredPath);
        if (string.Equals(actual, desired, StringComparison.OrdinalIgnoreCase)) return actual;
        if (!File.Exists(actual)) throw new FileNotFoundException("The generated output was not found.", actual);
        // Stage on the destination volume; never overwrite a previous output.
        using var transaction = OutputFileTransaction.Create(desired);
        File.Copy(actual, transaction.CandidatePath, overwrite: false);
        var finalPath = transaction.Commit();
        try { File.Delete(actual); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return finalPath;
    }

    public static bool TryReplaceOriginal(string candidatePath, string originalPath, out string backupPath, out string error, string? password = null)
    {
        backupPath = string.Empty;
        error = string.Empty;
        try
        {
            var candidate = Path.GetFullPath(candidatePath);
            var original = Path.GetFullPath(originalPath);
            if (string.Equals(candidate, original, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Replacement must be generated separately from the original.");
            if (!File.Exists(candidate) || new FileInfo(candidate).Length == 0)
                throw new IOException("The replacement output was not created.");
            if (!File.Exists(original)) throw new FileNotFoundException("The original file no longer exists.", original);
            using (var replacement = PdfReader.Open(candidate, password ?? string.Empty, PdfDocumentOpenMode.Import))
            {
                if (replacement.PageCount == 0) throw new InvalidDataException("The replacement PDF has no pages.");
            }
            OperationContext.ThrowIfCancellationRequested();
            // File.Replace creates the backup and replaces as one filesystem operation.
            // On filesystems that do not support this, fail safely instead of copy/delete.
            backupPath = original + $".{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.pdftoys.bak";
            using var staging = OutputFileTransaction.Create(Path.Combine(Path.GetDirectoryName(original)!, Path.GetFileName(original)));
            File.Copy(candidate, staging.CandidatePath, overwrite: false);
            using (var staged = File.OpenRead(staging.CandidatePath))
            using (var supplied = File.OpenRead(candidate))
            {
                if (staged.Length != supplied.Length) throw new IOException("Replacement validation failed.");
            }
            OperationContext.ThrowIfCancellationRequested();
            File.Replace(staging.CandidatePath, original, backupPath, ignoreMetadataErrors: false);
            try { File.Delete(candidate); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
