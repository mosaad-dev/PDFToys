using System;
using PDFToys.Core.Services;

namespace PDFToys.App.Services;

/// <summary>Compatibility adapter for callers migrated to Core output transactions.</summary>
public static class OutputFileHelper
{
    public const string BackupExtension = ".pdftoys.bak";

    public static string? MoveToExpectedPath(string actualPath, string desiredPath)
    {
        if (string.IsNullOrWhiteSpace(actualPath) || string.IsNullOrWhiteSpace(desiredPath)) return null;
        try { return SafeOutputFiles.MoveToUniquePath(actualPath, desiredPath); }
        catch (Exception) { return null; }
    }

    public static bool TryReplaceOriginal(string newFilePath, string originalInputPath) =>
        SafeOutputFiles.TryReplaceOriginal(newFilePath, originalInputPath, out _, out _);

    public static string BuildBackupPath(string originalInputPath) => originalInputPath + BackupExtension;
}