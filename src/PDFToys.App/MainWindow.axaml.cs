using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using PDFToys.App.ViewModels;

namespace PDFToys.App;

public partial class MainWindow : Window
{
    private OperationViewModelBase? _closingOperation;
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        Closed += (_, _) => UnsubscribeClosingOperation();
    }
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is not MainViewModel { CurrentPage: OperationViewModelBase { IsBusy: true } operation }) return;
        e.Cancel = true;
        if (_closingOperation is null)
        {
            _closingOperation = operation;
            operation.PropertyChanged += OnOperationChanged;
        }
        operation.RequestCancellation();
        operation.StatusMessage = "Cancelling after the current step. The window will close when it is safe.";
    }
    private void OnOperationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationViewModelBase.IsBusy) && _closingOperation?.IsBusy == false)
        {
            UnsubscribeClosingOperation();
            Dispatcher.UIThread.Post(Close);
        }
    }
    private void UnsubscribeClosingOperation()
    {
        if (_closingOperation is null) return;
        _closingOperation.PropertyChanged -= OnOperationChanged;
        _closingOperation = null;
    }
}
