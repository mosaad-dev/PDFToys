using System.Reflection;
using System.Text.Json;
using PDFToys.Core.Models;

namespace PDFToys.Core.OS.Windows;

public sealed record OfficeWorkerRequest(string InputPath, string OutputPath);

/// <summary>Internal application entry point. No GUI initialization is required.</summary>
public static class OfficeWorkerHost
{
    public const string Switch = "--pdftoys-office-worker";

    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || args[0] != Switch) return false;
        if (args.Length != 3) { exitCode = 2; return true; }
        OperationResult result;
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Office conversion requires Windows.");
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException("Office worker requires an STA entry point.");
            var request = JsonSerializer.Deserialize<OfficeWorkerRequest>(File.ReadAllText(args[1]))
                ?? throw new InvalidDataException("Invalid Office worker request.");
            result = new OfficeComAutomation().Execute(request.InputPath,
                new ConversionOptions(Path.GetDirectoryName(request.OutputPath)!), request.OutputPath);
        }
        catch (Exception ex)
        {
            result = new(false, string.Empty, ex.Message) { ErrorCode = "OfficeConversionFailed" };
        }
        try { File.WriteAllText(args[2], JsonSerializer.Serialize(result)); }
        catch { exitCode = 3; return true; }
        exitCode = result.IsSuccess ? 0 : 1;
        return true;
    }

    internal static void WriteRequest(string path, OfficeWorkerRequest request) => File.WriteAllText(path, JsonSerializer.Serialize(request));
    internal static OperationResult ReadResult(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<OperationResult>(File.ReadAllText(path)) ?? new(false, "", "Invalid Office worker response.")
        : new(false, "", "Office worker exited without a response.") { ErrorCode = "OfficeWorkerFailed" };

    internal static string? ResolveHost()
    {
        var processPath = Environment.ProcessPath;
        if (processPath is not null && Path.GetFileNameWithoutExtension(processPath).Equals("PDFToys.App", StringComparison.OrdinalIgnoreCase)) return processPath;
        var appHost = Path.Combine(AppContext.BaseDirectory, "PDFToys.App.exe");
        return File.Exists(appHost) ? appHost : null;
    }
}
