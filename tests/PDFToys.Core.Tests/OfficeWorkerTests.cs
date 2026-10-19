using System.Diagnostics;
using System.Text.Json;
using PdfSharp.Pdf.IO;
using PDFToys.Core.Models;
using PDFToys.Core.OS.Windows;

namespace PDFToys.Core.Tests;

public sealed class OfficeWorkerTests : PdfTestBase
{
    [WordWorkerFact]
    [Trait("Category", "OfficeIntegration")]
    public void Word_RealIsolatedWorker_ConvertsAndPreservesInput() => ConvertFixture("docx");

    [ExcelWorkerFact]
    [Trait("Category", "OfficeIntegration")]
    public void Excel_RealIsolatedWorker_ConvertsAndPreservesInput() => ConvertFixture("xlsx");

    [PowerPointWorkerFact]
    [Trait("Category", "OfficeIntegration")]
    public void PowerPoint_RealIsolatedWorker_ConvertsAndPreservesInput() => ConvertFixture("pptx");

    [Fact]
    public void Worker_InvalidArguments_FailsWithoutStartingOffice()
    {
        Assert.True(OfficeWorkerHost.TryRun([OfficeWorkerHost.Switch], out var exit));
        Assert.Equal(2, exit);
        Assert.False(OfficeWorkerHost.TryRun([], out exit));
    }

    [Fact]
    public void Converter_UnsupportedFormat_ReturnsExplicitFailure()
    {
        var result = new OfficeComPdfConverter().Execute("source.unknown", new(TempDirectory), Path.Combine(TempDirectory, "output.pdf"));
        Assert.False(result.IsSuccess);
        Assert.Contains("Unsupported Office format", result.ErrorMessage);
    }

    [OfficeUnavailableFact]
    public void Converter_MissingPowerPoint_ReportsUnavailableWithoutStartingAWorker()
    {
        var result = new OfficeComPdfConverter().Execute("source.pptx", new(TempDirectory), Path.Combine(TempDirectory, "output.pdf"));
        Assert.False(result.IsSuccess);
        Assert.Equal("OfficeUnavailable", result.ErrorCode);
        Assert.Contains("PowerPoint", result.ErrorMessage);
    }

    [WindowsWorkerFact]
    public void WorkerTimeout_TerminatesOnlyOwnedWorker_LeavesOtherProcessRunning()
    {
        using var worker = StartSleepingWorker(30000);
        using var other = StartSleepingWorker(30000);
        try
        {
            Assert.False(OfficeWorkerLifetime.WaitUntilExited(worker, TimeSpan.FromMilliseconds(120), "fixture.docx"));
            Assert.True(worker.HasExited);
            Assert.False(other.HasExited);
        }
        finally { if (!worker.HasExited) worker.Kill(false); if (!other.HasExited) other.Kill(false); }
    }

    [WindowsWorkerFact]
    public void WorkerCancellation_ReportsSafeBoundary_AndWaitsForCurrentStep()
    {
        using var worker = StartSleepingWorker(600);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var stages = new List<string>();
        using var scope = OperationContext.Begin(cancellation.Token, new ImmediateProgress(value => stages.Add(value.Stage)));
        var timer = Stopwatch.StartNew();
        try
        {
            Assert.True(OfficeWorkerLifetime.WaitUntilExited(worker, TimeSpan.FromSeconds(10), "fixture.docx"));
            Assert.True(timer.ElapsedMilliseconds >= 300);
            Assert.Contains("Cancelling after the current Office step", stages);
        }
        finally { if (!worker.HasExited) worker.Kill(false); }
    }

    private static Process StartSleepingWorker(int milliseconds)
    {
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add($"[Threading.Thread]::Sleep({milliseconds})");
        return Process.Start(start)!;
    }

    private sealed class ImmediateProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }

    private void ConvertFixture(string extension)
    {
        var input = Path.Combine(TempDirectory, "source." + extension);
        File.Copy(Path.Combine(WorkerTestAvailability.RepositoryRoot!, "tests", "PDFToys.Core.Tests", "Fixtures", "Office", "sample." + extension), input);
        var original = File.ReadAllBytes(input);
        var start = new ProcessStartInfo(WorkerTestAvailability.AppHost!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--command"); start.ArgumentList.Add("to-pdf"); start.ArgumentList.Add("--input"); start.ArgumentList.Add(input);
        using var process = Process.Start(start)!;
        Assert.True(process.WaitForExit(150000), "Office worker did not honor its bounded lifetime.");
        Assert.Equal(0, process.ExitCode);
        var output = Assert.Single(Directory.GetFiles(TempDirectory, "*Converted*.pdf"));
        using var document = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.True(document.PageCount > 0);
        Assert.Equal(original, File.ReadAllBytes(input));
        Assert.Empty(Directory.GetFiles(TempDirectory, ".pdftoys-*"));
    }
}

public static class WorkerTestAvailability
{
    public static string? RepositoryRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "PDFToys.sln"))) return directory.FullName;
            return null;
        }
    }
    public static string? AppHost
    {
        get
        {
            if (RepositoryRoot is null) return null;
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            var path = Path.Combine(RepositoryRoot, "src", "PDFToys.App", "bin", configuration, "net10.0", "PDFToys.App.exe");
            return File.Exists(path) ? path : null;
        }
    }
    public static string? SkipReason(string progId, string processName)
    {
        if (!OperatingSystem.IsWindows()) return "Office integration requires Windows.";
        if (Type.GetTypeFromProgID(progId, false) is null) return $"{progId} is not installed; no Office conversion was exercised.";
        if (AppHost is null) return "Build PDFToys.App first; isolated Office integration needs its apphost.";
        var processes = Process.GetProcessesByName(processName);
        try { return processes.Length > 0 ? $"{processName} is already open; user Office sessions are excluded from automation." : null; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}

public sealed class WordWorkerFactAttribute : FactAttribute
{
    public WordWorkerFactAttribute() => Skip = WorkerTestAvailability.SkipReason("Word.Application", "WINWORD");
}
public sealed class ExcelWorkerFactAttribute : FactAttribute
{
    public ExcelWorkerFactAttribute() => Skip = WorkerTestAvailability.SkipReason("Excel.Application", "EXCEL");
}
public sealed class PowerPointWorkerFactAttribute : FactAttribute
{
    public PowerPointWorkerFactAttribute() => Skip = WorkerTestAvailability.SkipReason("PowerPoint.Application", "POWERPNT");
}

public sealed class OfficeUnavailableFactAttribute : FactAttribute
{
    public OfficeUnavailableFactAttribute()
    {
        if (OperatingSystem.IsWindows() && Type.GetTypeFromProgID("PowerPoint.Application", false) is not null)
            Skip = "PowerPoint is installed; missing-Office behavior needs a host without it. Available-Office worker cases run separately.";
    }
}

public sealed class WindowsWorkerFactAttribute : FactAttribute
{
    public WindowsWorkerFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Worker process integration requires Windows."; }
}
