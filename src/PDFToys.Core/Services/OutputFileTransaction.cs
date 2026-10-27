using PdfSharp.Pdf.IO;
using PDFToys.Core.Models;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

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
            // FullPath does not resolve junctions, symbolic links, short names or hard
            // links. Require stable identities before any backup/replacement activity.
            // Refuse platforms/filesystems without this facility rather than using an
            // unsafe path-only fallback for a destructive operation.
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Safe replacement of originals is supported only on Windows. Keep the generated copy instead.");
            using var originalHandle = File.OpenHandle(original, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var originalIdentity = WindowsReplacementFiles.GetIdentity(originalHandle);
            OperationContext.ThrowIfCancellationRequested();
            using var staging = OutputFileTransaction.Create(Path.Combine(Path.GetDirectoryName(original)!, Path.GetFileName(original)));
            WindowsReplacementFiles.FileIdentity candidateIdentity;
            using (var supplied = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                candidateIdentity = WindowsReplacementFiles.GetIdentity(supplied.SafeFileHandle);
                if (candidateIdentity == originalIdentity)
                    throw new IOException("Replacement must be a different file from the original, including linked or aliased paths.");
                // Copy from the inspected handle while denying writes and deletion of
                // that file. Validate the actual staged bytes that will replace it.
                using (var staged = new FileStream(staging.CandidatePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    supplied.CopyTo(staged);
                    staged.Flush(flushToDisk: true);
                }
                using (var replacement = PdfReader.Open(staging.CandidatePath, password ?? string.Empty, PdfDocumentOpenMode.Import))
                {
                    if (replacement.PageCount == 0) throw new InvalidDataException("The replacement PDF has no pages.");
                }
                OperationContext.ThrowIfCancellationRequested();
                // File.Replace creates a distinct recovery backup atomically. There
                // is deliberately no copy/delete fallback if the filesystem refuses it.
                backupPath = original + $".{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.pdftoys.bak";
                File.Replace(staging.CandidatePath, original, backupPath, ignoreMetadataErrors: false);
            }
            // A parent junction could now resolve somewhere else. Delete only the
            // inspected candidate, through its handle; never follow a fresh path delete.
            WindowsReplacementFiles.TryDeleteInspectedCandidate(candidate, candidateIdentity);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static class WindowsReplacementFiles
    {
        internal readonly record struct FileIdentity(ulong Volume, ulong Low, ulong High);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeFileIdentity
        {
            public ulong Volume;
            public ulong Low;
            public ulong High;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileDisposition
        {
            public byte DeleteFile;
        }

        internal static FileIdentity GetIdentity(SafeFileHandle handle)
        {
            if (!GetFileInformationByHandleEx(handle, 18 /* FileIdInfo */, out var identity,
                    (uint)Marshal.SizeOf<NativeFileIdentity>()))
                throw new IOException("The filesystem cannot provide the stable file identity required for safe replacement.",
                    new Win32Exception(Marshal.GetLastPInvokeError()));
            return new(identity.Volume, identity.Low, identity.High);
        }

        internal static void TryDeleteInspectedCandidate(string path, FileIdentity expected)
        {
            try
            {
                var extendedPath = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path
                    : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
                using var handle = CreateFile(extendedPath, 0x00010080 /* DELETE | FILE_READ_ATTRIBUTES */,
                    7 /* share read/write/delete */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
                if (handle.IsInvalid || GetIdentity(handle) != expected) return;
                var disposition = new FileDisposition { DeleteFile = 1 };
                _ = SetFileInformationByHandle(handle, 4 /* FileDispositionInfo */, ref disposition,
                    (uint)Marshal.SizeOf<FileDisposition>());
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            // Cleanup is best effort. A retained candidate is safer than deleting a
            // path that no longer denotes the generated file.
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
            out NativeFileIdentity information, uint size);

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
            uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass,
            ref FileDisposition information, uint size);
    }
}
