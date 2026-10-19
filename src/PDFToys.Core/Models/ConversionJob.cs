namespace PDFToys.Core.Models;

public enum ConversionJobKind { ConvertToPdf, ConvertEachToPdf, CombineToPdf, Export }

/// <summary>Immutable selection/options snapshot, shared by desktop and command-line entry points.</summary>
public sealed class ConversionJob
{
    public ConversionJobKind Kind { get; }
    public IReadOnlyList<string> Inputs { get; }
    public string? OutputDirectory { get; }
    public PdfExportFormat Format { get; }

    public ConversionJob(ConversionJobKind kind, IEnumerable<string> inputs, string? outputDirectory = null,
        PdfExportFormat format = PdfExportFormat.Png)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        Kind = kind;
        Inputs = Array.AsReadOnly(inputs.ToArray());
        OutputDirectory = outputDirectory;
        Format = format;
    }
}
