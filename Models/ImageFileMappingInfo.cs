namespace ReviewTool.Models
{
    public readonly record struct ImageFileMappingInfo
    {
        public string OriginalName { get; init; }
        public string NewName { get; init; }
        public string ReviewStatus { get; init; }
        public string RejectReason { get; init; }
        public string ReviewDate { get; init; }
        public string? ReviewerName { get; init; }
    }
}
