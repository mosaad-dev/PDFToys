using PDFToys.App.Models;
using PDFToys.Core.Contracts;
using PDFToys.Core.Models;
using PDFToys.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PDFToys.App.Services;

public sealed record HeadlessExecutionResult(
    int ExitCode,
    bool IsSuccess,
    IReadOnlyList<string> Outputs,
    IReadOnlyList<string> Errors,
    string LogPath);

public sealed class HeadlessConversionExecutor
{
    private readonly ConversionWorkflowService _workflow;

    public HeadlessConversionExecutor(
        IConversionService conversionService,
        IMergeService mergeService,
        IExportService exportService)
    {
        _workflow = new ConversionWorkflowService(conversionService, mergeService, exportService);
    }

    public HeadlessExecutionResult Execute(StartupRoute route)
    {
        var outputs = new List<string>();
        var errors = new List<string>();

        try
        {
            if (route.Kind != StartupRouteKind.HeadlessConversion)
            {
                errors.Add("Route is not configured for headless conversion.");
                var invalidLog = WriteLog(route, outputs, errors, 2);
                return new HeadlessExecutionResult(2, false, outputs, errors, invalidLog);
            }

            if (route.Inputs.Count == 0)
            {
                errors.Add("No input files were provided.");
                var invalidLog = WriteLog(route, outputs, errors, 2);
                return new HeadlessExecutionResult(2, false, outputs, errors, invalidLog);
            }

            var exitCode = ExecuteByOperation(route, outputs, errors);
            var logPath = WriteLog(route, outputs, errors, exitCode);
            return new HeadlessExecutionResult(exitCode, exitCode == 0, outputs, errors, logPath);
        }
        catch (Exception ex)
        {
            errors.Add($"Unhandled failure: {ex}");
            var crashLog = WriteLog(route, outputs, errors, 3);
            return new HeadlessExecutionResult(3, false, outputs, errors, crashLog);
        }
    }

    private int ExecuteByOperation(StartupRoute route, List<string> outputs, List<string> errors)
    {
        ConversionJob? job = route.Operation switch
        {
            PdfToysOperation.ConvertToPdf => new(ConversionJobKind.ConvertToPdf, route.Inputs),
            PdfToysOperation.CombineToPdf => new(ConversionJobKind.CombineToPdf, route.Inputs),
            PdfToysOperation.ConvertEachToPdf => new(ConversionJobKind.ConvertEachToPdf, route.Inputs),
            PdfToysOperation.PdfToJpg => new(ConversionJobKind.Export, route.Inputs, format: PdfExportFormat.Jpg),
            PdfToysOperation.PdfToJpeg => new(ConversionJobKind.Export, route.Inputs, format: PdfExportFormat.Jpeg),
            PdfToysOperation.PdfToPng => new(ConversionJobKind.Export, route.Inputs, format: PdfExportFormat.Png),
            PdfToysOperation.PdfToMarkdown => new(ConversionJobKind.Export, route.Inputs, format: PdfExportFormat.Markdown),
            _ => null
        };
        if (job is null)
        {
            errors.Add(route.Operation == PdfToysOperation.ConvertFromPdf
                ? "convert-from-pdf requires a supported --target (jpg, jpeg, png, markdown, or md)."
                : "Unsupported conversion operation.");
            return 2;
        }
        var result = _workflow.Execute(job);
        outputs.AddRange(result.Outputs.Select(output => output.Path));
        if (!result.IsSuccess) errors.Add(result.ErrorMessage);
        return result.IsSuccess ? 0 : 1;
    }
    private static string WriteLog(StartupRoute route, IReadOnlyList<string> outputs, IReadOnlyList<string> errors, int exitCode)
    {
        // Headless logs record paths and error messages only — never passwords or secrets.
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PDFToys",
            "logs");
        Directory.CreateDirectory(logDirectory);

        var logPath = Path.Combine(logDirectory, $"headless_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.log");
        var builder = new StringBuilder();
        builder.AppendLine($"timestamp_utc={DateTime.UtcNow:O}");
        builder.AppendLine($"operation={route.Operation}");
        builder.AppendLine($"route_kind={route.Kind}");
        if (!string.IsNullOrWhiteSpace(route.TargetFormat))
        {
            builder.AppendLine($"target_format={route.TargetFormat}");
        }
        builder.AppendLine($"exit_code={exitCode}");
        builder.AppendLine("inputs:");
        foreach (var input in route.Inputs)
        {
            builder.AppendLine($"- {input}");
        }

        builder.AppendLine("outputs:");
        foreach (var output in outputs)
        {
            builder.AppendLine($"- {output}");
        }

        builder.AppendLine("errors:");
        foreach (var error in errors)
        {
            builder.AppendLine($"- {error}");
        }

        File.WriteAllText(logPath, builder.ToString());
        return logPath;
    }

}

