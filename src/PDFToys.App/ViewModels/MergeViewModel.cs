using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PDFToys.App.Services;
using PDFToys.Core.Contracts;
using PDFToys.Core.Models;

namespace PDFToys.App.ViewModels;

public sealed class MergeItem : ViewModelBase
{
    private string _fullPath;
    private int _order;

    public MergeItem(string fullPath, int order = 0)
    {
        _fullPath = fullPath;
        _order = order;
    }

    public string FullPath
    {
        get => _fullPath;
        set
        {
            if (_fullPath == value)
            {
                return;
            }

            _fullPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FileName));
        }
    }

    public string FileName => System.IO.Path.GetFileName(FullPath);

    public int Order
    {
        get => _order;
        set
        {
            if (_order == value)
            {
                return;
            }

            _order = value;
            OnPropertyChanged();
        }
    }
}

public sealed class MergeViewModel : OperationViewModelBase
{
    private readonly IMergeService _service;
    private readonly IFileDialogService _dialogs;
    private MergeItem? _selectedItem;
    public MergeViewModel(IMergeService mergeService, IFileDialogService dialogService, Action goBackAction, IApplicationJobRunner? runner = null) : base(goBackAction, runner)
    {
        _service = mergeService; _dialogs = dialogService;
        AddFilesCommand = IdleAsyncCommand(AddFilesAsync);
        ExecuteMergeCommand = IdleAsyncCommand(ExecuteMergeAsync);
        MoveUpCommand = IdleCommand(() => Move(-1), () => CanMove(-1));
        MoveDownCommand = IdleCommand(() => Move(1), () => CanMove(1));
        RemoveFileCommand = IdleCommand(() => { if (SelectedItem is not null) { MergeItems.Remove(SelectedItem); RefreshOrders(); } });
    }
    public ObservableCollection<MergeItem> MergeItems { get; } = [];
    public MergeItem? SelectedItem { get => _selectedItem; set { _selectedItem = value; OnPropertyChanged(); RefreshCommands(); } }
    public IAsyncRelayCommand AddFilesCommand { get; }
    public IAsyncRelayCommand ExecuteMergeCommand { get; }
    public IRelayCommand MoveUpCommand { get; }
    public IRelayCommand MoveDownCommand { get; }
    public IRelayCommand RemoveFileCommand { get; }
    private bool CanMove(int delta) { var index = SelectedItem is null ? -1 : MergeItems.IndexOf(SelectedItem); return index >= 0 && index + delta >= 0 && index + delta < MergeItems.Count; }
    private void Move(int delta) { if (CanMove(delta)) { var index = MergeItems.IndexOf(SelectedItem!); ReorderItem(index, index + delta); } }
    public void ReorderItem(int oldIndex, int newIndex)
    {
        if (IsBusy || oldIndex < 0 || newIndex < 0 || oldIndex >= MergeItems.Count || newIndex >= MergeItems.Count) return;
        MergeItems.Move(oldIndex, newIndex); RefreshOrders();
    }
    private void RefreshOrders() { for (var i = 0; i < MergeItems.Count; i++) MergeItems[i].Order = i + 1; RefreshCommands(); }
    private async Task AddFilesAsync()
    {
        foreach (var path in await _dialogs.PickPdfFilesAsync())
            if (!string.IsNullOrWhiteSpace(path) && !MergeItems.Any(f => string.Equals(f.FullPath, path, StringComparison.OrdinalIgnoreCase))) MergeItems.Add(new MergeItem(path, MergeItems.Count + 1));
        StatusMessage = $"{MergeItems.Count} PDF files selected.";
    }
    private async Task ExecuteMergeAsync()
    {
        if (MergeItems.Count < 2) { StatusMessage = "Please select at least 2 PDFs."; return; }
        var paths = MergeItems.Select(f => f.FullPath).ToArray();
        var directories = paths.Select(OutputDirectory).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var output = directories.Length == 1 ? Path.Combine(directories[0], "Merged_Output.pdf")
            : await _dialogs.ShowSaveFileDialogAsync("Save merged PDF", "Merged_Output.pdf", ".pdf");
        if (string.IsNullOrWhiteSpace(output)) { StatusMessage = "Merge cancelled."; return; }
        var options = new MergeOptions(OutputDirectory(output), Path.GetFileName(output));
        await MergeAsync(paths, options);
    }
    private Task MergeAsync(string[] paths, MergeOptions options) => RunOperationAsync("Merge", () => _service.Merge(paths.Select(p => new PdfFile(p)).ToArray(), options), paths[0], () => MergeAsync(paths, options));
}
