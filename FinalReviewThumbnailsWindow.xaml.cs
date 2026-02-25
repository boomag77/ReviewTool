using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace ReviewTool;

public partial class FinalReviewThumbnailsWindow : Window
{
    public event Action<int>? ThumbnailSelected;
    public event Action<int>? ThumbnailSizeChanged;
    public event Action<int>? ThumbnailMaxSizeChanged;
    private bool _isApplyingThumbnailSize;
    private const int MinThumbnailSizePx = 48;
    private const int MaxThumbnailSizePx = 320;

    public ObservableCollection<FinalReviewThumbnailItem> Items { get; } = new();

    public double ThumbnailTileWidth
    {
        get => (double)GetValue(ThumbnailTileWidthProperty);
        set => SetValue(ThumbnailTileWidthProperty, value);
    }

    public static readonly DependencyProperty ThumbnailTileWidthProperty =
        DependencyProperty.Register(nameof(ThumbnailTileWidth), typeof(double), typeof(FinalReviewThumbnailsWindow), new PropertyMetadata(68d));

    public double ThumbnailTileHeight
    {
        get => (double)GetValue(ThumbnailTileHeightProperty);
        set => SetValue(ThumbnailTileHeightProperty, value);
    }

    public static readonly DependencyProperty ThumbnailTileHeightProperty =
        DependencyProperty.Register(nameof(ThumbnailTileHeight), typeof(double), typeof(FinalReviewThumbnailsWindow), new PropertyMetadata(104d));

    public double ThumbnailItemWidth
    {
        get => (double)GetValue(ThumbnailItemWidthProperty);
        set => SetValue(ThumbnailItemWidthProperty, value);
    }

    public static readonly DependencyProperty ThumbnailItemWidthProperty =
        DependencyProperty.Register(nameof(ThumbnailItemWidth), typeof(double), typeof(FinalReviewThumbnailsWindow), new PropertyMetadata(88d));

    public int ThumbnailMaxSize
    {
        get => (int)GetValue(ThumbnailMaxSizeProperty);
        set => SetValue(ThumbnailMaxSizeProperty, value);
    }

    public static readonly DependencyProperty ThumbnailMaxSizeProperty =
        DependencyProperty.Register(nameof(ThumbnailMaxSize), typeof(int), typeof(FinalReviewThumbnailsWindow), new PropertyMetadata(160));

    public FinalReviewThumbnailsWindow()
    {
        InitializeComponent();
        ThumbnailsList.ItemsSource = Items;
        ApplyThumbnailMetrics((int)Math.Round(ThumbnailSizeSlider.Value, MidpointRounding.AwayFromZero));
        SetThumbnailMaxSize((int)Math.Round(ThumbnailSizeSlider.Maximum, MidpointRounding.AwayFromZero));
    }

    public void SetItems(IEnumerable<FinalReviewThumbnailItem> items)
    {
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }
    }

    private void ThumbnailsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThumbnailsList.SelectedItem is not FinalReviewThumbnailItem selectedItem)
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

    private void ApplyThumbnailMetrics(int thumbnailHeightPx)
    {
        ThumbnailTileHeight = thumbnailHeightPx;
        ThumbnailTileWidth = Math.Round(thumbnailHeightPx * 68d / 104d, MidpointRounding.AwayFromZero);
        ThumbnailItemWidth = ThumbnailTileWidth + 20d;
    }
}

public sealed class FinalReviewThumbnailItem : INotifyPropertyChanged
{
    public int Index { get; init; }
    public string Label { get; init; } = string.Empty;
    private BitmapSource? _thumbnail;
    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set => SetField(ref _thumbnail, value);
    }
    public string FilePath { get; init; } = string.Empty;

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
