using Microsoft.Win32;
using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ReviewTool;

public partial class MainWindow : Window
{
    public static readonly RoutedCommand NextImageCommand = new();
    public static readonly RoutedCommand PreviousImageCommand = new();
    public static readonly RoutedCommand BadOriginalCommand = new();
    public static readonly RoutedCommand OvercuttedCommand = new();

    private readonly MainWindowViewModel _viewModel = new();
    private readonly FileProcessor _fileProcessor = new();

    private readonly CurrentFolderIndex _originalFolderIndex;
    private readonly CurrentFolderIndex _processedFolderIndex;
    private CancellationTokenSource? _originalFolderIndexCts;
    private CancellationTokenSource? _processedFolderIndexCts;
    private int _currentImageIndex;
    private bool _isInitialReview;
    private bool _isFinalReview;
    private string? _initialReviewFolder;
    private bool _suppressFileSelection;


    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += (_, _) => Keyboard.Focus(this);
        _originalFolderIndex = new CurrentFolderIndex(this);
        _processedFolderIndex = new CurrentFolderIndex(this);

    }

    private async void StartFinalReview_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await StartFinalReviewAsync();
        }
        catch (OperationCanceledException)
        {

        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"An error occurred:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task StartFinalReviewAsync()
    {
        if (_isFinalReview)
        {
            FinishFinalReview();
            return;
        }

        _isInitialReview = false;
        _isFinalReview = true;
        _initialReviewFolder = null;
        _viewModel.IsInitialReview = false;
        _viewModel.OriginalFiles = new ObservableCollection<ImageFileItem>();
        _viewModel.SelectedOriginalFile = null;
        _viewModel.FinalReviewButtonText = "Finish Final Review";
        var originalFolder = SelectFolder("Select original images folder");
        if (string.IsNullOrWhiteSpace(originalFolder))
        {
            _isFinalReview = false;
            _viewModel.FinalReviewButtonText = "Start Final Review...";
            return;
        }

        var processedFolder = SelectFolder("Select processed images folder");
        if (string.IsNullOrWhiteSpace(processedFolder))
        {
            _isFinalReview = false;
            _viewModel.FinalReviewButtonText = "Start Final Review...";
            return;
        }
        await Task.WhenAll(
                BuildFoldersIndexesAsync(originalFolder, isOriginal: true),
                BuildFoldersIndexesAsync(processedFolder, isOriginal: false)
            );
        _currentImageIndex = 0;
        await UpdatePreviewImagesAsync();
        FocusWindowForInputDeferred();
    }

    private async void StartInitialReview_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await StartInitialReviewAsync();
        }
        catch (OperationCanceledException)
        {

        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"An error occurred:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task StartInitialReviewAsync()
    {
        if (_isInitialReview)
        {
            await FinishInitialReviewAsync();
            return;
        }

        var originalFolder = SelectFolder("Select original images folder");
        if (string.IsNullOrWhiteSpace(originalFolder))
        {
            return;
        }

        _isInitialReview = true;
        _isFinalReview = false;
        _viewModel.IsInitialReview = true;
        _viewModel.InitialReviewButtonText = "Finish Initial Review";
        _viewModel.FinalReviewButtonText = "Start Final Review...";
        _initialReviewFolder = _fileProcessor.EnsureInitialReviewFolder(originalFolder);

        await BuildFoldersIndexesAsync(originalFolder, isOriginal: true);
        if (_originalFolderIndex.LastIndex < 0)
        {
            ResetProcessedIndex();
            return;
        }

        LoadOriginalFilesList(originalFolder);
        ResetProcessedIndex();
        _currentImageIndex = 0;
        await UpdatePreviewImagesAsync();
        FocusWindowForInputDeferred();
    }

    private async Task FinishInitialReviewAsync()
    {
        await PersistInitialReviewResultsAsync();
        ClearReviewState(clearProcessed: true);
        _isInitialReview = false;
        _initialReviewFolder = null;
        _viewModel.IsInitialReview = false;
        _viewModel.InitialReviewButtonText = "Start Initial Review...";
        MessageBox.Show(this, "Initial Review Finished", "Review Finished", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void FinishFinalReview()
    {
        ClearReviewState(clearProcessed: true);
        _isFinalReview = false;
        _viewModel.FinalReviewButtonText = "Start Final Review...";
        MessageBox.Show(this, "Final Review Finished", "Review Finished", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void NextImageCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        try
        {
           await HandleOkAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error navigating to next image: {ex}");
            //MessageBox.Show(this, $"An error occurred:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void PreviousImageCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        try
        {
           await NavigateImages(-1);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error navigating to previous image: {ex}");
            //MessageBox.Show(this, $"An error occurred:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BadOriginalCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        BadOriginal_Click(sender, new RoutedEventArgs());
    }

    private void OvercuttedCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        Overcutted_Click(sender, new RoutedEventArgs());
    }

    private void OriginalFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFileSelection)
        {
            return;
        }

        if (sender is not ListBox listBox || listBox.SelectedItem is not ImageFileItem item)
        {
            return;
        }

        if (!_originalFolderIndex.TryGetFileIndexForPath(item.FilePath, out var idx))
        {
            return;
        }

        if (idx == _currentImageIndex)
        {
            return;
        }

        _currentImageIndex = idx;
        _ = UpdatePreviewImagesAsync();
    }

    private async void OkReview_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_isInitialReview)
            {
                SetReviewStatusForCurrentImage(ImageFileItem.ReviewStatusType.Approved,
                                               ImageFileItem.RejectReasonType.None,
                                               null);
                await NavigateImages(1);
                return;
            }

            await HandleOkAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error navigating from OK: {ex}");
        }
    }

    private async void BadOriginal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_isInitialReview)
            {
                //await SaveInitialReviewRejectedAsync("_bo");
                SetReviewStatusForCurrentImage(ImageFileItem.ReviewStatusType.Rejected,
                                               ImageFileItem.RejectReasonType.BadOriginal,
                                               null);

                await NavigateImages(1);
                return;
            }

            Debug.WriteLine("Bad original clicked.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error handling bad original: {ex}");
        }
    }

    private async void Overcutted_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_isInitialReview)
            {
                //await SaveInitialReviewRejectedAsync("_ct");
                SetReviewStatusForCurrentImage(ImageFileItem.ReviewStatusType.Rejected,
                                               ImageFileItem.RejectReasonType.Overcutted,
                                               null);

                await NavigateImages(1);
                return;
            }

            Debug.WriteLine("Overcutted clicked.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error handling overcutted: {ex}");
        }
    }

    private async void FileNameTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;

        if (!_isInitialReview)
        {
            return;
        }

        SetReviewStatusForCurrentImage(ImageFileItem.ReviewStatusType.Approved,
                                       ImageFileItem.RejectReasonType.None,
                                       null);
        await NavigateImages(1);
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

    private async Task BuildFoldersIndexesAsync(string folder, bool isOriginal)
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

        //_currentImageIndex = 0;
        //await UpdatePreviewImagesAsync();
    }

    private void SetReviewStatusForCurrentImage(ImageFileItem.ReviewStatusType status,
                                                ImageFileItem.RejectReasonType rejectReason,
                                                string? newName)
    {
        if (!_isInitialReview)
        {
            return;
        }
        var item = _viewModel.SelectedOriginalFile;
        if (item is null && _originalFolderIndex.TryGetFilePathForIndex(_currentImageIndex, out var currentPath))
        {
            item = _viewModel.OriginalFiles.FirstOrDefault(candidate =>
                string.Equals(candidate.FilePath, currentPath, StringComparison.OrdinalIgnoreCase));
        }

        if (item is null)
        {
            return;
        }

        if (newName is not null)
        {
            item.NewFileName = newName;
        }

        item.ReviewStatus = status;
        item.RejectReason = rejectReason;
    }

    private async Task PersistInitialReviewResultsAsync()
    {
        if (string.IsNullOrWhiteSpace(_initialReviewFolder))
        {
            return;
        }

        var items = _viewModel.OriginalFiles.ToList();
        await Task.Run(() =>
        {
            var rejectedFolder = _fileProcessor.EnsureRejectedFolder(_initialReviewFolder);
            foreach (var item in items)
            {
                switch (item.ReviewStatus)
                {
                    case ImageFileItem.ReviewStatusType.Pending:
                        var skippedFolder = _fileProcessor.EnsureSkippedFolder(_initialReviewFolder);
                        _fileProcessor.SaveFile(item.FilePath, p => p, skippedFolder);
                        continue;

                    case ImageFileItem.ReviewStatusType.Approved:
                        var approvedFileName = item.NewFileName;
                        if (string.IsNullOrWhiteSpace(_____))
                        {
                            _fileProcessor.SaveFile(item.FilePath, p => p, _initialReviewFolder);
                            continue;
                        }
                        approvedFileName = BuildReviewedFileName(item.FilePath, item.NewFileName);
                        _fileProcessor.SaveFile(item.FilePath, p => p, _initialReviewFolder, _ => approvedFileName);
                        continue;

                    case ImageFileItem.ReviewStatusType.Rejected:
                        var suffix = item.RejectReason switch
                        {
                            ImageFileItem.RejectReasonType.BadOriginal => "_bo",
                            ImageFileItem.RejectReasonType.Overcutted => "_ct",
                            _ => "_rejected",
                        };

                        var rejectedBaseName = BuildReviewedFileName(item.FilePath, item.NewFileName);
                        _fileProcessor.SaveFile(item.FilePath, p => p, rejectedFolder, _ => rejectedBaseName);
                        var rejectedFileName = BuildReviewedFileName(item.FilePath, item.NewFileName);
                        var suffixed = _fileProcessor.BuildSuffixedFileName(rejectedFileName, suffix);
                        _fileProcessor.SaveFile(item.FilePath, p => p, _initialReviewFolder, _ => suffixed);
                        continue;
                    default:
                        continue;
                }
            }
        });
    }

    private string BuildReviewedFileName(string sourcePath, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            return Path.GetFileName(sourcePath);
        }

        var ext = Path.GetExtension(sourcePath);
        return Path.HasExtension(newName) ? newName : string.Concat(newName, ext);
    }

    private async Task NavigateImages(int delta)
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
        await UpdatePreviewImagesAsync();
    }

    private async Task HandleOkAsync()
    {
        if (_isInitialReview)
        {
            SetReviewStatusForCurrentImage(ImageFileItem.ReviewStatusType.Approved,
                                           ImageFileItem.RejectReasonType.None,
                                           null);
        }

        await NavigateImages(1);
    }

    private async Task UpdatePreviewImagesAsync()
    {
        var currIdx = _currentImageIndex;
        var task1 = Task.Run(() => LoadBitmapForIndex(_originalFolderIndex, currIdx));
        var task2 = Task.Run(() => LoadBitmapForIndex(_processedFolderIndex, currIdx));
        var bitmaps = await Task.WhenAll(task1, task2);
        if (currIdx != _currentImageIndex)
        {
            return;
        }
        _viewModel.OriginalImagePreview = bitmaps[0];
        _viewModel.ReviewingImagePreview = bitmaps[1];

        UpdatePreviewLabels();
        UpdateSelectedOriginalFile();
        FocusCurrentFileNameField();
    }

    private BitmapSource? LoadBitmapForIndex(CurrentFolderIndex index, int imageIndex)
    {
        if (!index.TryGetFilePathForIndex(imageIndex, out var imagePath))
        {
            return null;
        }
        try
        {
            var img = _fileProcessor.LoadBitmapImage(imagePath);
            return img;
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"Failed to load image: {ex.Message}");
            //MessageBox.Show(this, $"Failed to load image:\n{ex.Message}", "Load Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"Failed to load image: {ex.Message}");
            //MessageBox.Show(this, $"Failed to load image:\n{ex.Message}", "Load Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        return null;
    }

    private void UpdatePreviewLabels()
    {
        _viewModel.OriginalImageLabel = BuildLabel("Original", _originalFolderIndex);
        _viewModel.ReviewingImageLabel = BuildLabel("Processed", _processedFolderIndex);
    }

    private void ResetProcessedIndex()
    {
        _processedFolderIndexCts?.Cancel();
        _processedFolderIndexCts = null;
        _processedFolderIndex.ClearIndex();
        _viewModel.ReviewingImagePreview = null;
        UpdatePreviewLabels();
    }

    private void ClearReviewState(bool clearProcessed)
    {
        _originalFolderIndexCts?.Cancel();
        _originalFolderIndexCts = null;
        _originalFolderIndex.ClearIndex();
        if (clearProcessed)
        {
            ResetProcessedIndex();
        }
        _currentImageIndex = 0;
        _viewModel.OriginalImagePreview = null;
        _viewModel.ReviewingImagePreview = null;
        _viewModel.OriginalFiles = new ObservableCollection<ImageFileItem>();
        _viewModel.SelectedOriginalFile = null;
        UpdatePreviewLabels();
    }

    private void LoadOriginalFilesList(string folderPath)
    {
        var files = _fileProcessor.ListImageFiles(folderPath);
        var items = files.Select(path => new ImageFileItem(path));
        _viewModel.OriginalFiles = new ObservableCollection<ImageFileItem>(items);
        _viewModel.SelectedOriginalFile = null;
    }

    private void UpdateSelectedOriginalFile()
    {
        if (!_isInitialReview)
        {
            return;
        }

        if (!_originalFolderIndex.TryGetFilePathForIndex(_currentImageIndex, out var currentPath))
        {
            return;
        }

        var match = _viewModel.OriginalFiles.FirstOrDefault(item =>
            string.Equals(item.FilePath, currentPath, StringComparison.OrdinalIgnoreCase));

        _suppressFileSelection = true;
        _viewModel.SelectedOriginalFile = match;
        _suppressFileSelection = false;
    }

    private void FocusCurrentFileNameField()
    {
        if (!_isInitialReview)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                var item = _viewModel.SelectedOriginalFile;
                if (item is null)
                {
                    return;
                }

                if (OriginalFilesList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container)
                {
                    OriginalFilesList.UpdateLayout();
                    container = OriginalFilesList.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
                }

                if (container is null)
                {
                    return;
                }

                var textBox = FindVisualChild<TextBox>(container);
                if (textBox is null)
                {
                    return;
                }

                textBox.Focus();
                textBox.SelectAll();
            }));
    }

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var descendent = FindVisualChild<T>(child);
            if (descendent is not null)
            {
                return descendent;
            }
        }

        return null;
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
            target.Source = _fileProcessor.LoadBitmapImage(imagePath);
        }
        catch (IOException ex)
        {
            target.Source = null;
            //MessageBox.Show(this, $"Failed to load image:\n{ex.Message}", "Load Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (UnauthorizedAccessException ex)
        {
            target.Source = null;
            //MessageBox.Show(this, $"Failed to load image:\n{ex.Message}", "Load Failed", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private Queue<int> _cachedIndices = new Queue<int>();
        private Queue<BitmapSource> _cachedImages = new Queue<BitmapSource>();

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

        public void ClearIndex()
        {
            Clear();
        }

        public async Task CreateAsync(string folderPath, string filePath, CancellationToken token)
        {
            Debug.WriteLine("Creating Folder index");
            Clear();

            await Task.Run(() =>
            {
                var files = Directory.EnumerateFiles(folderPath)
                                 .Where(_owner._fileProcessor.IsSupportedImage)
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

}
