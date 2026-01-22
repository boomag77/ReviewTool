using System.IO;

namespace ReviewTool;

public sealed class ImageFileItem
{
    public enum ReviewStatusType
    {
        Pending,
        Approved,
        Rejected
    }

    public enum RejectReasonType
    {
        None,
        BadOriginal,
        Overcutted
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
        set => field = value ?? string.Empty;
    }
    public ReviewStatusType ReviewStatus
    {
        get => field;
        set => field = value;
    }

    public RejectReasonType RejectReason
    {
        get => field;
        set => field = value;
    }

}
