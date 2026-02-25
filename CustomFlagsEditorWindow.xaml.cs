using System.Collections.ObjectModel;
using System.Windows;

namespace ReviewTool;

public partial class CustomFlagsEditorWindow : Window
{
    public static IReadOnlyList<ReviewStatusType> AvailableStatusTypes { get; } = new[]
    {
        ReviewStatusType.Accepted,
        ReviewStatusType.Rejected
    };

    public ObservableCollection<CustomFlagRow> Rows { get; } = new();
    public IReadOnlyList<ReviewStatus> ResultStatuses => _resultStatuses;

    private readonly List<ReviewStatus> _resultStatuses = new();

    public CustomFlagsEditorWindow(IEnumerable<ReviewStatus> currentStatuses)
    {
        InitializeComponent();
        DataContext = this;

        foreach (var status in currentStatuses)
        {
            Rows.Add(CustomFlagRow.FromReviewStatus(status));
        }
    }

    private void AddRow_Click(object sender, RoutedEventArgs e)
    {
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

        Rows.Remove(selectedRow);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var nextStatuses = new List<ReviewStatus>(Rows.Count);
        foreach (var row in Rows)
        {
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

            nextStatuses.Add(reviewStatus);
        }

        _resultStatuses.Clear();
        _resultStatuses.AddRange(nextStatuses);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    public sealed class CustomFlagRow
    {
        public ReviewStatusType StatusType { get; set; } = ReviewStatusType.Rejected;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ButtonTitle { get; set; } = string.Empty;
        public string TwoCharCode { get; set; } = string.Empty;
        public string Suffix { get; set; } = string.Empty;
        public string Prefix { get; set; } = string.Empty;
        public string Hotkey { get; set; } = string.Empty;

        public static CustomFlagRow FromReviewStatus(ReviewStatus reviewStatus)
        {
            return new CustomFlagRow
            {
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

            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                reviewStatus = default;
                validationError = "Name is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(normalizedButtonTitle))
            {
                reviewStatus = default;
                validationError = $"ButtonTitle is required for '{normalizedName}'.";
                return false;
            }

            reviewStatus = new ReviewStatus
            {
                StatusType = StatusType,
                StatusFlag = new ReviewStatusFlag
                {
                    Name = normalizedName,
                    Description = NormalizeOptional(Description),
                    ButtonTitle = normalizedButtonTitle,
                    TwoCharCode = NormalizeOptional(TwoCharCode),
                    Hotkey = NormalizeOptional(Hotkey),
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
