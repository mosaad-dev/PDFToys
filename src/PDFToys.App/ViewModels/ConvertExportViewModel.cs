using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PDFToys.App.Models;
using PDFToys.App.Services;
using PDFToys.Core.Contracts;
using PDFToys.Core.Models;
using PDFToys.Core.Services;

namespace PDFToys.App.ViewModels;

public sealed class ConvertExportViewModel : OperationViewModelBase
{
    private readonly ConversionWorkflowService _workflow;
    private readonly IFileDialogService _dialogs;
    private ConvertExportOperationItem _selected;
    public ConvertExportViewModel(IConversionService conversionService, IMergeService mergePdfService, IExportService pdfExportService,
        IFileDialogService fileDialogService, Action goBackAction, IEnumerable<string>? initialFiles = null, IApplicationJobRunner? runner = null) : base(goBackAction, runner)
    {
        _workflow = new ConversionWorkflowService(conversionService, mergePdfService, pdfExportService); _dialogs = fileDialogService;
        OperationItems = [new(ConvertExportOperation.ConvertToPdf, "Convert one file to PDF"), new(ConvertExportOperation.CombineToPdf, "Combine files into one PDF"),
            new(ConvertExportOperation.ConvertEachToPdf, "Convert each file to PDF"), new(ConvertExportOperation.PdfToJpg, "Export PDF pages as JPG"),
            new(ConvertExportOperation.PdfToJpeg, "Export PDF pages as JPEG"), new(ConvertExportOperation.PdfToPng, "Export PDF pages as PNG"), new(ConvertExportOperation.PdfToMarkdown, "Extract PDF text as Markdown")];
        _selected = OperationItems[0];
        foreach (var path in initialFiles ?? []) AddFile(path);
        AddFilesCommand = IdleAsyncCommand(AddFilesAsync);
        RemoveFileCommand = IdleCommand<PdfFileItem>(file => { if (file is not null) SelectedFiles.Remove(file); });
        ExecuteCommand = IdleAsyncCommand(ExecuteAsync);
        StatusMessage = "Select files and an operation to begin.";
    }
    public ObservableCollection<PdfFileItem> SelectedFiles { get; } = [];
    public IReadOnlyList<ConvertExportOperationItem> OperationItems { get; }
    public ConvertExportOperationItem SelectedOperationItem { get => _selected; set { _selected = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedOperationLabel)); OnPropertyChanged(nameof(AddFilesButtonLabel)); } }
    public ConvertExportOperation SelectedOperation => SelectedOperationItem.Operation;
    public string SelectedOperationLabel => SelectedOperationItem.Label;
    public string AddFilesButtonLabel => RequiresPdf(SelectedOperation) ? "Add PDF files" : "Add files";
    public IAsyncRelayCommand AddFilesCommand { get; }
    public IRelayCommand RemoveFileCommand { get; }
    public IAsyncRelayCommand ExecuteCommand { get; }
    private void AddFile(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && !SelectedFiles.Any(f => string.Equals(f.FullPath, path, StringComparison.OrdinalIgnoreCase))) SelectedFiles.Add(new PdfFileItem(path, Path.GetFileName(path)));
    }
    private async Task AddFilesAsync()
    {
        var files = RequiresPdf(SelectedOperation) ? await _dialogs.PickPdfFilesAsync() : await _dialogs.PickConvertibleFilesAsync();
        foreach (var path in files) AddFile(path);
        StatusMessage = $"{SelectedFiles.Count} files selected.";
    }
    private Task ExecuteAsync()
    {
        if (SelectedFiles.Count == 0) { StatusMessage = "Please select files first."; return Task.CompletedTask; }
        var inputs = SelectedFiles.Select(f => f.FullPath).ToArray(); var selected = SelectedOperation;
        if (selected == ConvertExportOperation.ConvertToPdf && inputs.Length != 1) { StatusMessage = "Convert to PDF requires exactly one file."; return Task.CompletedTask; }
        if (selected == ConvertExportOperation.CombineToPdf && inputs.Length < 2) { StatusMessage = "Combine into PDF requires at least two files."; return Task.CompletedTask; }
        if (RequiresPdf(selected) && inputs.Any(p => !p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))) { StatusMessage = "PDF export operations require PDF files only."; return Task.CompletedTask; }
        return ExecuteJobAsync(inputs, selected);
    }
    private Task ExecuteJobAsync(IReadOnlyList<string> inputs, ConvertExportOperation operation)
    {
        var kind = operation switch { ConvertExportOperation.ConvertToPdf => ConversionJobKind.ConvertToPdf, ConvertExportOperation.ConvertEachToPdf => ConversionJobKind.ConvertEachToPdf, ConvertExportOperation.CombineToPdf => ConversionJobKind.CombineToPdf, _ => ConversionJobKind.Export };
        var format = operation switch { ConvertExportOperation.PdfToJpg => PdfExportFormat.Jpg, ConvertExportOperation.PdfToJpeg => PdfExportFormat.Jpeg, ConvertExportOperation.PdfToMarkdown => PdfExportFormat.Markdown, _ => PdfExportFormat.Png };
        var job = new ConversionJob(kind, inputs, null, format);
        return RunBatchAsync("Conversion / export", [inputs[0]], _ => _workflow.Execute(job), failed => ExecuteJobAsync(kind == ConversionJobKind.CombineToPdf ? inputs : failed, operation));
    }
    private static bool RequiresPdf(ConvertExportOperation operation) => operation is ConvertExportOperation.PdfToJpg or ConvertExportOperation.PdfToJpeg or ConvertExportOperation.PdfToPng or ConvertExportOperation.PdfToMarkdown;
}
