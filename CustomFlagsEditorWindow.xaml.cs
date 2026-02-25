using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ReviewTool;

public partial class CustomFlagsEditorWindow : Window
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

    public CustomFlagsEditorWindow(IEnumerable<ReviewStatus> requiredStatuses,
                                   IEnumerable<ReviewStatus> customStatuses,
                                   int maxCustomStatusesCount)
    {
        InitializeComponent();
        DataContext = this;
        _maxCustomStatusesCount = maxCustomStatusesCount;

        foreach (var status in requiredStatuses)
        {
            Rows.Add(CustomFlagRow.FromReviewStatus(status, isRequired: true));
        }

        foreach (var status in customStatuses)
        {
            Rows.Add(CustomFlagRow.FromReviewStatus(status, isRequired: false));
        }
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

    public sealed class CustomFlagRow
    {
        public bool IsRequired { get; set; }
        public ReviewStatusType StatusType { get; set; } = ReviewStatusType.Rejected;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ButtonTitle { get; set; } = string.Empty;
        public string TwoCharCode { get; set; } = string.Empty;
        public string Suffix { get; set; } = string.Empty;
        public string Prefix { get; set; } = string.Empty;
        public string Hotkey { get; set; } = string.Empty;

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
                    Suffix = NormalizeOptional(Suffix),
                    Prefix = NormalizeOptional(Prefix)
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
    }
}
