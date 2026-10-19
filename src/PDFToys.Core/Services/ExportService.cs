using PDFToys.Core.Contracts;
using PDFToys.Core.Models;

namespace PDFToys.Core.Services;

public sealed class ExportService(IEnumerable<IExportStrategy> strategies) : ServiceBase, IExportService
{
    private readonly IEnumerable<IExportStrategy> _strategies = strategies;

    public OperationResult Export(IReadOnlyList<PdfFile> inputs, ExportOptions options)
    {
        return ExecuteSafe(() =>
        {
            var optionsError = ValidateOptionsNotNull(options);
            if (optionsError != null)
            {
                return optionsError;
            }

            var inputValidation = ValidateInputFiles(inputs);
            if (inputValidation != null)
            {
                return inputValidation;
            }

            var outputDirValidation = ValidateOutputDirectory(options.OutputDirectory);
            if (outputDirValidation != null)
            {
                return outputDirValidation;
            }

            var outputFolder = inputs!.Count == 1
                ? BuildSingleOutputFolderPath(inputs[0].FilePath, options)
                : BuildBatchOutputFolderPath(options);

            var strategy = _strategies.FirstOrDefault(s => s.CanHandle(options.Format));
            if (strategy == null)
            {
                return new OperationResult(false, string.Empty, $"Unsupported export format: {options.Format}");
            }

            OperationContext.ThrowIfCancellationRequested();
            // A new job owns a new directory. Previous exports are never mixed with this run.
            outputFolder += "_" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(outputFolder);
            return strategy.Execute(inputs, options, outputFolder);
        });
    }

    private static string BuildSingleOutputFolderPath(string inputPdfPath, ExportOptions options)
    {
        var sourceName = Path.GetFileNameWithoutExtension(inputPdfPath);
        var suffix = options.Format.ToString().ToLowerInvariant();
        return Path.Combine(options.OutputDirectory, $"{sourceName}_{suffix}");
    }

    private static string BuildBatchOutputFolderPath(ExportOptions options)
    {
        var suffix = options.Format.ToString().ToLowerInvariant();
        return Path.Combine(options.OutputDirectory, $"pdf_export_{suffix}_{DateTime.UtcNow:yyyyMMdd_HHmmss}");
    }
}
