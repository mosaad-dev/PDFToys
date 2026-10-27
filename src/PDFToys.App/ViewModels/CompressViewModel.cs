using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PDFToys.App.Services;
using PDFToys.Core.Contracts;
using PDFToys.Core.Models;

namespace PDFToys.App.ViewModels;

public sealed class CompressViewModel : OperationViewModelBase
{
    private readonly ICompressionService _service;
    private readonly IFileDialogService _dialogs;
    private readonly IUserSettingsStore? _settings;
    private string _level = "Standard";
    public CompressViewModel(ICompressionService compressionService, IFileDialogService fileDialogService, Action goBackAction,
        IEnumerable<string>? initialFiles = null, IUserSettingsStore? settingsStore = null, IApplicationJobRunner? runner = null) : base(goBackAction, runner)
    {
        _service = compressionService; _dialogs = fileDialogService; _settings = settingsStore;
        AddFilesCommand = IdleAsyncCommand(AddFilesAsync);
        RemoveFileCommand = IdleCommand<PdfFileItem>(file => { if (file is not null) SelectedFiles.Remove(file); });
        ExecuteCompressCommand = IdleAsyncCommand(ExecuteAsync);
        AddFiles(initialFiles ?? []);
        if (_settings is not null)
        {
            var level = _settings.Load().DefaultCompressionLevel;
            if (CompressionLevels.Contains(level)) SelectedCompressionLevel = level;
        }
    }
    public ObservableCollection<PdfFileItem> SelectedFiles { get; } = [];
    public ObservableCollection<string> CompressionLevels { get; } = ["Standard", "Maximum"];
    public string SelectedCompressionLevel { get => _level; set { _level = value; OnPropertyChanged(); } }
    public IAsyncRelayCommand AddFilesCommand { get; }
    public IRelayCommand RemoveFileCommand { get; }
    public IAsyncRelayCommand ExecuteCompressCommand { get; }
    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
            if (!SelectedFiles.Any(f => string.Equals(f.FullPath, path, StringComparison.OrdinalIgnoreCase))) SelectedFiles.Add(new PdfFileItem(path, Path.GetFileName(path)));
    }
    private async Task AddFilesAsync() { AddFiles(await _dialogs.PickPdfFilesAsync()); StatusMessage = $"{SelectedFiles.Count} PDF files selected."; }
    private async Task ExecuteAsync()
    {
        if (SelectedFiles.Count == 0) { StatusMessage = "Please select PDF files first."; return; }
        if (!CompressionLevels.Contains(SelectedCompressionLevel)) { StatusMessage = "Please select a valid compression level."; return; }
        var level = SelectedCompressionLevel;
        await CompressAsync(SelectedFiles.Select(f => f.FullPath).ToArray(), level);
        if (_settings is not null && Results.All(r => r.Result.IsSuccess))
        {
            try { var settings = _settings.Load(); settings.DefaultCompressionLevel = level; _settings.Save(settings); }
            catch (Exception) { StatusMessage += " Preferences could not be saved; outputs are available below."; }
        }
    }
    private Task CompressAsync(IReadOnlyList<string> inputs, string level) => RunBatchAsync("Compression", inputs,
        input => _service.Compress(new PdfFile(input), new CompressionOptions(OutputDirectory(input), level == "Maximum" ? CompressionLevel.Maximum : CompressionLevel.Normal)),
        failed => CompressAsync(failed, level));
}
