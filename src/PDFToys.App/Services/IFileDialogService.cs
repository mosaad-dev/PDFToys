using System.Collections.Generic;
using System.Threading.Tasks;

namespace PDFToys.App.Services;

public interface IFileDialogService
{
    Task<IReadOnlyList<string>> PickPdfFilesAsync();

    async Task<string?> PickPdfFileAsync()
    {
        var files = await PickPdfFilesAsync();
        return files.Count == 0 ? null : files[0];
    }

    Task<IReadOnlyList<string>> PickConvertibleFilesAsync(bool allowMultiple = true);

    Task<string?> ShowSaveFileDialogAsync(string title, string defaultFileName, string extension);
}
