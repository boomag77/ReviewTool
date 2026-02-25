using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace ReviewTool;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private const int MaxCustomStatusesCount = 5;

    private BitmapSource? _originalImagePreview;
    private BitmapSource? _reviewingImagePreview;
    private string _originalImageLabel = "Original 0/0";
    private string _reviewingImageLabel = "Processed 0/0";
    private bool _isInitialReview;
    private bool _isFinalReview;
    private ObservableCollection<ImageFileItem> _originalFiles = new();
    private ImageFileItem? _selectedOriginalFile;
    private string _suggestedNumberLabel = string.Empty;
    private string _initialReviewButtonText = "Start Initial Review";
    private string _finalReviewButtonText = "Start Final Review";
    private string _performMappingButtonText = "Perform mapping";
    private bool _isAutoFillEnabled;
    private string _targetFolderDisplayPath = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    private HashSet<string> _statusFlagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _statusFlagTwoCharCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _statusFlagHotkeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _statusFlagSuffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _statusFlagPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _statusFlagButtonTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private List<ReviewStatus> _requiredReviewStatuses = new List<ReviewStatus>
    {
        new ReviewStatus
        {
            StatusType = ReviewStatusType.Accepted,
            StatusFlag = new ReviewStatusFlag
            {
                Name = "Accepted",
                ButtonTitle = "Accept",
                TwoCharCode = "AC",
                Hotkey = "Enter"
            }
        },
        new ReviewStatus
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
        },
        new ReviewStatus
        {
            StatusType = ReviewStatusType.Pending,
            StatusFlag = new ReviewStatusFlag
            {
                Name = "Pending",
                Prefix = "_not_reviewed_"
            }
        }
    };

    private readonly ObservableCollection<ReviewStatus> _statusButtons = new();
    private readonly ObservableCollection<ReviewStatus> _customReviewStatuses = new();
    public ReadOnlyObservableCollection<ReviewStatus> StatusButtons { get; }
    public IReadOnlyList<ReviewStatus> RequiredReviewStatuses => _requiredReviewStatuses;
    public ReadOnlyObservableCollection<ReviewStatus> CustomReviewStatuses { get; }
    public int MaxCustomStatuses => MaxCustomStatusesCount;


    public MainWindowViewModel()
    {
        StatusButtons = new ReadOnlyObservableCollection<ReviewStatus>(_statusButtons);
        CustomReviewStatuses = new ReadOnlyObservableCollection<ReviewStatus>(_customReviewStatuses);

        InitializeReviewStatuses();
        RebuildStatusButtons();
    }

    private static bool HasButtonTitle(ReviewStatus status)
    {
        return !string.IsNullOrWhiteSpace(status.StatusFlag.ButtonTitle);
    }

    private void RebuildStatusButtons()
    {
        _statusButtons.Clear();

        foreach (var status in _requiredReviewStatuses)
        {
            if (HasButtonTitle(status))
            {
                _statusButtons.Add(status);
            }
        }

        foreach (var status in _customReviewStatuses)
        {
            if (HasButtonTitle(status))
            {
                _statusButtons.Add(status);
            }
        }
    }

    private void InitializeReviewStatuses()
    {
        _statusFlagNames.Clear();
        _statusFlagTwoCharCodes.Clear();
        _statusFlagHotkeys.Clear();
        _statusFlagSuffixes.Clear();
        _statusFlagPrefixes.Clear();
        _statusFlagButtonTitles.Clear();

        foreach (var status in _requiredReviewStatuses)
        {
            if (!string.IsNullOrEmpty(status.StatusFlag.Name))
            {
                _statusFlagNames.Add(status.StatusFlag.Name);
            }
            if (!string.IsNullOrEmpty(status.StatusFlag.TwoCharCode))
            {
                _statusFlagTwoCharCodes.Add(status.StatusFlag.TwoCharCode);
            }
            var normalizedHotkey = HotkeyHelper.NormalizeHotkey(status.StatusFlag.Hotkey);
            if (!string.IsNullOrEmpty(normalizedHotkey))
            {
                _statusFlagHotkeys.Add(normalizedHotkey);
            }
            if (!string.IsNullOrEmpty(status.StatusFlag.Suffix))
            {
                _statusFlagSuffixes.Add(status.StatusFlag.Suffix);
            }
            if (!string.IsNullOrEmpty(status.StatusFlag.Prefix))
            {
                _statusFlagPrefixes.Add(status.StatusFlag.Prefix);
            }
            if (!string.IsNullOrEmpty(status.StatusFlag.ButtonTitle))
                _statusFlagButtonTitles.Add(status.StatusFlag.ButtonTitle);
        }
    }

    private bool IsDuplicateStatusFlag(ReviewStatusFlag flag)
    {
        var normalizedHotkey = HotkeyHelper.NormalizeHotkey(flag.Hotkey);
        return (!string.IsNullOrEmpty(flag.Name) && _statusFlagNames.Contains(flag.Name)) ||
               (!string.IsNullOrEmpty(flag.TwoCharCode) && _statusFlagTwoCharCodes.Contains(flag.TwoCharCode)) ||
               (!string.IsNullOrEmpty(normalizedHotkey) && _statusFlagHotkeys.Contains(normalizedHotkey)) ||
               (!string.IsNullOrEmpty(flag.Suffix) && _statusFlagSuffixes.Contains(flag.Suffix)) ||
               (!string.IsNullOrEmpty(flag.Prefix) && _statusFlagPrefixes.Contains(flag.Prefix)) ||
               (!string.IsNullOrEmpty(flag.ButtonTitle) && _statusFlagButtonTitles.Contains(flag.ButtonTitle));
    }
    private void FillStatusFlagCollections(ReviewStatusFlag flag)
    {
        if (!string.IsNullOrEmpty(flag.Name))
        {
            _statusFlagNames.Add(flag.Name);
        }
        if (!string.IsNullOrEmpty(flag.TwoCharCode))
        {
            _statusFlagTwoCharCodes.Add(flag.TwoCharCode);
        }
        var normalizedHotkey = HotkeyHelper.NormalizeHotkey(flag.Hotkey);
        if (!string.IsNullOrEmpty(normalizedHotkey))
        {
            _statusFlagHotkeys.Add(normalizedHotkey);
        }
        if (!string.IsNullOrEmpty(flag.Suffix))
        {
            _statusFlagSuffixes.Add(flag.Suffix);
        }
        if (!string.IsNullOrEmpty(flag.Prefix))
        {
            _statusFlagPrefixes.Add(flag.Prefix);
        }
        if (!string.IsNullOrEmpty(flag.ButtonTitle))
        {
            _statusFlagButtonTitles.Add(flag.ButtonTitle);
        }
    }
    private void RemoveStatusFlagFromCollections(ReviewStatusFlag flag)
    {
        if (!string.IsNullOrEmpty(flag.Name))
        {
            _statusFlagNames.Remove(flag.Name);
        }
        if (!string.IsNullOrEmpty(flag.TwoCharCode))
        {
            _statusFlagTwoCharCodes.Remove(flag.TwoCharCode);
        }
        var normalizedHotkey = HotkeyHelper.NormalizeHotkey(flag.Hotkey);
        if (!string.IsNullOrEmpty(normalizedHotkey))
        {
            _statusFlagHotkeys.Remove(normalizedHotkey);
        }
        if (!string.IsNullOrEmpty(flag.Suffix))
        {
            _statusFlagSuffixes.Remove(flag.Suffix);
        }
        if (!string.IsNullOrEmpty(flag.Prefix))
        {
            _statusFlagPrefixes.Remove(flag.Prefix);
        }
        if (!string.IsNullOrEmpty(flag.ButtonTitle))
        {
            _statusFlagButtonTitles.Remove(flag.ButtonTitle);
        }
    }

    public bool TryAddCustomReviewStatus(ReviewStatus reviewStatus)
    {
        if (_customReviewStatuses.Count >= MaxCustomStatusesCount || IsDuplicateStatusFlag(reviewStatus.StatusFlag))
        {
            return false;
        }

        FillStatusFlagCollections(reviewStatus.StatusFlag);
        _customReviewStatuses.Add(reviewStatus);
        RebuildStatusButtons();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CustomReviewStatuses)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusButtons)));
        return true;
    }

        public bool RemoveCustomReviewStatus(ReviewStatus reviewStatus)
        {
            if (_customReviewStatuses.Count <= 0)
            {
                return false;
            }
            bool ok = _customReviewStatuses.Remove(reviewStatus);
            if (ok)
            {
                RemoveStatusFlagFromCollections(reviewStatus.StatusFlag);
                RebuildStatusButtons();
            }
            
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CustomReviewStatuses)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusButtons)));
        return ok;
    }

    public bool TryReplaceCustomReviewStatuses(IReadOnlyList<ReviewStatus> reviewStatuses, out string validationError)
    {
        return TryApplyReviewStatuses(_requiredReviewStatuses, reviewStatuses, out validationError);
    }

    public bool TryApplyReviewStatuses(IReadOnlyList<ReviewStatus> requiredStatuses,
                                       IReadOnlyList<ReviewStatus> customStatuses,
                                       out string validationError)
    {
        validationError = string.Empty;

        if (requiredStatuses is null || requiredStatuses.Count == 0)
        {
            validationError = "Required statuses list cannot be empty.";
            return false;
        }

        if (customStatuses.Count > MaxCustomStatusesCount)
        {
            validationError = $"Custom statuses limit is {MaxCustomStatusesCount}.";
            return false;
        }

        var nextNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextHotkeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextSuffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextButtonTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool TryFillSnapshotFlagCollections(ReviewStatusFlag flag)
        {
            if (!string.IsNullOrEmpty(flag.Name))
            {
                if (!nextNames.Add(flag.Name))
                {
                    return false;
                }
            }
            if (!string.IsNullOrEmpty(flag.TwoCharCode))
            {
                if (!nextCodes.Add(flag.TwoCharCode))
                {
                    return false;
                }
            }
            var normalizedHotkey = HotkeyHelper.NormalizeHotkey(flag.Hotkey);
            if (!string.IsNullOrEmpty(normalizedHotkey))
            {
                if (!nextHotkeys.Add(normalizedHotkey))
                {
                    return false;
                }
            }
            if (!string.IsNullOrEmpty(flag.Suffix))
            {
                if (!nextSuffixes.Add(flag.Suffix))
                {
                    return false;
                }
            }
            if (!string.IsNullOrEmpty(flag.Prefix))
            {
                if (!nextPrefixes.Add(flag.Prefix))
                {
                    return false;
                }
            }
            if (!string.IsNullOrEmpty(flag.ButtonTitle))
            {
                if (!nextButtonTitles.Add(flag.ButtonTitle))
                {
                    return false;
                }
            }

            return true;
        }

        foreach (var requiredStatus in requiredStatuses)
        {
            if (!TryFillSnapshotFlagCollections(requiredStatus.StatusFlag))
            {
                var statusName = string.IsNullOrWhiteSpace(requiredStatus.StatusFlag.Name) ? "(unnamed)" : requiredStatus.StatusFlag.Name;
                validationError = $"Duplicate required status flag values for '{statusName}'.";
                return false;
            }
        }

        foreach (var reviewStatus in customStatuses)
        {
            var flag = reviewStatus.StatusFlag;

            if (!TryFillSnapshotFlagCollections(flag))
            {
                var statusName = string.IsNullOrWhiteSpace(flag.Name) ? "(unnamed)" : flag.Name;
                validationError = $"Duplicate custom status flag values for '{statusName}'.";
                return false;
            }
        }

        _requiredReviewStatuses = requiredStatuses.ToList();

        _customReviewStatuses.Clear();
        foreach (var reviewStatus in customStatuses)
        {
            _customReviewStatuses.Add(reviewStatus);
        }

        _statusFlagNames = nextNames;
        _statusFlagTwoCharCodes = nextCodes;
        _statusFlagHotkeys = nextHotkeys;
        _statusFlagSuffixes = nextSuffixes;
        _statusFlagPrefixes = nextPrefixes;
        _statusFlagButtonTitles = nextButtonTitles;

        RebuildStatusButtons();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CustomReviewStatuses)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusButtons)));
        return true;
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

    public bool IsFinalReview
    {
        get => _isFinalReview;
        set => SetField(ref _isFinalReview, value);
    }

    public bool IsAutoFillEnabled
    {
        get => _isAutoFillEnabled;
        set => SetField(ref _isAutoFillEnabled, value);
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

    public string PerformMappingButtonText
    {
        get => _performMappingButtonText;
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

    public string TargetFolderDisplayPath
    {
        get => _targetFolderDisplayPath;
        set => SetField(ref _targetFolderDisplayPath, value ?? string.Empty);
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
