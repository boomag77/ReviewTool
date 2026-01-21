using Microsoft.Win32;
using System.Collections.Frozen;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ReviewTool;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public static readonly RoutedCommand NextImageCommand = new();
    public static readonly RoutedCommand PreviousImageCommand = new();

    private readonly FrozenSet<string> ImageExts =
        new[] { ".bmp", ".gif", ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".webp" }
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly CurrentFolderIndex _originalFolderIndex;
    private readonly CurrentFolderIndex _processedFolderIndex;
    private CancellationTokenSource? _originalFolderIndexCts;
    private CancellationTokenSource? _processedFolderIndexCts;
    private int _currentImageIndex;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Keyboard.Focus(this);
        _originalFolderIndex = new CurrentFolderIndex(this);
        _processedFolderIndex = new CurrentFolderIndex(this);
    }

    private async void StartPreview_Click(object sender, RoutedEventArgs e)
    {
        var originalFolder = SelectFolder("Select original images folder");
        if (string.IsNullOrWhiteSpace(originalFolder))
        {
            return;
        }

        var processedFolder = SelectFolder("Select processed images folder");
        if (string.IsNullOrWhiteSpace(processedFolder))
        {
            return;
        }

        await LoadFolderImagesAsync(originalFolder, isOriginal: true);
        await LoadFolderImagesAsync(processedFolder, isOriginal: false);
        FocusWindowForInputDeferred();
    }

    private void NextImageCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        NavigateImages(1);
    }

    private void PreviousImageCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        NavigateImages(-1);
    }

    private void FocusWindowForInput()
    {
        Activate();
        Focus();
        Keyboard.Focus(this);
    }

    private void FocusWindowForInputDeferred()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(FocusWindowForInput));
    }

    private static string? SelectFolder(string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private async Task LoadFolderImagesAsync(string folder, bool isOriginal)
    {
        var index = isOriginal ? _originalFolderIndex : _processedFolderIndex;
        var cts = isOriginal ? _originalFolderIndexCts : _processedFolderIndexCts;
        cts?.Cancel();
        cts = new CancellationTokenSource();
        if (isOriginal)
        {
            _originalFolderIndexCts = cts;
        }
        else
        {
            _processedFolderIndexCts = cts;
        }

        try
        {
            await index.CreateAsync(folder, string.Empty, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (index.LastIndex < 0)
        {
            MessageBox.Show(this, "No supported image files found in the selected folder.", "No Images Found",
                MessageBoxButton.OK, MessageBoxImage.Information);
            UpdatePreviewLabels();
            return;
        }

        _currentImageIndex = 0;
        UpdatePreviewImages();
    }

    private void NavigateImages(int delta)
    {
        var maxIndex = Math.Max(_originalFolderIndex.LastIndex, _processedFolderIndex.LastIndex);
        if (maxIndex < 0)
        {
            return;
        }

        var nextIndex = _currentImageIndex + delta;
        if (nextIndex < 0 || nextIndex > maxIndex)
        {
            return;
        }

        _currentImageIndex = nextIndex;
        UpdatePreviewImages();
    }

    private void UpdatePreviewImages()
    {
        SetImageFromIndex(_originalFolderIndex, OriginalImage);
        SetImageFromIndex(_processedFolderIndex, ProcessedImage);
        UpdatePreviewLabels();
    }

    private void UpdatePreviewLabels()
    {
        OriginalLabel.Text = BuildLabel("Original", _originalFolderIndex);
        ProcessedLabel.Text = BuildLabel("Processed", _processedFolderIndex);
    }

    private string BuildLabel(string name, CurrentFolderIndex index)
    {
        var count = Math.Max(0, index.LastIndex + 1);
        var current = index.TryGetFilePathForIndex(_currentImageIndex, out _) ? _currentImageIndex + 1 : 0;
        return $"{name} {current}/{count}";
    }

    private void SetImageFromIndex(CurrentFolderIndex index, Image target)
    {
        if (!index.TryGetFilePathForIndex(_currentImageIndex, out var imagePath))
        {
            target.Source = null;
            return;
        }

        target.Source = null;
        try
        {
            target.Source = LoadBitmapImage(imagePath);
        }
        catch (IOException ex)
        {
            target.Source = null;
            MessageBox.Show(this, $"Failed to load image:\n{ex.Message}", "Load Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (UnauthorizedAccessException ex)
        {
            target.Source = null;
            MessageBox.Show(this, $"Failed to load image:\n{ex.Message}", "Load Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed class CurrentFolderIndex
    {
        private readonly MainWindow _owner;
        private int _cachedFileIndex = -1;
        private string _cachedDirectory = string.Empty;
        private FrozenDictionary<int, string> _folderIndex = FrozenDictionary<int, string>.Empty;
        private FrozenDictionary<string, int> _folderIndexByPath = FrozenDictionary<string, int>.Empty;

        private readonly object _lock = new object();

        public int CachedFileIndex
        {
            get
            {
                lock (_lock)
                    return _cachedFileIndex;
            }
            set
            {
                lock (_lock)
                    _cachedFileIndex = value;
            }
        }

        public int LastIndex
        {
            get
            {
                var index = _folderIndex;
                return index.Count - 1;
            }
        }

        public string CachedDirectory
        {
            get
            {
                lock (_lock)
                    return _cachedDirectory;
            }
        }

        public CurrentFolderIndex(MainWindow owner)
        {
            _owner = owner;
        }

        public bool TryGetFilePathForIndex(int index, out string filePath)
        {
            if (!_folderIndex.TryGetValue(index, out string? value))
            {
                filePath = string.Empty;
                return false;
            }
            filePath = value;
            Interlocked.Exchange(ref _cachedFileIndex, index);
            return true;
        }

        public bool TryGetFileIndexForPath(string filePath, out int idx)
        {
            if (!_folderIndexByPath.TryGetValue(filePath, out int fileIndex))
            {
                idx = -1;
                return false;
            }
            idx = fileIndex;
            return true;
        }

        private void Clear()
        {
            lock (_lock)
            {
                _folderIndex = FrozenDictionary<int, string>.Empty;
                _folderIndexByPath = FrozenDictionary<string, int>.Empty;
                _cachedFileIndex = -1;
                _cachedDirectory = string.Empty;
            }
        }

        public async Task CreateAsync(string folderPath, string filePath, CancellationToken token)
        {
            Debug.WriteLine("Creating Folder index");
            Clear();

            await Task.Run(() =>
            {
                var files = Directory.EnumerateFiles(folderPath)
                                 .Where(f => _owner.ImageExts.Contains(Path.GetExtension(f)))
                                 .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
                var tmpFolderIndex = new Dictionary<int, string>();
                var tmpFolderIndexByPath = new Dictionary<string, int>();
                int i = 0;
                foreach (var file in files)
                {
                    if (token.IsCancellationRequested) return;
                    tmpFolderIndex[i] = file;
                    tmpFolderIndexByPath[file] = i;
                    i++;
                }
                token.ThrowIfCancellationRequested();

                lock (_lock)
                {
                    _folderIndex = tmpFolderIndex.ToFrozenDictionary();
                    _folderIndexByPath = tmpFolderIndexByPath.ToFrozenDictionary();
                    _cachedDirectory = folderPath;
                    _cachedFileIndex = _folderIndexByPath.TryGetValue(filePath, out var idx) ? idx : -1;
                }

            }, token);
        }
    }

    private static BitmapSource LoadBitmapImage(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var oriented = ApplyExifOrientation(frame);
        oriented.Freeze();
        return oriented;
    }

    private static BitmapSource ApplyExifOrientation(BitmapFrame frame)
    {
        if (frame.Metadata is not BitmapMetadata metadata)
        {
            return frame;
        }

        ushort orientation;
        try
        {
            if (!metadata.ContainsQuery("/app1/ifd/{ushort=274}"))
            {
                return frame;
            }

            var orientationObj = metadata.GetQuery("/app1/ifd/{ushort=274}");
            if (orientationObj is null)
            {
                return frame;
            }

            orientation = Convert.ToUInt16(orientationObj);
        }
        catch
        {
            return frame;
        }

        var width = frame.PixelWidth / 2d;
        var height = frame.PixelHeight / 2d;

        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1, width, height),
            3 => new RotateTransform(180, width, height),
            4 => new ScaleTransform(1, -1, width, height),
            5 => new TransformGroup
            {
                Children =
                {
                    new RotateTransform(90, width, height),
                    new ScaleTransform(-1, 1, width, height),
                },
            },
            6 => new RotateTransform(90, width, height),
            7 => new TransformGroup
            {
                Children =
                {
                    new RotateTransform(270, width, height),
                    new ScaleTransform(-1, 1, width, height),
                },
            },
            8 => new RotateTransform(270, width, height),
            _ => null,
        };

        return transform is null ? frame : new TransformedBitmap(frame, transform);
    }
}
