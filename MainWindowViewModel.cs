using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace ReviewTool;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private BitmapSource? _originalImagePreview;
    private BitmapSource? _reviewingImagePreview;
    private string _originalImageLabel = "Original 0/0";
    private string _reviewingImageLabel = "Processed 0/0";
    private bool _isInitialReview;
    private ObservableCollection<ImageFileItem> _originalFiles = new();
    private ImageFileItem? _selectedOriginalFile;
    private string _suggestedNumberLabel = string.Empty;
    private string _initialReviewButtonText = "Start Initial Review...";
    private string _finalReviewButtonText = "Start Final Review...";

    public event PropertyChangedEventHandler? PropertyChanged;

    public BitmapSource? OriginalImagePreview
    {
        get => _originalImagePreview;
        set => SetField(ref _originalImagePreview, value);
    }

    public BitmapSource? ReviewingImagePreview
    {
        get => _reviewingImagePreview;
        set => SetField(ref _reviewingImagePreview, value);
    }

    public string OriginalImageLabel
    {
        get => _originalImageLabel;
        set => SetField(ref _originalImageLabel, value);
    }

    public string ReviewingImageLabel
    {
        get => _reviewingImageLabel;
        set => SetField(ref _reviewingImageLabel, value);
    }

    public bool IsInitialReview
    {
        get => _isInitialReview;
        set => SetField(ref _isInitialReview, value);
    }

    public ObservableCollection<ImageFileItem> OriginalFiles
    {
        get => _originalFiles;
        set => SetField(ref _originalFiles, value);
    }

    public ImageFileItem? SelectedOriginalFile
    {
        get => _selectedOriginalFile;
        set => SetField(ref _selectedOriginalFile, value);
    }

    public string InitialReviewButtonText
    {
        get => _initialReviewButtonText;
        set => SetField(ref _initialReviewButtonText, value);
    }

    public string FinalReviewButtonText
    {
        get => _finalReviewButtonText;
        set => SetField(ref _finalReviewButtonText, value);
    }

    public string SuggestedNumberLabel
    {
        get => _suggestedNumberLabel;
        set => SetField(ref _suggestedNumberLabel, value);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
