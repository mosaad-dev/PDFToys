using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PDFToys.Core.Contracts;
using PDFToys.Core.Models;
using PDFToys.Core.Services;

namespace PDFToys.App.ViewModels;

public abstract class OperationViewModelBase : ViewModelBase
{
    private readonly IApplicationJobRunner _runner;
    private readonly List<IRelayCommand> _idleCommands = [];
    private CancellationTokenSource? _cancellation;
    private bool _isBusy;
    private bool _isCancelling;
    private string _statusMessage = "Ready";
    private string _progressText = string.Empty;
    private Func<Task>? _retry;
    private bool _isRetrying;
    private long _jobVersion;
    protected OperationViewModelBase(Action goBackAction, IApplicationJobRunner? runner = null)
    {
        _runner = runner ?? ApplicationJobRunner.Instance;
        GoBackCommand = IdleCommand(goBackAction);
        CancelCommand = new RelayCommand(RequestCancellation, () => IsBusy && !IsCancelling);
        RetryFailedCommand = new AsyncRelayCommand(async () =>
        {
            if (_retry is null) return;
            _isRetrying = true;
            try { await _retry(); } finally { _isRetrying = false; }
        }, () => !IsBusy && _retry is not null);
    }
    public ObservableCollection<OperationResultItemViewModel> Results { get; } = [];
    public bool HasResults => Results.Count > 0;
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIdle)); RefreshCommands(); } }
    public bool IsIdle => !IsBusy;
    public bool IsCancelling { get => _isCancelling; private set { _isCancelling = value; OnPropertyChanged(); CancelCommand.NotifyCanExecuteChanged(); } }
    public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }
    public string ProgressText { get => _progressText; private set { _progressText = value; OnPropertyChanged(); } }
    public IRelayCommand GoBackCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IAsyncRelayCommand RetryFailedCommand { get; }
    public bool CanRetry => _retry is not null && !IsBusy;
    public void RequestCancellation()
    {
        if (!IsBusy || IsCancelling) return;
        IsCancelling = true;
        StatusMessage = "Cancelling after the current step. Completed outputs will be kept.";
        _cancellation?.Cancel();
    }
    protected RelayCommand IdleCommand(Action action, Func<bool>? enabled = null)
    {
        var command = new RelayCommand(action, () => !IsBusy && (enabled?.Invoke() ?? true));
        _idleCommands.Add(command); return command;
    }
    protected RelayCommand<T> IdleCommand<T>(Action<T?> action, Predicate<T?>? enabled = null)
    {
        var command = new RelayCommand<T>(action, value => !IsBusy && (enabled?.Invoke(value) ?? true));
        _idleCommands.Add(command); return command;
    }
    protected AsyncRelayCommand IdleAsyncCommand(Func<Task> action, Func<bool>? enabled = null)
    {
        var command = new AsyncRelayCommand(async () =>
        {
            try { await action(); }
            catch (Exception ex) { StatusMessage = ex.Message; }
        }, () => !IsBusy && (enabled?.Invoke() ?? true));
        _idleCommands.Add(command); return command;
    }
    protected void RefreshCommands()
    {
        foreach (var command in _idleCommands) command.NotifyCanExecuteChanged();
        CancelCommand?.NotifyCanExecuteChanged(); RetryFailedCommand?.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(CanRetry));
    }
    protected Task RunOperationAsync(string label, Func<OperationResult> operation, string inputPath, Func<Task>? retry = null) =>
        RunBatchAsync(label, [inputPath], _ => operation(), _ => retry is null ? Task.CompletedTask : retry(), retry is not null);
    protected async Task RunBatchAsync(string label, IReadOnlyList<string> inputs, Func<string, OperationResult> operation, Func<IReadOnlyList<string>, Task> retry, bool allowRetry = true)
    {
        if (IsBusy) return;
        if (_isRetrying)
        {
            foreach (var result in Results.Where(r => !r.Result.IsSuccess).ToArray()) Results.Remove(result);
        }
        else Results.Clear();
        OnPropertyChanged(nameof(HasResults));
        _retry = null; IsCancelling = false; IsBusy = true;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        var version = ++_jobVersion;
        StatusMessage = $"{label}…";
        var failed = new List<string>(); var completed = 0; var unchanged = 0;
        var progress = new Progress<OperationProgress>(p =>
        {
            if (!IsBusy || _jobVersion != version || cancellation.IsCancellationRequested) return;
            ProgressText = string.IsNullOrWhiteSpace(p.CurrentFile) ? p.Stage : $"{p.Stage} · {System.IO.Path.GetFileName(p.CurrentFile)}";
            if (p.TotalPages is > 0) ProgressText += $" · page {p.CompletedPages}/{p.TotalPages}";
        });
        try
        {
            foreach (var input in inputs)
            {
                if (cancellation.IsCancellationRequested) break;
                ProgressText = $"File {completed + 1} of {inputs.Count} · {System.IO.Path.GetFileName(input)}";
                OperationResult result;
                try { result = await _runner.RunAsync(() => operation(input), cancellation.Token, progress); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { result = new OperationResult(false, string.Empty, ex.Message); }
                AddResult(input, result); completed++;
                if (result.Outcome == OperationOutcome.Unchanged) unchanged++;
                if (result.Files.Count == 0 && !result.IsSuccess && result.Outcome != OperationOutcome.Cancelled) failed.Add(input);
                if (result.Files.Count > 0 && !result.IsSuccess && result.Files.All(f => f.Result.IsSuccess) && result.Outcome != OperationOutcome.Cancelled) failed.Add(input);
                if (result.Files.Count > 0) failed.AddRange(result.Files.Where(f => !f.Result.IsSuccess && f.Result.Outcome != OperationOutcome.Cancelled).Select(f => f.InputPath));
                if (result.Outcome == OperationOutcome.Cancelled) break;
            }
            var cancelled = cancellation.IsCancellationRequested || Results.Any(r => r.Result.Outcome == OperationOutcome.Cancelled);
            if (cancelled)
                foreach (var input in inputs.Skip(completed)) AddResult(input, OperationResult.Cancelled("Not processed because the operation was cancelled."));
            StatusMessage = cancelled ? $"Cancelled after {completed}/{inputs.Count} files. Completed outputs are available below."
                : failed.Count > 0 ? $"{label}: {failed.Distinct().Count()} file(s) need attention. See results below."
                : unchanged == inputs.Count ? "Unchanged. No useful reduction; original files were preserved."
                : $"{label}: {completed}/{inputs.Count} files completed" + (unchanged > 0 ? $" ({unchanged} unchanged)." : ".");
            if (allowRetry && failed.Count > 0)
            {
                var snapshot = failed.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                _retry = () => retry(snapshot);
            }
        }
        finally { _cancellation = null; IsBusy = false; IsCancelling = false; ProgressText = string.Empty; RefreshCommands(); }
    }
    protected async Task<T?> InspectAsync<T>(string label, Func<T> inspect)
    {
        if (IsBusy) return default;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        ++_jobVersion;
        IsBusy = true; IsCancelling = false; StatusMessage = label;
        T? value = default;
        try
        {
            var result = await _runner.RunAsync(() => { value = inspect(); return new OperationResult(true, string.Empty, string.Empty); }, cancellation.Token);
            if (!result.IsSuccess) { StatusMessage = result.ErrorMessage; return default; }
            if (cancellation.IsCancellationRequested) { StatusMessage = "Cancelled."; return default; }
            return value;
        }
        catch (Exception ex) { StatusMessage = ex.Message; return default; }
        finally { _cancellation = null; IsBusy = false; IsCancelling = false; }
    }
    private void AddResult(string input, OperationResult result)
    {
        if (result.Files.Count > 0)
        {
            foreach (var file in result.Files)
                if (!file.Result.IsSuccess || file.Result.Outputs.Count > 0) AddResult(file.InputPath, file.Result);
            var childPaths = result.Files.SelectMany(f => f.Result.Outputs).Select(o => o.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var output in result.Outputs.Where(o => !childPaths.Contains(o.Path)))
                Results.Add(new OperationResultItemViewModel(input, result, output.Path));
            if (!result.IsSuccess && result.Files.All(f => f.Result.IsSuccess))
                Results.Add(new OperationResultItemViewModel(input, result, string.Empty));
        }
        else
        {
            var outputs = result.Outputs.Select(o => o.Path).ToList();
            if (outputs.Count == 0 && result.IsSuccess && !string.IsNullOrWhiteSpace(result.OutputPath)) outputs.Add(result.OutputPath);
            if (outputs.Count == 0) outputs.Add(string.Empty);
            foreach (var path in outputs.Distinct(StringComparer.OrdinalIgnoreCase)) Results.Add(new OperationResultItemViewModel(input, result, path));
        }
        OnPropertyChanged(nameof(HasResults));
    }
    protected static string OutputDirectory(string input) => Path.GetDirectoryName(input) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
}
