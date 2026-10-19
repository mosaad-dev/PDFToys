namespace PDFToys.Core.Models;

public sealed record ProtectOptions(string Password, string OutputDirectory, bool ReplaceOriginal = false);
