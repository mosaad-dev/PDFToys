using PDFToys.Core.Contracts;
using PDFToys.Core.Models;

namespace PDFToys.Core.Services.ExportStrategies;

public abstract class ExportStrategyBase : IExportStrategy
{
    public abstract bool CanHandle(PdfExportFormat format);
    public abstract OperationResult Execute(IReadOnlyList<PdfFile> inputs, ExportOptions options, string outputFolder);

    protected static OperationResult ExportInputs(IReadOnlyList<PdfFile> inputs, string outputFolder,
        Action<PdfFile, int, List<OperationOutput>> export)
    {
        var files = new List<FileOperationResult>();
        var allOutputs = new List<OperationOutput>();
        var cancelled = false;
        for (var index = 0; index < inputs.Count; index++)
        {
            if (OperationContext.CancellationToken.IsCancellationRequested) { cancelled = true; break; }
            var outputs = new List<OperationOutput>();
            OperationResult result;
            try
            {
                export(inputs[index], index, outputs);
                result = outputs.Count > 0 ? new(true, outputFolder, "") : new(false, "", "No pages were exported from the PDF.");
            }
            catch (OperationCanceledException) { result = OperationResult.Cancelled(); cancelled = true; }
            catch (Exception ex) { result = new(false, "", ex.Message) { ErrorCode = "ExportFailed" }; }
            result = result with { Outputs = outputs.ToArray() };
            if (!result.IsSuccess && outputs.Count > 0 && !cancelled) result = result with { Outcome = OperationOutcome.PartialSuccess };
            files.Add(new(inputs[index].FilePath, result));
            allOutputs.AddRange(outputs);
            if (cancelled) break;
        }
        if (cancelled && files.Count < inputs.Count)
            files.AddRange(inputs.Skip(files.Count).Select(input => new FileOperationResult(input.FilePath,
                OperationResult.Cancelled("Cancelled before this input was processed."))));
        var failures = files.Where(file => !file.Result.IsSuccess).ToArray();
        var success = !cancelled && failures.Length == 0 && allOutputs.Count > 0;
        return new(success, allOutputs.Count > 0 ? outputFolder : "",
            cancelled ? "Export cancelled. Completed pages have been kept." : string.Join(Environment.NewLine, failures.Select(file => file.Result.ErrorMessage)))
        {
            Outcome = cancelled ? OperationOutcome.Cancelled : success ? OperationOutcome.Succeeded : allOutputs.Count > 0 ? OperationOutcome.PartialSuccess : OperationOutcome.Failed,
            Outputs = allOutputs.ToArray(),
            Files = files.ToArray(),
            ErrorCode = cancelled ? "Cancelled" : failures.Length > 0 ? "ExportFailed" : ""
        };
    }

    protected static string PageOutputName(IReadOnlyList<PdfFile> inputs, int inputIndex, int page, string extension) =>
        inputs.Count > 1 ? $"{inputIndex + 1:000}_{SanitizeSourceName(inputs[inputIndex].FilePath)}_page_{page:000}.{extension}" : $"page_{page:000}.{extension}";

    protected static string SanitizeSourceName(string inputPath)
    {
        var sourceName = Path.GetFileNameWithoutExtension(inputPath);
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return "document";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new char[sourceName.Length];
        for (var i = 0; i < sourceName.Length; i++)
        {
            var ch = sourceName[i];
            sanitized[i] = invalidChars.Contains(ch) ? '_' : ch;
        }

        return new string(sanitized);
    }
}
