using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ReviewTool;

public sealed class ImageFileItem : INotifyPropertyChanged
{
    public enum ReviewStatusType
    {
        Pending,
        Approved,
        Rejected,
        Custom
    }

    public enum RejectReasonType
    {
        None,
        BadOriginal,
        Rescan
    }



    public ImageFileItem(string filePath)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        ReviewStatus = ReviewStatusType.Pending;
        RejectReason = RejectReasonType.None;
    }

    public string FilePath { get; }
    public string FileName { get; }

    public string NewFileName
    {
        get => field ?? string.Empty;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value ?? string.Empty;
            OnPropertyChanged();
        }
    }
    public ReviewStatusType ReviewStatus
    {
        get => field;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusCode));
            OnPropertyChanged(nameof(StatusBrush));
        }
    }

    public RejectReasonType RejectReason
    {
        get => field;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusCode));
            OnPropertyChanged(nameof(StatusBrush));
        }
    }
    public string StatusCode =>
        ReviewStatus switch
        {
            ReviewStatusType.Approved => "AC",
            ReviewStatusType.Rejected => RejectReason switch
            {
                RejectReasonType.Rescan => "RS",
                RejectReasonType.BadOriginal => "BO",
                _ => "RE",
            },
            _ => string.Empty,
        };

    public Brush StatusBrush =>
        ReviewStatus switch
        {
            ReviewStatusType.Approved => Brushes.LimeGreen,
            ReviewStatusType.Rejected => Brushes.OrangeRed,
            _ => Brushes.Transparent,
        };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
