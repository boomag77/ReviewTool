using ReviewTool.Models;

namespace ReviewTool.Interfaces
{
    public interface IStatusAffixResolver
    {
        bool TryGetReviewStatusByFlagName(string flagName, out ReviewStatus reviewStatus);
        bool TryGetRequiredReviewStatus(ReviewStatusType statusType, out ReviewStatus reviewStatus);
    }
}
