using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ReviewTool;

public partial class CustomFlagsEditorWindow : Window, INotifyPropertyChanged
{
    public static IReadOnlyList<ReviewStatusType> AvailableSelectableStatusTypes { get; } = new[]
    {
        ReviewStatusType.Accepted,
        ReviewStatusType.Rejected
    };

    public ObservableCollection<CustomFlagRow> Rows { get; } = new();
    public IReadOnlyList<ReviewStatus> ResultRequiredStatuses => _resultRequiredStatuses;
    public IReadOnlyList<ReviewStatus> ResultCustomStatuses => _resultCustomStatuses;

    private readonly List<ReviewStatus> _resultRequiredStatuses = new();
    private readonly List<ReviewStatus> _resultCustomStatuses = new();
    private readonly int _maxCustomStatusesCount;
    private bool _canSubmit = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool CanSubmit
    {
        get => _canSubmit;
        private set => SetField(ref _canSubmit, value);
    }

    public CustomFlagsEditorWindow(IEnumerable<ReviewStatus> requiredStatuses,
                                   IEnumerable<ReviewStatus> customStatuses,
                                   int maxCustomStatusesCount)
    {
        InitializeComponent();
        DataContext = this;
        _maxCustomStatusesCount = maxCustomStatusesCount;
        Rows.CollectionChanged += Rows_CollectionChanged;

        foreach (var status in requiredStatuses)
        {
            Rows.Add(CustomFlagRow.FromReviewStatus(status, isRequired: true));
        }

        foreach (var status in customStatuses)
        {
            Rows.Add(CustomFlagRow.FromReviewStatus(status, isRequired: false));
        }

        foreach (var row in Rows)
        {
            row.PropertyChanged += Row_PropertyChanged;
        }

        UpdateValidationState();
    }

    private void AddRow_Click(object sender, RoutedEventArgs e)
    {
        var currentCustomCount = Rows.Count(row => !row.IsRequired);
        if (currentCustomCount >= _maxCustomStatusesCount)
        {
            MessageBox.Show(this,
                            $"Custom statuses limit is {_maxCustomStatusesCount}.",
                            "Custom status",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
            return;
        }

        var newRow = new CustomFlagRow();
        Rows.Add(newRow);
        FlagsDataGrid.SelectedItem = newRow;
        UpdateValidationState();
    }

    private void RemoveRow_Click(object sender, RoutedEventArgs e)
    {
        if (FlagsDataGrid.SelectedItem is not CustomFlagRow selectedRow)
        {
            return;
        }

        if (selectedRow.IsRequired)
        {
            MessageBox.Show(this,
                            "Required flags cannot be removed.",
                            "Custom status",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
            return;
        }

        Rows.Remove(selectedRow);
        UpdateValidationState();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var nextRequiredStatuses = new List<ReviewStatus>(Rows.Count);
        var nextCustomStatuses = new List<ReviewStatus>(Rows.Count);

        foreach (var row in Rows)
        {
            if (row.IsRequired && row.IsEmpty())
            {
                MessageBox.Show(this,
                                "Required flag rows cannot be empty.",
                                "Custom status",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }

            if (row.IsEmpty())
            {
                continue;
            }

            if (!row.TryBuildReviewStatus(out var reviewStatus, out var validationError))
            {
                MessageBox.Show(this,
                                validationError,
                                "Custom status",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }

            if (row.IsRequired)
            {
                nextRequiredStatuses.Add(reviewStatus);
            }
            else
            {
                nextCustomStatuses.Add(reviewStatus);
            }
        }

        if (nextCustomStatuses.Count > _maxCustomStatusesCount)
        {
            MessageBox.Show(this,
                            $"Custom statuses limit is {_maxCustomStatusesCount}.",
                            "Custom status",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
            return;
        }

        _resultRequiredStatuses.Clear();
        _resultCustomStatuses.Clear();
        _resultRequiredStatuses.AddRange(nextRequiredStatuses);
        _resultCustomStatuses.AddRange(nextCustomStatuses);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Rows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var oldItem in e.OldItems)
            {
                if (oldItem is CustomFlagRow oldRow)
                {
                    oldRow.PropertyChanged -= Row_PropertyChanged;
                }
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var newItem in e.NewItems)
            {
                if (newItem is CustomFlagRow newRow)
                {
                    newRow.PropertyChanged += Row_PropertyChanged;
                }
            }
        }

        UpdateValidationState();
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CustomFlagRow.IsAffixRequiredViolation)
            || e.PropertyName == nameof(CustomFlagRow.Prefix)
            || e.PropertyName == nameof(CustomFlagRow.Suffix)
            || e.PropertyName == nameof(CustomFlagRow.Name)
            || e.PropertyName == nameof(CustomFlagRow.StatusType))
        {
            UpdateValidationState();
        }
    }

    private void UpdateValidationState()
    {
        CanSubmit = Rows.All(row => !row.IsAffixRequiredViolation);
    }

    private void HotkeyEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.DataContext is not CustomFlagRow row)
        {
            return;
        }

        var normalizedHotkey = BuildHotkeyStringFrom(e);
        if (normalizedHotkey is null)
        {
            return;
        }

