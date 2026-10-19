using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PDFToys.App.Models;
using PDFToys.App.Services;
using PDFToys.Core.Contracts;
using PDFToys.Core.Models;

namespace PDFToys.App.ViewModels;

public sealed class OrganizePagesViewModel : OperationViewModelBase
{
    private readonly IRearrangeService _service;
    private readonly IFileDialogService _dialogs;
    private readonly IPdfPagePreviewService _preview;
    private readonly IPagePreviewDialogService _previewDialog;
    private string _selectedFilePath = string.Empty;
    private string _outputFilePath = string.Empty;
    private OrganizePageItemViewModel? _selectedPage;
    public OrganizePagesViewModel(IRearrangeService rearrangeService, IFileDialogService fileDialogService,
        IPdfPagePreviewService pagePreviewService, IPagePreviewDialogService pagePreviewDialogService, Action goBackAction,
        string? initialFilePath = null, IApplicationJobRunner? runner = null) : base(goBackAction, runner)
    {
        _service = rearrangeService; _dialogs = fileDialogService; _preview = pagePreviewService; _previewDialog = pagePreviewDialogService;
        _selectedFilePath = initialFilePath ?? string.Empty;
        SelectPdfCommand = IdleAsyncCommand(SelectPdfAsync);
        ChooseOutputCommand = IdleAsyncCommand(ChooseOutputAsync);
        MoveUpCommand = IdleCommand(() => Move(-1), () => CanMove(-1));
        MoveDownCommand = IdleCommand(() => Move(1), () => CanMove(1));
        RotateLeftCommand = IdleCommand(() => Rotate(-90), () => SelectedPage is not null);
        RotateRightCommand = IdleCommand(() => Rotate(90), () => SelectedPage is not null);
        DeleteCommand = IdleCommand(() => Include(false), () => SelectedPage?.IsIncluded == true);
        RestoreCommand = IdleCommand(() => Include(true), () => SelectedPage?.IsIncluded == false);
        ResetCommand = IdleAsyncCommand(() => LoadInputAsync(SelectedFilePath), () => !string.IsNullOrWhiteSpace(SelectedFilePath));
        ViewPageCommand = IdleAsyncCommand(ViewPageAsync, () => SelectedPage is not null);
        SaveCommand = IdleAsyncCommand(SaveAsync, CanSave);
        StatusMessage = "Select a PDF to reorder, rotate or remove pages.";
    }
    public ObservableCollection<OrganizePageItemViewModel> Pages { get; } = [];
    public string SelectedFilePath { get => _selectedFilePath; private set { _selectedFilePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedFileLabel)); } }
    public string SelectedFileLabel => string.IsNullOrWhiteSpace(SelectedFilePath) ? "No PDF selected" : SelectedFilePath;
    public string OutputFilePath { get => _outputFilePath; private set { _outputFilePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(SavedAsLabel)); } }
    public string SavedAsLabel => string.IsNullOrWhiteSpace(OutputFilePath) ? "New copy beside the original" : OutputFilePath;
    public OrganizePageItemViewModel? SelectedPage { get => _selectedPage; set { _selectedPage = value; OnPropertyChanged(); RefreshCommands(); } }
    public IAsyncRelayCommand SelectPdfCommand { get; }
    public IAsyncRelayCommand ChooseOutputCommand { get; }
    public IRelayCommand MoveUpCommand { get; }
    public IRelayCommand MoveDownCommand { get; }
    public IRelayCommand RotateLeftCommand { get; }
    public IRelayCommand RotateRightCommand { get; }
    public IRelayCommand DeleteCommand { get; }
    public IRelayCommand RestoreCommand { get; }
    public IAsyncRelayCommand ResetCommand { get; }
    public IAsyncRelayCommand ViewPageCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public async Task LoadInputAsync(string path)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(path)) return;
        var count = await InspectAsync("Reading PDF…", () => _service.TryGetPageCount(new PdfFile(path)));
        if (count is null or 0) { StatusMessage = "Could not read PDF. Check that the file exists and is unlocked."; return; }
        SelectedFilePath = path; Pages.Clear();
        for (var page = 1; page <= count; page++) Pages.Add(new OrganizePageItemViewModel(page, page));
        OutputFilePath = Path.Combine(OutputDirectory(path), $"{Path.GetFileNameWithoutExtension(path)}_organized.pdf");
        SelectedPage = Pages.FirstOrDefault(); StatusMessage = $"Loaded {count} pages. Select a page to edit or preview."; RefreshCommands();
    }
    private async Task SelectPdfAsync() { var path = await _dialogs.PickPdfFileAsync(); if (!string.IsNullOrWhiteSpace(path)) await LoadInputAsync(path); }
    private async Task ChooseOutputAsync()
    {
        var path = await _dialogs.ShowSaveFileDialogAsync("Save organized PDF", string.IsNullOrWhiteSpace(OutputFilePath) ? "organized.pdf" : Path.GetFileName(OutputFilePath), ".pdf");
        if (!string.IsNullOrWhiteSpace(path)) { OutputFilePath = path; RefreshCommands(); }
    }
    private bool CanMove(int delta) { var index = SelectedPage is null ? -1 : Pages.IndexOf(SelectedPage); return index >= 0 && index + delta >= 0 && index + delta < Pages.Count; }
    private void Move(int delta)
    {
        if (!CanMove(delta)) return;
        var index = Pages.IndexOf(SelectedPage!); Pages.Move(index, index + delta);
        for (var i = 0; i < Pages.Count; i++) Pages[i].DisplayPosition = i + 1;
        RefreshCommands();
    }
    private void Rotate(int delta) { if (SelectedPage is not null) SelectedPage.RotationDegrees += delta; RefreshCommands(); }
    private void Include(bool included) { if (SelectedPage is not null) SelectedPage.IsIncluded = included; RefreshCommands(); }
    private async Task ViewPageAsync()
    {
        if (SelectedPage is null) return;
        var path = SelectedFilePath; var page = SelectedPage.SourcePageNumber; var delta = SelectedPage.RotationDegrees;
        var details = $"Position {SelectedPage.DisplayPosition} · Source page {page} · Added rotation {delta}° · Included: {SelectedPage.IncludedLabel}";
        var result = await InspectAsync("Rendering preview…", () =>
        {
            var rendered = _preview.RenderPage(path, page);
            return rendered.IsSuccess && rendered.ImagePngBytes is { } bytes ? rendered with { ImagePngBytes = PreviewRotation.Apply(bytes, delta) } : rendered;
        });
        if (result is null) return;
        await _previewDialog.ShowAsync(new PagePreviewRequest($"Page {page}", details, result.ImagePngBytes, result.IsSuccess ? string.Empty : result.ErrorMessage));
        StatusMessage = result.IsSuccess ? "Preview shows the page with pending rotation." : result.ErrorMessage;
    }
    private bool CanSave() => !string.IsNullOrWhiteSpace(SelectedFilePath) && !string.IsNullOrWhiteSpace(OutputFilePath) && Pages.Any(p => p.IsIncluded);
    private Task SaveAsync()
    {
        if (!CanSave()) { StatusMessage = "Select a PDF and include at least one page."; return Task.CompletedTask; }
        var path = SelectedFilePath; var pages = Pages.Where(p => p.IsIncluded).Select(p => new PageArrangementItem(p.SourcePageNumber - 1, p.RotationDegrees)).ToArray();
        var options = new RearrangeOptions(OutputDirectory(OutputFilePath), Path.GetFileName(OutputFilePath));
        return SaveSnapshotAsync(path, pages, options);
    }
    private async Task SaveSnapshotAsync(string path, PageArrangementItem[] pages, RearrangeOptions options)
    {
        await RunOperationAsync("Save organized PDF", () => _service.Rearrange(new PdfFile(path), pages, options), path, () => SaveSnapshotAsync(path, pages, options));
        var saved = Results.FirstOrDefault(r => r.Result.IsSuccess && r.HasOutput);
        if (saved is not null) OutputFilePath = saved.Path;
    }
}
