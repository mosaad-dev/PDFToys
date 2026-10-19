using PDFToys.Core.Contracts;
using PDFToys.Core.Models;

namespace PDFToys.Core.Services;

/// <summary>The single conversion/export orchestration path used by the desktop and CLI.</summary>
public sealed class ConversionWorkflowService(IConversionService conversion, IMergeService merge, IExportService export)
{
    public OperationResult Execute(ConversionJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.Inputs.Count == 0) return new(false, "", "At least one input is required.");
        if (job.Kind == ConversionJobKind.CombineToPdf) return Combine(job);
        var files = new List<FileOperationResult>();
        var inputs = job.Kind == ConversionJobKind.ConvertToPdf ? job.Inputs.Take(1) : job.Inputs;
        foreach (var input in inputs)
        {
            if (OperationContext.CancellationToken.IsCancellationRequested) return Aggregate(files, true);
            OperationContext.Report(new("Processing file", input, files.Count, job.Inputs.Count));
            OperationResult result;
            try
            {
                var destination = ResolveOutputDirectory(input, job.OutputDirectory);
                result = job.Kind == ConversionJobKind.Export
                    ? export.Export([new PdfFile(input)], new ExportOptions(destination, job.Format))
                    : conversion.Convert(input, new ConversionOptions(destination));
            }
            catch (OperationCanceledException) { return Aggregate(files, true); }
            catch (Exception ex) { result = new(false, "", ex.Message) { ErrorCode = "OperationFailed" }; }
            files.Add(new(input, result));
            if (result.Outcome == OperationOutcome.Cancelled) return Aggregate(files, true);
            OperationContext.Report(new("File complete", input, files.Count, job.Inputs.Count));
        }
        return Aggregate(files, false);
    }

    private OperationResult Combine(ConversionJob job)
    {
        var temp = Path.Combine(Path.GetTempPath(), "pdftoys-combine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var files = new List<FileOperationResult>();
        var converted = new List<PdfFile>();
        try
        {
            for (var index = 0; index < job.Inputs.Count; index++)
            {
                if (OperationContext.CancellationToken.IsCancellationRequested) return Aggregate(files, true);
                var input = job.Inputs[index];
                OperationContext.Report(new("Preparing document to combine", input, index, job.Inputs.Count));
                // Every source owns a distinct intermediate directory, even identical basenames or repeated selections.
                var sourceDirectory = Path.Combine(temp, index.ToString("D6"));
                Directory.CreateDirectory(sourceDirectory);
                var result = Path.GetExtension(input).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                    ? new OperationResult(true, input, "")
                    : conversion.Convert(input, new ConversionOptions(sourceDirectory));
                if (!result.IsSuccess)
                {
                    files.Add(new(input, result));
                    return Aggregate(files, result.Outcome == OperationOutcome.Cancelled);
                }
                converted.Add(new(result.OutputPath));
                // Intermediate files are deliberately absent from the public output manifest.
                files.Add(new(input, new OperationResult(true, "", "")));
            }
            OperationContext.ThrowIfCancellationRequested();
            var merged = merge.Merge(converted, new MergeOptions(ResolveOutputDirectory(job.Inputs[0], job.OutputDirectory), "combined.pdf"));
            return merged with { Files = files.ToArray() };
        }
        catch (OperationCanceledException) { return Aggregate(files, true); }
        catch (Exception ex) { return new(false, "", ex.Message) { Files = files.ToArray(), ErrorCode = "CombineFailed" }; }
        finally
        {
            try { Directory.Delete(temp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static OperationResult Aggregate(IReadOnlyList<FileOperationResult> files, bool cancelled)
    {
        var outputs = files.SelectMany(file => file.Result.Outputs).ToArray();
        var failed = files.Where(file => !file.Result.IsSuccess).ToArray();
        var outcome = cancelled ? OperationOutcome.Cancelled
            : failed.Length == 0 ? OperationOutcome.Succeeded
            : outputs.Length > 0 ? OperationOutcome.PartialSuccess : OperationOutcome.Failed;
        var message = cancelled ? "Operation cancelled. Completed outputs have been kept."
            : string.Join(Environment.NewLine, failed.Select(file => $"{Path.GetFileName(file.InputPath)}: {file.Result.ErrorMessage}"));
        return new(outcome == OperationOutcome.Succeeded, outputs.FirstOrDefault()?.Path ?? "", message)
        {
            Outcome = outcome,
            Outputs = outputs,
            Files = files.ToArray(),
            Warnings = files.SelectMany(file => file.Result.Warnings).ToArray(),
            ErrorCode = cancelled ? "Cancelled" : failed.Length > 0 ? "BatchFailed" : ""
        };
    }

    private static string ResolveOutputDirectory(string input, string? selected) => !string.IsNullOrWhiteSpace(selected)
        ? selected : Path.GetDirectoryName(Path.GetFullPath(input)) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
}
