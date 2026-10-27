using System;
using System.Collections.Generic;
using System.Linq;
using PDFToys.App.Models;

namespace PDFToys.App.ViewModels;

public sealed class PdfOutputModeSelection : ViewModelBase
{
    public const string ReplaceOriginalWarning =
        "The replacement PDF is validated and a recovery copy is kept when the original is replaced. The recovery location appears in the results.";

    private PdfOutputModeItem _selectedOutputModeItem;

    public PdfOutputModeSelection()
    {
        OutputModeItems =
        [
            new PdfOutputModeItem(PdfOutputMode.CreateNewCopy, "Create new copy"),
            new PdfOutputModeItem(PdfOutputMode.ReplaceOriginal, "Replace original file")
        ];
        _selectedOutputModeItem = OutputModeItems[0];
    }

    public IReadOnlyList<PdfOutputModeItem> OutputModeItems { get; }

    public PdfOutputModeItem SelectedOutputModeItem
    {
        get => _selectedOutputModeItem;
        set
        {
            if (_selectedOutputModeItem == value)
            {
                return;
            }

            _selectedOutputModeItem = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedOutputMode));
            OnPropertyChanged(nameof(ShowReplaceOriginalWarning));
        }
    }

    public PdfOutputMode SelectedOutputMode => SelectedOutputModeItem.Mode;

    public bool ShowReplaceOriginalWarning =>
        SelectedOutputMode == PdfOutputMode.ReplaceOriginal;

    public void ApplyMode(PdfOutputMode mode)
    {
        var item = OutputModeItems.FirstOrDefault(i => i.Mode == mode);
        if (item is not null)
        {
            SelectedOutputModeItem = item;
        }
    }

    public void ApplyModeFromName(string? modeName)
    {
        if (string.IsNullOrWhiteSpace(modeName))
        {
            return;
        }

        if (Enum.TryParse<PdfOutputMode>(modeName, ignoreCase: true, out var mode))
        {
            ApplyMode(mode);
        }
    }
}
