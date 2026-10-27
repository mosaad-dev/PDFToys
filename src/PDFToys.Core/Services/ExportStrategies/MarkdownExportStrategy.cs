using PDFToys.Core.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace PDFToys.Core.Services.ExportStrategies;

public sealed class MarkdownExportStrategy : ExportStrategyBase
{
    public override bool CanHandle(PdfExportFormat format) => format == PdfExportFormat.Markdown;

    public override OperationResult Execute(IReadOnlyList<PdfFile> inputs, ExportOptions options, string outputFolder) =>
        ExportInputs(inputs, outputFolder, (input, index, outputs) =>
        {
            OperationContext.ThrowIfCancellationRequested();
            using var document = PdfDocument.Open(input.FilePath);
            var pageIndex = 1;
            foreach (var page in document.GetPages())
            {
                OperationContext.ThrowIfCancellationRequested();
                OperationContext.Report(new("Extracting page text", input.FilePath, index, inputs.Count, pageIndex, document.NumberOfPages));
                var text = ContentOrderTextExtractor.GetText(page, addDoubleNewline: true);
                using var transaction = OutputFileTransaction.Create(Path.Combine(outputFolder, PageOutputName(inputs, index, pageIndex, "md")));
                File.WriteAllText(transaction.CandidatePath, string.IsNullOrEmpty(text) ? Environment.NewLine : text);
                outputs.Add(new(transaction.Commit()));
                pageIndex++;
            }
        });
}
