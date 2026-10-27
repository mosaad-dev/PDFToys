using Avalonia.Media.Imaging;
using System;
using System.IO;
using System.Windows.Input;

namespace PDFToys.App.ViewModels;

public sealed class PagePreviewViewModel : ViewModelBase, IDisposable
{
    private Bitmap? _previewImage;
    private string _detailsText = string.Empty;
    private string _errorMessage = string.Empty;

    public PagePreviewViewModel(
        string title,
        string detailsText,
        byte[]? imagePngBytes,
        string errorMessage,
        Action closeAction)
    {
        Title = title;
        DetailsText = detailsText;
        ErrorMessage = errorMessage;
        CloseCommand = new DelegateCommand(closeAction);

        if (imagePngBytes is { Length: > 0 })
        {
            using var stream = new MemoryStream(imagePngBytes);
            PreviewImage = new Bitmap(stream);
        }
    }

    public string Title { get; }

    public string DetailsText
    {
        get => _detailsText;
        private set
        {
            if (_detailsText == value)
            {
                return;
            }

            _detailsText = value;
            OnPropertyChanged();
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (_errorMessage == value)
            {
                return;
            }

            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public Bitmap? PreviewImage
    {
        get => _previewImage;
        private set
        {
            if (ReferenceEquals(_previewImage, value))
            {
                return;
            }

            _previewImage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPreviewImage));
        }
    }

    public bool HasPreviewImage => PreviewImage is not null;

    public ICommand CloseCommand { get; }

    public void Dispose() => PreviewImage?.Dispose();

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
