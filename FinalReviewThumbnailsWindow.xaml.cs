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

    public ObservableCollection<FinalReviewThumbnailItem> Items { get; } = new();

    public FinalReviewThumbnailsWindow()
    {
        InitializeComponent();
        ThumbnailsList.ItemsSource = Items;
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
