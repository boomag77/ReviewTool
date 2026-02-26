using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace ReviewTool;

public partial class ReviewThumbnailsWindow : Window
{
    private const double PortraitTileAspectRatio = 68d / 104d;
    private const double MaxLandscapeTileWidthFactor = 1.35d;
    private const double ThumbnailItemHorizontalPaddingPx = 10d;
    public event Action<int>? ThumbnailSelected;
    public event Action<int>? ThumbnailSizeChanged;
    public event Action<int>? ThumbnailMaxSizeChanged;
    private bool _isApplyingThumbnailSize;
    private const int MinThumbnailSizePx = 48;
    private const int MaxThumbnailSizePx = 320;

    public ObservableCollection<ReviewThumbnailItem> Items { get; } = new();
    public ObservableCollection<ReviewThumbnailFilterItem> Filters { get; } = new();
    private ICollectionView? _itemsView;
    private string? _activeFilterFlagName;
    private bool _isClosed;

    public double ThumbnailTileWidth
    {
        get => (double)GetValue(ThumbnailTileWidthProperty);
        set => SetValue(ThumbnailTileWidthProperty, value);
    }

    public static readonly DependencyProperty ThumbnailTileWidthProperty =
        DependencyProperty.Register(nameof(ThumbnailTileWidth), typeof(double), typeof(ReviewThumbnailsWindow), new PropertyMetadata(68d));

    public double ThumbnailTileHeight
    {
        get => (double)GetValue(ThumbnailTileHeightProperty);
        set => SetValue(ThumbnailTileHeightProperty, value);
    }

    public static readonly DependencyProperty ThumbnailTileHeightProperty =
        DependencyProperty.Register(nameof(ThumbnailTileHeight), typeof(double), typeof(ReviewThumbnailsWindow), new PropertyMetadata(104d));

    public double ThumbnailItemWidth
    {
        get => (double)GetValue(ThumbnailItemWidthProperty);
        set => SetValue(ThumbnailItemWidthProperty, value);
    }

    public static readonly DependencyProperty ThumbnailItemWidthProperty =
        DependencyProperty.Register(nameof(ThumbnailItemWidth), typeof(double), typeof(ReviewThumbnailsWindow), new PropertyMetadata(88d));

    public int ThumbnailMaxSize
    {
        get => (int)GetValue(ThumbnailMaxSizeProperty);
        set => SetValue(ThumbnailMaxSizeProperty, value);
    }

    public static readonly DependencyProperty ThumbnailMaxSizeProperty =
        DependencyProperty.Register(nameof(ThumbnailMaxSize), typeof(int), typeof(ReviewThumbnailsWindow), new PropertyMetadata(160));

    public ReviewThumbnailsWindow()
    {
        InitializeComponent();
        ThumbnailsList.ItemsSource = Items;
        FiltersItemsControl.ItemsSource = Filters;
        _itemsView = CollectionViewSource.GetDefaultView(Items);
        _itemsView.Filter = FilterThumbnailItem;
        ApplyThumbnailMetrics((int)Math.Round(ThumbnailSizeSlider.Value, MidpointRounding.AwayFromZero));
        SetThumbnailMaxSize((int)Math.Round(ThumbnailSizeSlider.Maximum, MidpointRounding.AwayFromZero));
    }

    public void SetItems(IEnumerable<ReviewThumbnailItem> items, CancellationToken cancellationToken = default)
    {
        if (_isClosed || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        Items.Clear();
        foreach (var item in items)
        {
            if (_isClosed || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            item.SetPortraitFrameWidth(ThumbnailTileWidth);
            Items.Add(item);
        }

        _itemsView?.Refresh();
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        base.OnClosed(e);
    }

    public void SetFilters(IEnumerable<ReviewThumbnailFilterItem> filters)
    {
        Filters.Clear();
        foreach (var filter in filters)
        {
            Filters.Add(filter);
        }

        if (Filters.Count == 0)
        {
            _activeFilterFlagName = null;
            return;
        }

        SetActiveFilter(Filters[0].FlagName);
    }

    private void ThumbnailsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThumbnailsList.SelectedItem is not ReviewThumbnailItem selectedItem)
        {
            return;
        }

        ThumbnailSelected?.Invoke(selectedItem.Index);
    }

    public void SetThumbnailSize(int thumbnailHeightPx)
    {
        var clampedHeight = Math.Clamp(thumbnailHeightPx, MinThumbnailSizePx, ThumbnailMaxSize);
        ApplyThumbnailMetrics(clampedHeight);

        if (ThumbnailSizeSlider is null)
        {
            return;
        }

        if (Math.Abs(ThumbnailSizeSlider.Value - clampedHeight) < 0.5)
        {
            return;
        }

        _isApplyingThumbnailSize = true;
        ThumbnailSizeSlider.Value = clampedHeight;
        _isApplyingThumbnailSize = false;
    }

    public void SetThumbnailMaxSize(int maxSizePx)
    {
        var clampedMax = Math.Clamp(maxSizePx, MinThumbnailSizePx, MaxThumbnailSizePx);
        ThumbnailMaxSize = clampedMax;

        if (ThumbnailMaxSizeTextBox is not null)
        {
            ThumbnailMaxSizeTextBox.Text = clampedMax.ToString();
        }

        if (ThumbnailSizeSlider is null)
        {
            return;
        }

        _isApplyingThumbnailSize = true;
        ThumbnailSizeSlider.Maximum = clampedMax;
        if (ThumbnailSizeSlider.Value > clampedMax)
        {
            ThumbnailSizeSlider.Value = clampedMax;
        }
        _isApplyingThumbnailSize = false;
    }

