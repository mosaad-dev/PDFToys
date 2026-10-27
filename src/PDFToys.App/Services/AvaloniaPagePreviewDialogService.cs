using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using PDFToys.App.Models;
using PDFToys.App.ViewModels;
using PDFToys.App.Views;

namespace PDFToys.App.Services;

public sealed class AvaloniaPagePreviewDialogService : IPagePreviewDialogService
{
    public async Task ShowAsync(PagePreviewRequest request)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is null)
        {
            return;
        }

        var window = new PagePreviewWindow();
        using var viewModel = new PagePreviewViewModel(
            request.Title,
            request.DetailsText,
            request.ImagePngBytes,
            request.ErrorMessage,
            () => window.Close());

        window.DataContext = viewModel;
        await window.ShowDialog(desktop.MainWindow);
    }
}
