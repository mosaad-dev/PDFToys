using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;

namespace PDFToys.App.ViewModels;

public sealed class HomeViewModel : ViewModelBase
{
    private string _currentWorkspace = string.Empty;
    private readonly Action<ViewModelBase> _navigateAction;
    private readonly Func<ViewModelBase>? _createMergePage;
    private readonly Func<ViewModelBase>? _createSplitPage;
    private readonly Func<ViewModelBase>? _createCompressPage;
    private readonly Func<ViewModelBase>? _createProtectPage;
    private readonly Func<ViewModelBase>? _createOrganizePagesPage;
    private readonly Func<ViewModelBase>? _createUnlockPage;
    private readonly Func<ViewModelBase>? _createConvertExportPage;
    private readonly Func<ViewModelBase>? _createAboutPage;

    public HomeViewModel(
        Action<ViewModelBase> navigateAction,
        Func<ViewModelBase>? createMergePage = null,
        Func<ViewModelBase>? createSplitPage = null,
        Func<ViewModelBase>? createCompressPage = null,
        Func<ViewModelBase>? createProtectPage = null,
        Func<ViewModelBase>? createOrganizePagesPage = null,
        Func<ViewModelBase>? createUnlockPage = null,
        Func<ViewModelBase>? createConvertExportPage = null,
        Func<ViewModelBase>? createAboutPage = null,
        string? directoryPath = null)
    {
        _navigateAction = navigateAction;
        _createMergePage = createMergePage;
        _createSplitPage = createSplitPage;
        _createCompressPage = createCompressPage;
        _createProtectPage = createProtectPage;
        _createOrganizePagesPage = createOrganizePagesPage;
        _createUnlockPage = createUnlockPage;
        _createConvertExportPage = createConvertExportPage;
        _createAboutPage = createAboutPage;
        AvailableFiles = [];
        GoToMergeCommand = new DelegateCommand(() => NavigateToTarget(_createMergePage));
        GoToSplitCommand = new DelegateCommand(() => NavigateToTarget(_createSplitPage));
        GoToCompressCommand = new DelegateCommand(() => NavigateToTarget(_createCompressPage));
        GoToProtectCommand = new DelegateCommand(() => NavigateToTarget(_createProtectPage));
        GoToOrganizePagesCommand = new DelegateCommand(() => NavigateToTarget(_createOrganizePagesPage));
        GoToUnlockCommand = new DelegateCommand(() => NavigateToTarget(_createUnlockPage));
        GoToConvertExportCommand = new DelegateCommand(() => NavigateToTarget(_createConvertExportPage));
        GoToAboutCommand = new DelegateCommand(() => NavigateToTarget(_createAboutPage));

        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        CurrentWorkspace = directoryPath;

    }

    public string Message => "Open a PDF tool for this workspace";

    public string SecondaryMessage => string.IsNullOrWhiteSpace(CurrentWorkspace)
        ? "Select a folder workspace to begin."
        : "Choose a tool to continue.";

    public string CurrentWorkspace
    {
        get => _currentWorkspace;
        set
        {
            if (_currentWorkspace == value)
            {
                return;
            }

            _currentWorkspace = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SecondaryMessage));
        }
    }

    public ObservableCollection<string> AvailableFiles { get; }

    public ICommand GoToMergeCommand { get; }

    public ICommand GoToSplitCommand { get; }

    public ICommand GoToCompressCommand { get; }

    public ICommand GoToProtectCommand { get; }

    public ICommand GoToOrganizePagesCommand { get; }

    public ICommand GoToUnlockCommand { get; }

    public ICommand GoToConvertExportCommand { get; }

    public ICommand GoToAboutCommand { get; }

    private void NavigateToTarget(Func<ViewModelBase>? createTarget)
    {
        if (createTarget is null)
        {
            return;
        }

        _navigateAction(createTarget());
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
