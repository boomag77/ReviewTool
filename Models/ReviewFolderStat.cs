namespace ReviewTool.Models;

public readonly record struct ReviewFolderStat
{
    public string[] NotReviewedPages { get; init; }
    public string[] ApprovedPages { get; init; }
    public string[] BadOriginalPages { get; init; }
    public string[] RescanPages { get; init; }
    public string[] SavedPages { get; init; }
    public int[] MissingPages { get; init; }
    public int MaxPageNumber { get; init; }
}
