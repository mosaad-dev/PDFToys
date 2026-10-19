using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PDFToys.App.Services;
using PDFToys.Core.Contracts;
using PDFToys.Core.Models;

namespace PDFToys.App.ViewModels;

public sealed class SplitViewModel : OperationViewModelBase
{
    private readonly ISplitService _service;
    private readonly IFileDialogService? _dialogs;
    private readonly IRearrangeService? _pages;
    private string _selectedFilePath = string.Empty;
    private string _customRangeText = string.Empty;
    private int? _pageCount;
    public SplitViewModel(ISplitService splitService, Action goBackAction, IFileDialogService? fileDialogService = null,
        IRearrangeService? rearrangeService = null, IApplicationJobRunner? runner = null) : base(goBackAction, runner)
    {
        _service = splitService; _dialogs = fileDialogService; _pages = rearrangeService;
        SelectPdfCommand = IdleAsyncCommand(SelectPdfAsync);
        SplitPresetCommand = new AsyncRelayCommand<string>(ExecutePresetAsync, _ => !IsBusy);
        ExecuteCustomSplitCommand = IdleAsyncCommand(ExecuteCustomAsync);
        PropertyChanged += (_, e) => { if (e.PropertyName == nameof(IsBusy)) SplitPresetCommand.NotifyCanExecuteChanged(); };
    }
    public string SelectedFilePath
    {
        get => _selectedFilePath;
        set { if (_selectedFilePath == value) return; _selectedFilePath = value; _pageCount = null; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedFileLabel)); OnPropertyChanged(nameof(PageCountLabel)); }
    }
    public string SelectedFileLabel => string.IsNullOrWhiteSpace(SelectedFilePath) ? "No PDF selected" : SelectedFilePath;
    public string PageCountLabel => _pageCount is { } count ? $"{count} pages" : "Choose a PDF to see its page count.";
    public string CustomRangeText { get => _customRangeText; set { _customRangeText = value; OnPropertyChanged(); } }
    public IAsyncRelayCommand SelectPdfCommand { get; }
    public IAsyncRelayCommand<string> SplitPresetCommand { get; }
    public IAsyncRelayCommand ExecuteCustomSplitCommand { get; }
    public async Task LoadInputAsync(string path)
    {
        if (!File.Exists(path) || !path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) { StatusMessage = "Select an existing PDF file."; return; }
        int? count = null;
        try { if (_pages is not null) count = await InspectAsync("Reading PDF…", () => _pages.TryGetPageCount(new PdfFile(path))); }
        catch (Exception ex) { StatusMessage = $"Could not read PDF: {ex.Message}"; return; }
        if (_pages is not null && count is null or 0) { StatusMessage = "Could not read the selected PDF. Check that it is valid and unlocked."; return; }
        SelectedFilePath = path; _pageCount = count; OnPropertyChanged(nameof(PageCountLabel)); StatusMessage = "Ready to split selected PDF.";
    }
    private async Task SelectPdfAsync() { if (_dialogs is null) return; var path = await _dialogs.PickPdfFileAsync(); if (!string.IsNullOrWhiteSpace(path)) await LoadInputAsync(path); }
    private async Task ExecutePresetAsync(string? preset)
    {
        if (!ValidateInput()) return;
        var choice = preset == "half" ? SplitPreset.Half : preset == "quarter" ? SplitPreset.Quarter : SplitPreset.None;
        if (choice == SplitPreset.None) { StatusMessage = "Invalid split preset."; return; }
        var path = SelectedFilePath; var options = new SplitOptions(OutputDirectory(path), 0, null, choice);
        await RunOperationAsync("Split", () => _service.Split(new PdfFile(path), options), path, () => ExecuteSavedAsync(path, options));
    }
    private Task ExecuteSavedAsync(string path, SplitOptions options) => RunOperationAsync("Split", () => _service.Split(new PdfFile(path), options), path, () => ExecuteSavedAsync(path, options));
    private async Task ExecuteCustomAsync()
    {
        if (!ValidateInput()) return;
        if (!TryParseCustomRanges(CustomRangeText, out var ranges)) { StatusMessage = "Enter page ranges such as 1-5, 8, 11-13."; return; }
        if (_pageCount is { } count && ranges.Any(r => r.EndPage > count)) { StatusMessage = $"Page ranges must be between 1 and {count}."; return; }
        var path = SelectedFilePath; var options = new SplitOptions(OutputDirectory(path), 0, ranges);
        await ExecuteSavedAsync(path, options);
    }
    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(SelectedFilePath)) { StatusMessage = "Please select a PDF file first."; return false; }
        return true;
    }
    private static bool TryParseCustomRanges(string text, out SplitRange[] ranges)
    {
        ranges = []; if (string.IsNullOrWhiteSpace(text)) return false;
        var parsed = new List<SplitRange>();
        foreach (var token in text.Split(',', StringSplitOptions.TrimEntries))
        {
            var bounds = token.Split('-', StringSplitOptions.TrimEntries);
            if (bounds.Length is < 1 or > 2 || !int.TryParse(bounds[0], out var start) || start < 1) return false;
            var end = start;
            if (bounds.Length == 2 && (!int.TryParse(bounds[1], out end) || end < start)) return false;
            parsed.Add(new SplitRange(start, end));
        }
        ranges = parsed.ToArray(); return ranges.Length > 0;
    }
}
