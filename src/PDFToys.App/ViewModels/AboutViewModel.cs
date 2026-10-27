using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Input;

namespace PDFToys.App.ViewModels;

public sealed class AboutViewModel : ViewModelBase
{
    public AboutViewModel(Action goBackAction)
    {
        GoBackCommand = new DelegateCommand(goBackAction);
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText = version is null ? "0.2.0" : version.ToString(3);
        LicensePath = Path.Combine(AppContext.BaseDirectory, "LICENSE");
        NoticePath = Path.Combine(AppContext.BaseDirectory, "NOTICE");
        OpenLicenseCommand = new DelegateCommand(() => OpenDocument(LicensePath));
        OpenNoticesCommand = new DelegateCommand(() => OpenDocument(NoticePath));
    }

    public string Title => "About PDFToys";

    public string VersionText { get; }

    public string LicenseSummary =>
        "PDFToys is distributed under the Apache License 2.0.";

    public string LicensePath { get; }
    public string NoticePath { get; }

    public bool IsLicenseAvailable => File.Exists(LicensePath);
    public bool AreNoticesAvailable => File.Exists(NoticePath);

    public ICommand GoBackCommand { get; }
    public ICommand OpenLicenseCommand { get; }
    public ICommand OpenNoticesCommand { get; }

    private string _statusMessage = string.Empty;
    public string StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnPropertyChanged(); } }

    private void OpenDocument(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var startInfo = new ProcessStartInfo("notepad.exe") { UseShellExecute = false };
        startInfo.ArgumentList.Add(path);
        try { Process.Start(startInfo); }
        catch (Exception ex) { StatusMessage = $"Could not open document: {ex.Message}"; }
    }

    private sealed class DelegateCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();
    }
}
