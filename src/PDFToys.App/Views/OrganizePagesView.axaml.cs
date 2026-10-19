using Avalonia;
using Avalonia.Controls;
using PDFToys.App.ViewModels;

namespace PDFToys.App.Views;

public partial class OrganizePagesView : UserControl
{
    public OrganizePagesView() { InitializeComponent(); }
    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is OrganizePagesViewModel vm && !string.IsNullOrWhiteSpace(vm.SelectedFilePath) && !vm.IsBusy)
            await vm.LoadInputAsync(vm.SelectedFilePath);
    }
}
