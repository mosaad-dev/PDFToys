using System.Diagnostics;
using PDFToys.Core.Contracts;
using PDFToys.Core.Models;

namespace PDFToys.Core.OS.Windows;

/// <summary>Runs Office in a disposable STA child process, with a bounded wait.</summary>
public sealed class OfficeComPdfConverter : IConversionStrategy
{
    private readonly TimeSpan _timeout;
    public OfficeComPdfConverter() : this(TimeSpan.FromMinutes(2)) { }
    public OfficeComPdfConverter(TimeSpan timeout) => _timeout = timeout > TimeSpan.Zero ? timeout : throw new ArgumentOutOfRangeException(nameof(timeout));

    public bool CanHandle(string extension) => extension.ToLowerInvariant() is ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx";

    public OperationResult Execute(string inputFilePath, ConversionOptions options, string outputFilePath)
    {
        var extension = Path.GetExtension(inputFilePath).ToLowerInvariant();
        var (progId, name) = extension switch
        {
            ".doc" or ".docx" => ("Word.Application", "Microsoft Word"),
            ".xls" or ".xlsx" => ("Excel.Application", "Microsoft Excel"),
            ".ppt" or ".pptx" => ("PowerPoint.Application", "Microsoft PowerPoint"),
            _ => ("", "Office")
        };
        if (progId.Length == 0) return new(false, string.Empty, "Unsupported Office format.");
        if (!OperatingSystem.IsWindows() || Type.GetTypeFromProgID(progId, false) is null)
            return new(false, string.Empty, $"{name} is not installed or cannot be started.") { ErrorCode = "OfficeUnavailable" };

        var host = OfficeWorkerHost.ResolveHost();
        if (host is null) return new(false, string.Empty, "Office conversion requires the PDFToys application host. Launch PDFToys to convert Office documents.") { ErrorCode = "OfficeWorkerUnavailable" };
        var workDirectory = Path.Combine(Path.GetTempPath(), "pdftoys-office-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);
        var requestPath = Path.Combine(workDirectory, "request.json");
        var resultPath = Path.Combine(workDirectory, "result.json");
        try
        {
            OfficeWorkerHost.WriteRequest(requestPath, new OfficeWorkerRequest(Path.GetFullPath(inputFilePath), Path.GetFullPath(outputFilePath)));
            var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            start.ArgumentList.Add(OfficeWorkerHost.Switch);
            start.ArgumentList.Add(requestPath);
            start.ArgumentList.Add(resultPath);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the Office worker.");
            if (!OfficeWorkerLifetime.WaitUntilExited(process, _timeout, inputFilePath))
                return new(false, string.Empty, $"{name} conversion timed out. The isolated worker was stopped; check Office for an unanswered prompt before retrying.") { ErrorCode = "OfficeTimeout" };
            OperationContext.ThrowIfCancellationRequested();
            return OfficeWorkerHost.ReadResult(resultPath);
        }
        finally
        {
            try { Directory.Delete(workDirectory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

