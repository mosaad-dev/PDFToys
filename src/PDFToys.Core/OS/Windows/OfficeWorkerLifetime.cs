using System.Diagnostics;
using PDFToys.Core.Models;

namespace PDFToys.Core.OS.Windows;

internal static class OfficeWorkerLifetime
{
    internal static bool WaitUntilExited(Process process, TimeSpan timeout, string currentInput)
    {
        var elapsed = Stopwatch.StartNew();
        var cancellingReported = false;
        while (!process.WaitForExit(100))
        {
            if (OperationContext.CancellationToken.IsCancellationRequested && !cancellingReported)
            {
                OperationContext.Report(new("Cancelling after the current Office step", currentInput));
                cancellingReported = true;
            }
            if (elapsed.Elapsed < timeout) continue;
            // The supplied process is the owned PDFToys worker. Never kill Office or a process tree.
            if (!process.HasExited) process.Kill(entireProcessTree: false);
            process.WaitForExit(5000);
            return false;
        }
        return true;
    }
}
