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

namespace PDFToys.App.ViewModels;

public sealed class UnlockViewModel : OperationViewModelBase
{
    private readonly IProtectService _service;
    private readonly IFileDialogService _dialogs;
    private readonly IUserSettingsStore? _settings;
    private string _password = string.Empty;
    public UnlockViewModel(IProtectService passwordService, IFileDialogService fileDialogService, Action goBackAction,
        IEnumerable<string>? initialFiles = null, IUserSettingsStore? settingsStore = null, IApplicationJobRunner? runner = null) : base(goBackAction, runner)
    {
        _service = passwordService; _dialogs = fileDialogService; _settings = settingsStore;
        AddFilesCommand = IdleAsyncCommand(AddFilesAsync);
        RemoveFileCommand = IdleCommand<PdfFileItem>(file => { if (file is not null) SelectedFiles.Remove(file); });
        ExecuteUnlockCommand = IdleAsyncCommand(ExecuteAsync);
        AddFiles(initialFiles ?? []);
        if (_settings is not null) OutputMode.ApplyModeFromName(_settings.Load().DefaultPdfOutputMode);
    }
    public PdfOutputModeSelection OutputMode { get; } = new();
    public ObservableCollection<PdfFileItem> SelectedFiles { get; } = [];
    public string Password { get => _password; set { _password = value; OnPropertyChanged(); } }
    public IAsyncRelayCommand AddFilesCommand { get; }
    public IRelayCommand RemoveFileCommand { get; }
    public IAsyncRelayCommand ExecuteUnlockCommand { get; }
    private void AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
            if (!SelectedFiles.Any(f => string.Equals(f.FullPath, path, StringComparison.OrdinalIgnoreCase))) SelectedFiles.Add(new PdfFileItem(path, Path.GetFileName(path)));
    }
    private async Task AddFilesAsync() { AddFiles(await _dialogs.PickPdfFilesAsync()); StatusMessage = $"{SelectedFiles.Count} PDF files selected."; }
    private async Task ExecuteAsync()
    {
        if (SelectedFiles.Count == 0) { StatusMessage = "Please select PDF files first."; return; }
        if (string.IsNullOrWhiteSpace(Password)) { StatusMessage = "Password is required."; return; }
        var password = Password;
        var mode = OutputMode.SelectedOutputMode;
        await ProcessAsync(SelectedFiles.Select(f => f.FullPath).ToArray(), password, mode == PdfOutputMode.ReplaceOriginal);
        if (_settings is not null && Results.All(r => r.Result.IsSuccess))
        {
            try { var settings = _settings.Load(); settings.DefaultPdfOutputMode = mode.ToString(); _settings.Save(settings); }
            catch (Exception) { StatusMessage += " Preferences could not be saved; outputs are available below."; }
        }
    }
    private Task ProcessAsync(IReadOnlyList<string> inputs, string password, bool replace) => RunBatchAsync("Unlock", inputs,
        input => _service.Unlock(new PdfFile(input), new UnlockOptions(password, OutputDirectory(input), replace)),
        failed => ProcessAsync(failed, password, replace));
}
