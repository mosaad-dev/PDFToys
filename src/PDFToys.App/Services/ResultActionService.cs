using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input.Platform;
using Avalonia.Controls.ApplicationLifetimes;

namespace PDFToys.App.Services;

public interface IResultActionService
{
    void Open(string path);
    void ShowInFolder(string path);
    Task CopyPathAsync(string path);
}

public sealed class ResultActionService : IResultActionService
{
    public void Open(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new IOException("This output is no longer available at the saved location.");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void ShowInFolder(string path)
    {
        if (Directory.Exists(path)) { Open(path); return; }
        if (!File.Exists(path)) throw new IOException("This output is no longer available at the saved location.");
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", path }, UseShellExecute = true });
    }

    public async Task CopyPathAsync(string path)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(path);
        else throw new InvalidOperationException("Clipboard is unavailable.");
    }
}
