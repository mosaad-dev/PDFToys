using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PDFToys.Core.Contracts;
using PDFToys.Core.Models;
using System.Text;

namespace PDFToys.Core.Services;

public sealed class MergeService : ServiceBase, IMergeService
{
    static MergeService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Merges multiple PDF files into a single output document.
    /// </summary>
    /// <param name="inputs">The PDF files to merge.</param>
    /// <param name="options">Configuration for the output destination.</param>
    /// <returns>An OperationResult indicating success or failure.</returns>
    public OperationResult Merge(IReadOnlyList<PdfFile> inputs, MergeOptions options)
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

            if (string.IsNullOrWhiteSpace(options.OutputFileName))
            {
                return new OperationResult(false, string.Empty, "Output file name is required.");
            }

            if (Path.GetFileName(options.OutputFileName) != options.OutputFileName || !options.OutputFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                return new(false, "", "Output file name must be a PDF file name without a directory.");

            Directory.CreateDirectory(options.OutputDirectory);

            using var outputDocument = new PdfDocument();

            var completed = 0;
            foreach (var file in inputs!)
            {
                OperationContext.ThrowIfCancellationRequested();
                OperationContext.Report(new("Merging document", file.FilePath, completed, inputs.Count));
                using var inputDocument = PdfReader.Open(file.FilePath, PdfDocumentOpenMode.Import);
                var compatibility = PdfDocumentSafety.CheckPageCopy(inputDocument);
                if (compatibility is not null) return compatibility;

                foreach (var page in inputDocument.Pages)
                {
                    OperationContext.ThrowIfCancellationRequested();
                    outputDocument.AddPage(page);
                }
                completed++;
            }

            using var transaction = OutputFileTransaction.Create(Path.Combine(options.OutputDirectory, options.OutputFileName), inputs[0].FilePath);
            outputDocument.Save(transaction.CandidatePath);
            var outputPath = transaction.CommitPdf();

            return new OperationResult(true, outputPath, string.Empty);
        });
    }
}