        row.Hotkey = normalizedHotkey;
        textBox.Text = normalizedHotkey;
        e.Handled = true;
    }

    private static string? BuildHotkeyStringFrom(KeyEventArgs e)
    {
        if (e.Key == Key.Tab)
        {
            return null;
        }

        if (e.Key == Key.Back || e.Key == Key.Delete)
        {
            return string.Empty;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!HotkeyHelper.TryBuildHotkeyString(key, Keyboard.Modifiers, out var hotkey))
        {
            return null;
        }

        return hotkey;
    }

    public sealed class CustomFlagRow : INotifyPropertyChanged
    {
        private bool _isRequired;
        private ReviewStatusType _statusType = ReviewStatusType.Rejected;
        private string _name = string.Empty;
        private string _description = string.Empty;
        private string _buttonTitle = string.Empty;
        private string _twoCharCode = string.Empty;
        private string _suffix = string.Empty;
        private string _prefix = string.Empty;
        private string _hotkey = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsRequired
        {
            get => _isRequired;
            set => SetField(ref _isRequired, value);
        }

        public ReviewStatusType StatusType
        {
            get => _statusType;
            set => SetField(ref _statusType, value);
        }

        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        public string Description
        {
            get => _description;
            set => SetField(ref _description, value);
        }

        public string ButtonTitle
        {
            get => _buttonTitle;
            set => SetField(ref _buttonTitle, value);
        }

        public string TwoCharCode
        {
            get => _twoCharCode;
            set => SetField(ref _twoCharCode, value);
        }

        public string Suffix
        {
            get => _suffix;
            set => SetField(ref _suffix, value);
        }

        public string Prefix
        {
            get => _prefix;
            set => SetField(ref _prefix, value);
        }

        public string Hotkey
        {
            get => _hotkey;
            set => SetField(ref _hotkey, value);
        }

        public bool IsAffixRequiredViolation
        {
            get
            {
                if (IsEmpty())
                {
                    return false;
                }

                var hasAffix = !string.IsNullOrWhiteSpace(Suffix) || !string.IsNullOrWhiteSpace(Prefix);
                var isRequiredAccepted = IsRequired && StatusType == ReviewStatusType.Accepted;
                return !hasAffix && !isRequiredAccepted;
            }
        }

        public static CustomFlagRow FromReviewStatus(ReviewStatus reviewStatus, bool isRequired)
        {
            return new CustomFlagRow
            {
                IsRequired = isRequired,
                StatusType = reviewStatus.StatusType,
                Name = reviewStatus.StatusFlag.Name ?? string.Empty,
                Description = reviewStatus.StatusFlag.Description ?? string.Empty,
                ButtonTitle = reviewStatus.StatusFlag.ButtonTitle ?? string.Empty,
                TwoCharCode = reviewStatus.StatusFlag.TwoCharCode ?? string.Empty,
                Suffix = reviewStatus.StatusFlag.Suffix ?? string.Empty,
                Prefix = reviewStatus.StatusFlag.Prefix ?? string.Empty,
                Hotkey = reviewStatus.StatusFlag.Hotkey ?? string.Empty
            };
        }

        public bool IsEmpty()
        {
            return string.IsNullOrWhiteSpace(Name)
                   && string.IsNullOrWhiteSpace(ButtonTitle)
                   && string.IsNullOrWhiteSpace(TwoCharCode)
                   && string.IsNullOrWhiteSpace(Hotkey)
                   && string.IsNullOrWhiteSpace(Suffix)
                   && string.IsNullOrWhiteSpace(Prefix)
                   && string.IsNullOrWhiteSpace(Description);
        }

        public bool TryBuildReviewStatus(out ReviewStatus reviewStatus, out string validationError)
        {
            var normalizedName = Name.Trim();
            var normalizedButtonTitle = ButtonTitle.Trim();
            var normalizedHotkey = HotkeyHelper.NormalizeHotkey(Hotkey);
            var normalizedSuffix = NormalizeOptional(Suffix);
            var normalizedPrefix = NormalizeOptional(Prefix);

            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                reviewStatus = default;
                validationError = "Name is required.";
                return false;
            }

            if (!IsRequired && StatusType == ReviewStatusType.Pending)
            {
                reviewStatus = default;
                validationError = "Pending is technical and cannot be selected for custom UI flags.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(normalizedButtonTitle)
                && string.IsNullOrWhiteSpace(normalizedHotkey))
            {
                reviewStatus = default;
                validationError = $"Hotkey is required when ButtonTitle is set for '{normalizedName}'.";
                return false;
            }

            var hasAffix = !string.IsNullOrWhiteSpace(normalizedSuffix)
                           || !string.IsNullOrWhiteSpace(normalizedPrefix);
            var isRequiredAcceptedFlag = IsRequired && StatusType == ReviewStatusType.Accepted;
            if (!hasAffix && !isRequiredAcceptedFlag)
            {
                reviewStatus = default;
                validationError = $"Flag '{normalizedName}' must have Prefix or Suffix. Only required Accepted flag may have neither.";
                return false;
            }

            reviewStatus = new ReviewStatus
            {
                StatusType = StatusType,
                StatusFlag = new ReviewStatusFlag
                {
                    Name = normalizedName,
                    Description = NormalizeOptional(Description),
                    ButtonTitle = NormalizeOptional(normalizedButtonTitle),
                    TwoCharCode = NormalizeOptional(TwoCharCode),
                    Hotkey = NormalizeOptional(normalizedHotkey),
                    Suffix = normalizedSuffix,
                    Prefix = normalizedPrefix
                }
            };

            validationError = string.Empty;
            return true;
        }

        private static string? NormalizeOptional(string value)
        {
            var normalized = value.Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAffixRequiredViolation)));
        }
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
