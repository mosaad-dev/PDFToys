using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PDFToys.App.Services;

public sealed class AvaloniaFileDialogService : IFileDialogService
{
    public async Task<IReadOnlyList<string>> PickConvertibleFilesAsync(bool allowMultiple = true)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow?.StorageProvider is null)
        {
            return [];
        }

        var files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = allowMultiple,
            Title = "Select files to convert",
            FileTypeFilter =
            [
                new FilePickerFileType("Supported files")
                {
                    Patterns =
                    [
                        "*.doc", "*.docx", "*.xls", "*.xlsx", "*.ppt", "*.pptx",
                        "*.jpg", "*.jpeg", "*.png", "*.txt", "*.md", "*.svg"
                    ]
                }
            ]
        });

        return files
            .Select(file => file.Path.LocalPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    public async Task<string?> PickPdfFileAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow?.StorageProvider is null)
        {
            return null;
        }

        var files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select a PDF",
            FileTypeFilter = [new FilePickerFileType("PDF files") { Patterns = ["*.pdf"] }]
        });
        return files.FirstOrDefault()?.Path.LocalPath;
    }

    public async Task<IReadOnlyList<string>> PickPdfFilesAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow?.StorageProvider is null)
        {
            return [];
        }

        var files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = "Select PDF files",
            FileTypeFilter =
            [
                new FilePickerFileType("PDF files")
                {
                    Patterns = ["*.pdf"]
                }
            ]
        });

        return files
            .Select(file => file.Path.LocalPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    public async Task<string?> ShowSaveFileDialogAsync(string title, string defaultFileName, string extension)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow?.StorageProvider is null)
        {
            return null;
        }

        var fileTypeName = extension.Equals(".pdf", System.StringComparison.OrdinalIgnoreCase)
            ? "PDF files"
            : $"{extension.TrimStart('.').ToUpperInvariant()} files";

        var saveFile = await desktop.MainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = defaultFileName,
            DefaultExtension = extension,
            FileTypeChoices =
            [
                new FilePickerFileType(fileTypeName)
                {
                    Patterns = [$"*{extension}"]
                }
            ]
        });

        var localPath = saveFile?.Path.LocalPath;
        return string.IsNullOrWhiteSpace(localPath) ? null : localPath;
    }
}
