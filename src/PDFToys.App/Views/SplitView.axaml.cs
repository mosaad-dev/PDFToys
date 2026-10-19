using Avalonia;
using Avalonia.Controls;
using PDFToys.App.ViewModels;

namespace PDFToys.App.Views;

public partial class SplitView : UserControl
{
    public SplitView() { InitializeComponent(); }
    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is SplitViewModel vm && !string.IsNullOrWhiteSpace(vm.SelectedFilePath) && !vm.IsBusy)
            await vm.LoadInputAsync(vm.SelectedFilePath);
    }
}
