using System;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using PDFToys.Core.Contracts;
using PDFToys.App.Services;
using PDFToys.App.Models;

namespace PDFToys.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly StartupOperationRouter _startupRouter;
    private readonly IUserSettingsStore _settingsStore;
    private ViewModelBase _currentPage = null!;
    private readonly string? _startupWorkspace;

    public MainViewModel(IServiceProvider serviceProvider, OperationRequest request)
    {
        _serviceProvider = serviceProvider;
        _startupRouter = _serviceProvider.GetRequiredService<StartupOperationRouter>();
        _settingsStore = _serviceProvider.GetRequiredService<IUserSettingsStore>();
        var route = _startupRouter.BuildRoute(request);
        var sanitizedInputs = route.Inputs.ToList();
        var settings = _settingsStore.Load();

        var resolvedWorkspace = route.Workspace;
        if (string.IsNullOrWhiteSpace(resolvedWorkspace))
        {
            var inputPath = sanitizedInputs.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(inputPath))
            {
                resolvedWorkspace = Path.GetDirectoryName(inputPath);
            }
        }

        if (string.IsNullOrWhiteSpace(resolvedWorkspace) &&
            !string.IsNullOrWhiteSpace(settings.LastWorkspace) &&
            Directory.Exists(settings.LastWorkspace))
        {
            resolvedWorkspace = settings.LastWorkspace;
        }

        _startupWorkspace = resolvedWorkspace;

        if (!string.IsNullOrWhiteSpace(resolvedWorkspace) &&
            !string.Equals(settings.LastWorkspace, resolvedWorkspace, StringComparison.OrdinalIgnoreCase))
        {
            settings.LastWorkspace = resolvedWorkspace;
            _settingsStore.Save(settings);
        }

        CurrentPage = CreateHomeViewModel(_startupWorkspace);
        NavigateTo(ResolvePage(route));
    }

    public ViewModelBase CurrentPage
    {
        get => _currentPage;
        set
        {
            if (ReferenceEquals(_currentPage, value))
            {
                return;
            }

            _currentPage = value;
            OnPropertyChanged();
        }
    }

    public void NavigateTo(ViewModelBase page)
    {
        if (CurrentPage is OperationViewModelBase { IsBusy: true } operation)
        {
            operation.StatusMessage = "Wait for the operation to finish, or choose Cancel before returning home.";
            return;
        }
        CurrentPage = page;
    }

    private ViewModelBase ResolvePage(StartupRoute route)
    {
        var inputs = route.Inputs.ToList();
        switch (route.Kind)
        {
            case StartupRouteKind.Merge:
                {
                    var mergeViewModel = CreateMergeViewModel();
                    if (inputs.Count > 0)
                    {
                        mergeViewModel.MergeItems.Clear();
                        var order = 1;
                        foreach (var path in inputs)
                        {
                            mergeViewModel.MergeItems.Add(new MergeItem(path, order));
                            order++;
                        }

                        if (mergeViewModel.MergeItems.Count > 0)
                        {
                            mergeViewModel.StatusMessage = $"{mergeViewModel.MergeItems.Count} PDF files selected.";
                        }
                    }

                    return mergeViewModel;
                }
            case StartupRouteKind.Split:
                {
                    var splitViewModel = CreateSplitViewModel();
                    splitViewModel.SelectedFilePath = inputs.FirstOrDefault() ?? string.Empty;
                    splitViewModel.StatusMessage = string.IsNullOrWhiteSpace(splitViewModel.SelectedFilePath)
                        ? "Ready"
                        : "Ready to split selected PDF.";
                    return splitViewModel;
                }
            case StartupRouteKind.Compress:
                {
                    var compressViewModel = CreateCompressViewModel(inputs);
                    compressViewModel.StatusMessage = compressViewModel.SelectedFiles.Count == 0
                        ? "Please select PDF files first."
                        : $"Ready to compress {compressViewModel.SelectedFiles.Count} files.";
                    return compressViewModel;
                }
            case StartupRouteKind.Protect:
                {
                    var protectViewModel = CreateProtectViewModel(inputs);
                    protectViewModel.StatusMessage = protectViewModel.SelectedFiles.Count == 0
                        ? "Please select PDF files first."
                        : $"Ready to protect {protectViewModel.SelectedFiles.Count} files.";
                    return protectViewModel;
                }
            case StartupRouteKind.OrganizePages:
                return CreateOrganizePagesViewModel(inputs.FirstOrDefault());
            case StartupRouteKind.Unlock:
                {
                    var unlockViewModel = CreateUnlockViewModel(inputs);
                    unlockViewModel.StatusMessage = unlockViewModel.SelectedFiles.Count == 0
                        ? "Please select PDF files first."
                        : $"Ready to unlock {unlockViewModel.SelectedFiles.Count} files.";
                    return unlockViewModel;
                }
            case StartupRouteKind.ConvertExport:
                return CreateConvertExportViewModel(inputs);
            case StartupRouteKind.Placeholder:
                {
                    return CreateStartupPlaceholder(route.Operation);
                }
            case StartupRouteKind.Home:
            default:
                return CreateHomeViewModel(route.Workspace ?? _startupWorkspace);
        }
    }

    private HomeViewModel CreateHomeViewModel(string? workspaceDirectory)
    {
        return new HomeViewModel(
            NavigateTo,
            () => BuildMergePage(workspaceDirectory),
            () => BuildSplitPage(workspaceDirectory),
            () => BuildCompressPage(workspaceDirectory),
            () => BuildProtectPage(workspaceDirectory),
            () => BuildOrganizePagesPage(workspaceDirectory),
            () => BuildUnlockPage(workspaceDirectory),
            () => BuildConvertExportPage(workspaceDirectory),
            () => new AboutViewModel(GoHome),
            workspaceDirectory);
    }

    private void GoHome()
    {
        CurrentPage = CreateHomeViewModel(_startupWorkspace);
    }

    private MergeViewModel BuildMergePage(string? currentWorkspace)
    {
        var mergeViewModel = CreateMergeViewModel();
        var workspaceFiles = BuildWorkspacePdfPaths(currentWorkspace);
        mergeViewModel.MergeItems.Clear();
        var order = 1;
        foreach (var filePath in workspaceFiles)
        {
            mergeViewModel.MergeItems.Add(new MergeItem(filePath, order));
            order++;
        }

        mergeViewModel.StatusMessage = mergeViewModel.MergeItems.Count == 0
            ? "Please select at least 2 PDFs."
            : $"{mergeViewModel.MergeItems.Count} PDF files selected.";
        return mergeViewModel;
    }

    private SplitViewModel BuildSplitPage(string? currentWorkspace)
    {
        var splitViewModel = CreateSplitViewModel();
        var workspaceFiles = BuildWorkspacePdfPaths(currentWorkspace);
        splitViewModel.SelectedFilePath = workspaceFiles.FirstOrDefault() ?? string.Empty;
        splitViewModel.StatusMessage = string.IsNullOrWhiteSpace(splitViewModel.SelectedFilePath)
            ? "Please select a PDF file first."
            : "Ready to split selected PDF.";
        return splitViewModel;
    }

    private ProtectViewModel BuildProtectPage(string? currentWorkspace)
    {
        var workspaceFiles = BuildWorkspacePdfPaths(currentWorkspace);
        var protectViewModel = CreateProtectViewModel(workspaceFiles);
        protectViewModel.StatusMessage = protectViewModel.SelectedFiles.Count == 0
            ? "Please select PDF files first."
            : $"Ready to protect {protectViewModel.SelectedFiles.Count} files.";
        return protectViewModel;
    }

    private CompressViewModel BuildCompressPage(string? currentWorkspace)
    {
        var workspaceFiles = BuildWorkspacePdfPaths(currentWorkspace);
        var compressViewModel = CreateCompressViewModel(workspaceFiles);
        compressViewModel.StatusMessage = compressViewModel.SelectedFiles.Count == 0
            ? "Please select PDF files first."
            : $"Ready to compress {compressViewModel.SelectedFiles.Count} files.";
        return compressViewModel;
    }

    private OrganizePagesViewModel BuildOrganizePagesPage(string? currentWorkspace)
    {
        var workspaceFiles = BuildWorkspacePdfPaths(currentWorkspace);
        return CreateOrganizePagesViewModel(workspaceFiles.FirstOrDefault());
    }

    private UnlockViewModel BuildUnlockPage(string? currentWorkspace)
    {
        var workspaceFiles = BuildWorkspacePdfPaths(currentWorkspace);
        var unlockViewModel = CreateUnlockViewModel(workspaceFiles);
        unlockViewModel.StatusMessage = unlockViewModel.SelectedFiles.Count == 0
            ? "Please select PDF files first."
            : $"Ready to unlock {unlockViewModel.SelectedFiles.Count} files.";
        return unlockViewModel;
    }

    private ConvertExportViewModel BuildConvertExportPage(string? currentWorkspace)
    {
        return CreateConvertExportViewModel(BuildWorkspacePdfPaths(currentWorkspace));
    }

    private static List<string> BuildWorkspacePdfPaths(string? workspaceDirectory)
    {
        if (string.IsNullOrWhiteSpace(workspaceDirectory) || !Directory.Exists(workspaceDirectory))
        {
            return [];
        }

        return Directory
            .GetFiles(workspaceDirectory, "*.pdf")
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();
    }

    private MergeViewModel CreateMergeViewModel()
    {
        var mergeService = _serviceProvider.GetRequiredService<IMergeService>();
        var fileDialogService = _serviceProvider.GetRequiredService<IFileDialogService>();
        return new MergeViewModel(mergeService, fileDialogService, GoHome);
    }

    private SplitViewModel CreateSplitViewModel()
    {
        var splitService = _serviceProvider.GetRequiredService<ISplitService>();
        return new SplitViewModel(splitService, GoHome,
            _serviceProvider.GetRequiredService<IFileDialogService>(),
            _serviceProvider.GetRequiredService<IRearrangeService>());
    }

    private ProtectViewModel CreateProtectViewModel(IEnumerable<string>? initialFiles = null)
    {
        var protectService = _serviceProvider.GetRequiredService<IProtectService>();
        var fileDialogService = _serviceProvider.GetRequiredService<IFileDialogService>();
        return new ProtectViewModel(
            protectService,
            fileDialogService,
            GoHome,
            initialFiles,
            _settingsStore);
    }

    private CompressViewModel CreateCompressViewModel(IEnumerable<string>? initialFiles = null)
    {
        var compressService = _serviceProvider.GetRequiredService<ICompressionService>();
        var fileDialogService = _serviceProvider.GetRequiredService<IFileDialogService>();
        return new CompressViewModel(
            compressService,
            fileDialogService,
            GoHome,
            initialFiles,
            _settingsStore);
    }

    private OrganizePagesViewModel CreateOrganizePagesViewModel(string? initialFilePath = null)
    {
        var rearrangeService = _serviceProvider.GetRequiredService<IRearrangeService>();
        var fileDialogService = _serviceProvider.GetRequiredService<IFileDialogService>();
        var pagePreviewService = _serviceProvider.GetRequiredService<IPdfPagePreviewService>();
        var pagePreviewDialogService = _serviceProvider.GetRequiredService<IPagePreviewDialogService>();
        return new OrganizePagesViewModel(
            rearrangeService,
            fileDialogService,
            pagePreviewService,
            pagePreviewDialogService,
            GoHome,
            initialFilePath);
    }

    private UnlockViewModel CreateUnlockViewModel(IEnumerable<string>? initialFiles = null)
    {
        var protectService = _serviceProvider.GetRequiredService<IProtectService>();
        var fileDialogService = _serviceProvider.GetRequiredService<IFileDialogService>();
        return new UnlockViewModel(
            protectService,
            fileDialogService,
            GoHome,
            initialFiles,
            _settingsStore);
    }

    private ConvertExportViewModel CreateConvertExportViewModel(IEnumerable<string>? initialFiles = null)
    {
        var conversionService = _serviceProvider.GetRequiredService<IConversionService>();
        var mergeService = _serviceProvider.GetRequiredService<IMergeService>();
        var exportService = _serviceProvider.GetRequiredService<IExportService>();
        var fileDialogService = _serviceProvider.GetRequiredService<IFileDialogService>();
        return new ConvertExportViewModel(
            conversionService,
            mergeService,
            exportService,
            fileDialogService,
            GoHome,
            initialFiles);
    }

    private StartupOperationViewModel CreateStartupPlaceholder(PdfToysOperation operation)
    {
        return new StartupOperationViewModel(operation);
    }
}
