using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PDFToys.App.Services;
using PDFToys.Core.Models;

namespace PDFToys.App.ViewModels;

public sealed class OperationResultItemViewModel : ViewModelBase
{
    private string _details;
    public OperationResultItemViewModel(string inputPath, OperationResult result, string path, IResultActionService? actions = null)
    {
        InputPath = inputPath; Result = result; Path = path;
        var service = actions ?? new ResultActionService();
        _details = BuildDetails(result);
        OpenCommand = new RelayCommand(() => Act(() => service.Open(Path)), () => HasOutput);
        ShowInFolderCommand = new RelayCommand(() => Act(() => service.ShowInFolder(Path)), () => HasOutput);
        CopyPathCommand = new AsyncRelayCommand(() => CopyAsync(service), () => HasOutput);
    }
    public string InputPath { get; }
    public OperationResult Result { get; }
    public string Path { get; }
    public string FileName => System.IO.Path.GetFileName(string.IsNullOrEmpty(Path) ? InputPath : Path);
    public bool HasOutput => !string.IsNullOrWhiteSpace(Path);
    public string OutcomeLabel => Result.Outcome switch
    {
        OperationOutcome.Unchanged => "Unchanged",
        OperationOutcome.PartialSuccess => "Partially completed",
        OperationOutcome.Cancelled => "Cancelled",
        OperationOutcome.Failed => "Failed",
        _ => "Completed"
    };
    public string Details { get => _details; private set { _details = value; OnPropertyChanged(); } }
    public IRelayCommand OpenCommand { get; }
    public IRelayCommand ShowInFolderCommand { get; }
    public IAsyncRelayCommand CopyPathCommand { get; }
    private void Act(Action action) { try { action(); } catch (Exception ex) { Details = ex.Message; } }
    private async Task CopyAsync(IResultActionService service)
    { try { await service.CopyPathAsync(Path); } catch (Exception ex) { Details = ex.Message; } }
    private static string BuildDetails(OperationResult result)
    {
        var parts = result.Warnings.ToList();
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage)) parts.Insert(0, result.ErrorMessage);
        if (result.Compression is { } stats)
        {
            var saved = Math.Max(0, stats.OriginalBytes - stats.FinalBytes);
            var percent = stats.OriginalBytes > 0 ? saved * 100d / stats.OriginalBytes : 0;
            parts.Insert(0, $"{FormatBytes(stats.OriginalBytes)} → {FormatBytes(stats.FinalBytes)} · {percent:0.#}% saved · {stats.ImagesChanged} images changed, {stats.ImagesSkipped} skipped");
            parts.AddRange(stats.SkipReasons);
        }
        return string.Join(Environment.NewLine, parts.Distinct());
    }
    private static string FormatBytes(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.##} MB" : $"{bytes / 1024d:0.##} KB";
}