    private void ThumbnailSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isApplyingThumbnailSize)
        {
            return;
        }

        var selectedHeight = Math.Clamp((int)Math.Round(e.NewValue, MidpointRounding.AwayFromZero),
                                        MinThumbnailSizePx,
                                        ThumbnailMaxSize);
        ApplyThumbnailMetrics(selectedHeight);
        ThumbnailSizeChanged?.Invoke(selectedHeight);
    }

    private void ThumbnailMaxSizeTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        ApplyThumbnailMaxSizeFromTextInput();
    }

    private void ThumbnailMaxSizeTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter)
        {
            return;
        }

        ApplyThumbnailMaxSizeFromTextInput();
        e.Handled = true;
    }

    private void ApplyThumbnailMaxSizeFromTextInput()
    {
        if (ThumbnailMaxSizeTextBox is null)
        {
            return;
        }

        if (!int.TryParse(ThumbnailMaxSizeTextBox.Text, out var maxSizeFromText))
        {
            SetThumbnailMaxSize(ThumbnailMaxSize);
            return;
        }

        SetThumbnailMaxSize(maxSizeFromText);
        if (ThumbnailSizeSlider is not null && ThumbnailSizeSlider.Value > ThumbnailMaxSize)
        {
            SetThumbnailSize(ThumbnailMaxSize);
            ThumbnailSizeChanged?.Invoke(ThumbnailMaxSize);
        }

        ThumbnailMaxSizeChanged?.Invoke(ThumbnailMaxSize);
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tagValue)
        {
            return;
        }

        var filterFlagName = string.Equals(tagValue, "__all__", StringComparison.Ordinal)
            ? null
            : tagValue;
        SetActiveFilter(filterFlagName);
    }

    private void SetActiveFilter(string? filterFlagName)
    {
        _activeFilterFlagName = filterFlagName;
        foreach (var filter in Filters)
        {
            var isActive = string.IsNullOrWhiteSpace(_activeFilterFlagName)
                ? string.IsNullOrWhiteSpace(filter.FlagName)
                : string.Equals(filter.FlagName, _activeFilterFlagName, StringComparison.OrdinalIgnoreCase);
            filter.IsActive = isActive;
        }

        _itemsView?.Refresh();
    }

    private bool FilterThumbnailItem(object obj)
    {
        if (obj is not ReviewThumbnailItem item)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_activeFilterFlagName))
        {
            return true;
        }

        return string.Equals(item.FlagName, _activeFilterFlagName, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyThumbnailMetrics(int thumbnailHeightPx)
    {
        ThumbnailTileHeight = thumbnailHeightPx;
        ThumbnailTileWidth = Math.Round(thumbnailHeightPx * PortraitTileAspectRatio, MidpointRounding.AwayFromZero);
        ThumbnailItemWidth = Math.Round((ThumbnailTileWidth * MaxLandscapeTileWidthFactor) + ThumbnailItemHorizontalPaddingPx,
            MidpointRounding.AwayFromZero);
        foreach (var item in Items)
        {
            item.SetPortraitFrameWidth(ThumbnailTileWidth);
        }
    }
}

public sealed class ReviewThumbnailItem : INotifyPropertyChanged
{
    private const double LandscapeRatioThreshold = 1.15d;
    private const double MaxLandscapeWidthFactor = 1.35d;

    public int Index { get; init; }
    public string Label { get; init; } = string.Empty;
    private BitmapSource? _thumbnail;
    private double _portraitFrameWidth = 68d;
    private double _thumbnailFrameWidth = 68d;
    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (SetField(ref _thumbnail, value))
            {
                UpdateThumbnailFrameWidth();
            }
        }
    }

    public double ThumbnailFrameWidth
    {
        get => _thumbnailFrameWidth;
        private set => SetField(ref _thumbnailFrameWidth, value);
    }

    public string FilePath { get; init; } = string.Empty;
    public string? FlagName { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetPortraitFrameWidth(double portraitFrameWidth)
    {
        if (portraitFrameWidth <= 0)
        {
            return;
        }

        _portraitFrameWidth = portraitFrameWidth;
        UpdateThumbnailFrameWidth();
    }

    private void UpdateThumbnailFrameWidth()
    {
        var widthFactor = 1d;
        if (_thumbnail is not null && _thumbnail.PixelWidth > 0 && _thumbnail.PixelHeight > 0)
        {
            var aspectRatio = _thumbnail.PixelWidth / (double)_thumbnail.PixelHeight;
            if (aspectRatio > LandscapeRatioThreshold)
            {
                widthFactor = Math.Min(MaxLandscapeWidthFactor, aspectRatio / LandscapeRatioThreshold);
            }
        }

        ThumbnailFrameWidth = Math.Round(_portraitFrameWidth * widthFactor, MidpointRounding.AwayFromZero);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class ReviewThumbnailFilterItem : INotifyPropertyChanged
{
    private bool _isActive;
    public string? FlagName { get; init; }
    public string ButtonLabel { get; init; } = string.Empty;
    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }

    public string ButtonTag => string.IsNullOrWhiteSpace(FlagName) ? "__all__" : FlagName;

    public event PropertyChangedEventHandler? PropertyChanged;

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
