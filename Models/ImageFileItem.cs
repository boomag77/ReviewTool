using System.Collections.Frozen;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ReviewTool.Models;

public struct ReviewStatusFlag
{
    public string Name { get; init; }
    public string? Description { get; init; }
    public string? ButtonTitle { get; init; }
    public string? TwoCharCode { get; init; }
    public string? Suffix { get; init; }
    public string? Prefix { get; init; }
    public string? Hotkey { get; init; }
}

public struct ReviewStatus
{
    public ReviewStatusType StatusType { get; set; }
    public ReviewStatusFlag StatusFlag { get; set; }

}

public enum ReviewStatusType
{
    Pending,
    Accepted,
    Rejected
}

public sealed class ImageFileItem : INotifyPropertyChanged
{
    ReviewStatus Accepted = new ReviewStatus
    {
        StatusType = ReviewStatusType.Accepted,
        StatusFlag = new ReviewStatusFlag
        {
            Name = "Accepted",
            ButtonTitle = "Accept",
            TwoCharCode = "AC",
            Hotkey = "Enter"
        }
    };
    ReviewStatus Rejected = new ReviewStatus
    {
        StatusType = ReviewStatusType.Rejected,
        StatusFlag = new ReviewStatusFlag
        {
            Name = "Rescan",
            ButtonTitle = "Rescan",
            TwoCharCode = "RS",
            Hotkey = "R",
            Suffix = "rs"
        }
    };
    ReviewStatus Pending = new ReviewStatus
    {
        StatusType = ReviewStatusType.Pending,
        StatusFlag = new ReviewStatusFlag
        {
           Prefix = "_not_reviewed_"
        }
    };

    


    List<ReviewStatusFlag> flags = new List<ReviewStatusFlag>(5); 

   


    public ImageFileItem(string filePath)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        Status = new ReviewStatus
        {
            StatusType = ReviewStatusType.Pending
        };
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
    public ReviewStatus Status
    {
        get => field;
        set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusCode));
            OnPropertyChanged(nameof(StatusBrush));
        }
    }

    public string StatusCode => Status.StatusFlag.TwoCharCode ?? string.Empty;

    public Brush StatusBrush =>
        Status.StatusType switch
        {
            ReviewStatusType.Accepted => Brushes.LimeGreen,
            ReviewStatusType.Rejected => Brushes.OrangeRed,
            _ => Brushes.Transparent,
        };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
