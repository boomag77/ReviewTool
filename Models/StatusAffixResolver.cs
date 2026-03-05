using ReviewTool.Interfaces;

namespace ReviewTool.Models
{
    public class StatusAffixResolver : IStatusAffixResolver
    {
        private readonly List<ReviewStatus> _requiredReviewStatuses = new List<ReviewStatus>();
        private readonly List<ReviewStatus> _customReviewStatuses = new List<ReviewStatus>();

        public StatusAffixResolver(IReadOnlyList<ReviewStatus> requiredReviewStatuses, IReadOnlyList<ReviewStatus> customReviewStatuses)
        {
            _requiredReviewStatuses = requiredReviewStatuses.ToList();
            _customReviewStatuses = customReviewStatuses.ToList();
        }

        public bool TryGetRequiredReviewStatus(ReviewStatusType statusType, out ReviewStatus reviewStatus)
        {
            foreach (var status in _requiredReviewStatuses)
            {
                if (status.StatusType == statusType)
                {
                    reviewStatus = status;
                    return true;
                }
            }

            reviewStatus = default;
            return false;
        }
        public bool TryGetReviewStatusByFlagName(string flagName, out ReviewStatus reviewStatus)
        {
            if (string.IsNullOrWhiteSpace(flagName))
            {
                reviewStatus = default;
                return false;
            }

            foreach (var status in _requiredReviewStatuses)
            {
                if (string.Equals(status.StatusFlag.Name, flagName, StringComparison.OrdinalIgnoreCase))
                {
                    reviewStatus = status;
                    return true;
                }
            }

            foreach (var status in _customReviewStatuses)
            {
                if (string.Equals(status.StatusFlag.Name, flagName, StringComparison.OrdinalIgnoreCase))
                {
                    reviewStatus = status;
                    return true;
                }
            }

            reviewStatus = default;
            return false;
        }
    }
}
