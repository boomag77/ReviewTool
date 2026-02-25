namespace ReviewTool;

internal sealed class InitialReviewProcessor
{
    private const string RescanFlagName = "Rescan";
    private const string BadOriginalFlagName = "BadOriginal";
    private const string PendingFlagName = "Pending";

    private readonly MainWindowViewModel _viewModel;

    public InitialReviewProcessor(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public ReviewStatus GetAcceptedReviewStatus()
    {
        if (_viewModel.TryGetRequiredReviewStatus(ReviewStatusType.Accepted, out var reviewStatus))
        {
            return reviewStatus;
        }

        return new ReviewStatus
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
    }

    public ReviewStatus GetPendingReviewStatus()
    {
        if (_viewModel.TryGetRequiredReviewStatus(ReviewStatusType.Pending, out var reviewStatus))
        {
            return reviewStatus;
        }

        return new ReviewStatus
        {
            StatusType = ReviewStatusType.Pending,
            StatusFlag = new ReviewStatusFlag
            {
                Name = PendingFlagName,
                Prefix = "_not_reviewed_"
            }
        };
    }

    public ReviewStatus GetRescanReviewStatus()
    {
        if (_viewModel.TryGetReviewStatusByFlagName(RescanFlagName, out var reviewStatus))
        {
            return reviewStatus;
        }

        return new ReviewStatus
        {
            StatusType = ReviewStatusType.Rejected,
            StatusFlag = new ReviewStatusFlag
            {
                Name = RescanFlagName,
                ButtonTitle = "Rescan",
                TwoCharCode = "RS",
                Hotkey = "R",
                Suffix = "rs"
            }
        };
    }

    public ReviewStatus GetBadOriginalReviewStatus()
    {
        if (_viewModel.TryGetReviewStatusByFlagName(BadOriginalFlagName, out var reviewStatus))
        {
            return reviewStatus;
        }

        return new ReviewStatus
        {
            StatusType = ReviewStatusType.Rejected,
            StatusFlag = new ReviewStatusFlag
            {
                Name = BadOriginalFlagName,
                ButtonTitle = "Bad original",
                TwoCharCode = "BO",
                Hotkey = "B",
                Suffix = "bo"
            }
        };
    }

    public void ApplyReviewStatus(ImageFileItem item, ReviewStatus status, string? newName)
    {
        if (newName is not null)
        {
            item.NewFileName = newName;
        }

        item.Status = status;
    }

    public bool IsPending(ImageFileItem item)
    {
        return item.Status.StatusType == ReviewStatusType.Pending;
    }

    public ReviewStatusType ParseStatusTypeFromMapping(string statusValue)
    {
        if (string.IsNullOrWhiteSpace(statusValue))
        {
            return ReviewStatusType.Pending;
        }

        if (string.Equals(statusValue, "Approved", StringComparison.OrdinalIgnoreCase)
            || string.Equals(statusValue, "Accepted", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewStatusType.Accepted;
        }

        if (string.Equals(statusValue, "Rejected", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewStatusType.Rejected;
        }

        if (string.Equals(statusValue, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewStatusType.Pending;
        }

        return ReviewStatusType.Pending;
    }

    public string NormalizeFlagName(string flagName)
    {
        if (string.IsNullOrWhiteSpace(flagName)
            || string.Equals(flagName, "None", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return flagName.Trim();
    }

    public string GetPrefixForStatus(ReviewStatusType statusType, string flagName)
    {
        if (!string.IsNullOrWhiteSpace(flagName)
            && _viewModel.TryGetReviewStatusByFlagName(flagName, out var statusByFlag)
            && !string.IsNullOrWhiteSpace(statusByFlag.StatusFlag.Prefix))
        {
            return NormalizePrefixForFileName(statusByFlag.StatusFlag.Prefix!);
        }

        if (_viewModel.TryGetRequiredReviewStatus(statusType, out var requiredStatus)
            && !string.IsNullOrWhiteSpace(requiredStatus.StatusFlag.Prefix))
        {
            return NormalizePrefixForFileName(requiredStatus.StatusFlag.Prefix!);
        }

        return statusType == ReviewStatusType.Pending ? "_not_reviewed_" : string.Empty;
    }

    public string GetSuffixForStatus(ReviewStatusType statusType, string flagName)
    {
        if (!string.IsNullOrWhiteSpace(flagName)
            && _viewModel.TryGetReviewStatusByFlagName(flagName, out var reviewStatus)
            && !string.IsNullOrWhiteSpace(reviewStatus.StatusFlag.Suffix))
        {
            return NormalizeSuffixForFileName(reviewStatus.StatusFlag.Suffix!);
        }

        if (_viewModel.TryGetRequiredReviewStatus(statusType, out var requiredStatus)
            && !string.IsNullOrWhiteSpace(requiredStatus.StatusFlag.Suffix))
        {
            return NormalizeSuffixForFileName(requiredStatus.StatusFlag.Suffix!);
        }

        if (statusType != ReviewStatusType.Rejected)
        {
            return string.Empty;
        }

        if (string.Equals(flagName, RescanFlagName, StringComparison.OrdinalIgnoreCase))
        {
            return "_rs";
        }

        if (string.Equals(flagName, BadOriginalFlagName, StringComparison.OrdinalIgnoreCase))
        {
            return "_bo";
        }

        return "_rejected";
    }

    public bool IsRescanFlag(string flagName)
    {
        return string.Equals(flagName, RescanFlagName, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePrefixForFileName(string prefix)
    {
        var normalizedToken = NormalizeFileNameAffixToken(prefix);
        if (string.IsNullOrWhiteSpace(normalizedToken))
        {
            return string.Empty;
        }

        return normalizedToken + "_";
    }

    private static string NormalizeSuffixForFileName(string suffix)
    {
        var normalizedToken = NormalizeFileNameAffixToken(suffix);
        if (string.IsNullOrWhiteSpace(normalizedToken))
        {
            return string.Empty;
        }

        return "_" + normalizedToken;
    }

    private static string NormalizeFileNameAffixToken(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return string.Empty;
        }

        var trimmedToken = rawToken.Trim();
        while (trimmedToken.Length > 0 &&
               (trimmedToken[0] == '_' || trimmedToken[0] == '-' || trimmedToken[0] == ' '))
        {
            trimmedToken = trimmedToken[1..];
        }

        while (trimmedToken.Length > 0 &&
               (trimmedToken[^1] == '_' || trimmedToken[^1] == '-' || trimmedToken[^1] == ' '))
        {
            trimmedToken = trimmedToken[..^1];
        }

        return trimmedToken;
    }
}
