using PDFToys.Core.Models;

namespace PDFToys.Core.Services;

public abstract class ServiceBase
{
    /// <summary>
    /// Wraps PDF operations in a standard try/catch block to guarantee an OperationResult is always returned.
    /// </summary>
    protected static OperationResult ExecuteSafe(Func<OperationResult> operation)
    {
        try
        {
            OperationContext.ThrowIfCancellationRequested();
            return operation();
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            return new OperationResult(false, string.Empty, ex.Message);
        }
    }

    protected static OperationResult? ValidateOptionsNotNull<T>(T? options) where T : class
    {
        if (options is null)
        {
            return new OperationResult(false, string.Empty, "Options are required.");
        }

        return null;
    }

    /// <summary>
    /// Performs standard validation for the input file and output directory.
    /// </summary>
    protected static OperationResult? ValidateStandardInputs(PdfFile input, string outputDirectory)
    {
        if (input is null || string.IsNullOrWhiteSpace(input.FilePath))
        {
            return new OperationResult(false, string.Empty, "Input PDF path is required.");
        }

        if (!File.Exists(input.FilePath))
        {
            return new OperationResult(false, string.Empty, $"Input file not found: {input.FilePath}");
        }

        var extensionError = ValidatePdfExtension(input.FilePath);
        if (extensionError != null)
        {
            return extensionError;
        }

        return ValidateOutputDirectory(outputDirectory);
    }

    /// <summary>
    /// Performs standard validation for the input file (String) and output directory.
    /// </summary>
    protected static OperationResult? ValidateForeignInput(string inputFilePath, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(inputFilePath))
        {
            return new OperationResult(false, string.Empty, "Input file path is required.");
        }

        if (!File.Exists(inputFilePath))
        {
            return new OperationResult(false, string.Empty, $"Input file not found: {inputFilePath}");
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return new OperationResult(false, string.Empty, "Output directory is required.");
        }

        return null;
    }

    protected static OperationResult? ValidateOutputDirectory(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return new OperationResult(false, string.Empty, "Output directory is required.");
        }

        return null;
    }

    protected static OperationResult? ValidatePdfExtension(string filePath)
    {
        if (!Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return new OperationResult(false, string.Empty, "Input file must be a PDF.");
        }

        return null;
    }

    protected static OperationResult? ValidateInputFiles(IReadOnlyList<PdfFile> inputs)
    {
        if (inputs is null || inputs.Count == 0)
        {
            return new OperationResult(false, string.Empty, "At least one input PDF is required.");
        }

        foreach (var input in inputs)
        {
            if (input is null || string.IsNullOrWhiteSpace(input.FilePath))
            {
                return new OperationResult(false, string.Empty, "Input PDF path is required.");
            }

            if (!File.Exists(input.FilePath))
            {
                return new OperationResult(false, string.Empty, $"Input PDF not found: {input.FilePath}");
            }

            var extensionError = ValidatePdfExtension(input.FilePath);
            if (extensionError != null)
            {
                return extensionError;
            }
        }

        return null;
    }

    /// <summary>
    /// Creates the output directory and generates a standardized output file path.
    /// </summary>
    protected static string PrepareOutputEnvironment(string inputPath, string outputDirectory, string suffix)
    {
        Directory.CreateDirectory(outputDirectory);
        var originalFileName = Path.GetFileNameWithoutExtension(inputPath);
        return SafeOutputFiles.GetUniquePath(Path.Combine(outputDirectory, $"{originalFileName}_{suffix}.pdf"));
    }
}
