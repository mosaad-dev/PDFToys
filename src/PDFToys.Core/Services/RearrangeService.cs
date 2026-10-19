using PdfSharp;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PDFToys.Core.Contracts;
using PDFToys.Core.Models;

namespace PDFToys.Core.Services;

public sealed class RearrangeService : ServiceBase, IRearrangeService
{
    public int? TryGetPageCount(PdfFile input)
    {
        if (input is null || string.IsNullOrWhiteSpace(input.FilePath) || !File.Exists(input.FilePath))
        {
            return null;
        }

        if (!Path.GetExtension(input.FilePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using var document = PdfReader.Open(input.FilePath, PdfDocumentOpenMode.Import);
            return document.PageCount;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a new PDF from the requested page order and optional per-page rotations.
    /// </summary>
    /// <param name="input">The source PDF file.</param>
    /// <param name="pages">Ordered list of source pages using 0-based indices.</param>
    /// <param name="options">The destination options for the rearranged PDF.</param>
    /// <returns>An OperationResult containing the path to the rearranged file.</returns>
    public OperationResult Rearrange(PdfFile input, IReadOnlyList<PageArrangementItem> pages, RearrangeOptions options)
    {
        return ExecuteSafe(() =>
        {
            var optionsError = ValidateOptionsNotNull(options);
            if (optionsError != null)
            {
                return optionsError;
            }

            var validationError = ValidateStandardInputs(input, options!.OutputDirectory);
            if (validationError != null)
            {
                return validationError;
            }

            if (pages is null || pages.Count == 0)
            {
                return new OperationResult(false, string.Empty, "At least one page must be included.");
            }

            if (options.OutputFileName is not null && (Path.GetFileName(options.OutputFileName) != options.OutputFileName || !options.OutputFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
                return new(false, "", "Output file name must be a PDF file name without a directory.");
            using var transaction = options.OutputFileName is null
                ? OutputFileTransaction.CreatePdf(input.FilePath, options.OutputDirectory, "Rearranged")
                : OutputFileTransaction.Create(Path.Combine(options.OutputDirectory, options.OutputFileName), input.FilePath);

            using var inputDocument = PdfReader.Open(input.FilePath, PdfDocumentOpenMode.Import);
            var compatibility = PdfDocumentSafety.CheckPageCopy(inputDocument);
            if (compatibility is not null) return compatibility;
            using var outputDocument = new PdfDocument();

            foreach (var pageItem in pages)
            {
                OperationContext.ThrowIfCancellationRequested();
                OperationContext.Report(new("Organizing pages", input.FilePath, 0, 1, outputDocument.PageCount, pages.Count));
                if (pageItem.SourcePageIndex < 0 || pageItem.SourcePageIndex >= inputDocument.PageCount)
                {
                    return new OperationResult(
                        false,
                        string.Empty,
                        $"Page index out of range: {pageItem.SourcePageIndex} (0-based).");
                }

                if (pageItem.RotationDegrees % 90 != 0)
                    return new OperationResult(false, string.Empty, "Page rotation must be a multiple of 90 degrees.");

                var sourcePage = inputDocument.Pages[pageItem.SourcePageIndex];
                var rotation = GetEffectiveRotation(sourcePage);
                var page = outputDocument.AddPage(sourcePage);
                page.Rotation = ToPageRotation(rotation + pageItem.RotationDegrees);
            }

            outputDocument.Save(transaction.CandidatePath);
            var outputPath = transaction.CommitPdf();

            return new OperationResult(true, Path.GetFullPath(outputPath), string.Empty);
        });
    }

    private static PageRotation ToPageRotation(int rotationDegrees)
    {
        var normalized = ((rotationDegrees % 360) + 360) % 360;
        return normalized switch
        {
            90 => PageRotation.Rotate90DegreesRight,
            180 => PageRotation.RotateUpsideDown,
            270 => PageRotation.Rotate90DegreesLeft,
            _ => PageRotation.None
        };
    }

    private static int GetEffectiveRotation(PdfDictionary page)
    {
        var visited = new HashSet<PdfDictionary>();
        for (PdfDictionary? node = page; node is not null && visited.Add(node);)
        {
            if (node.Elements.ContainsKey("/Rotate")) return node.Elements.GetInteger("/Rotate");
            node = node.Elements.GetDictionary("/Parent");
        }
        return 0;
    }
}
