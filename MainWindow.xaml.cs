using Microsoft.Win32;
using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ReviewTool;

public partial class MainWindow : Window
{
    private struct ReviewStat
    {
        public int notReviewed;
        public List<int> ApprovedPages;
        public List<int> BadOriginalPages;
        public List<int> OvercuttedPages;
        public List<int> SavedPages;
    }

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
    private MagnifierAdorner? _magnifierAdorner;
    private bool _magnifierEnabled;
    private double _magnifierZoom = 2.0;
    private const double MagnifierMinZoom = 1.0;
    private const double MagnifierMaxZoom = 10.0;
    private const double MagnifierZoomStep = 0.2;
    private double _magnifierSize = 240.0;
    private int? _lastSuggestedNumber;
    private readonly Dictionary<ImageFileItem, string> _suggestedNames = new();

    private readonly FileNameBuilder _fileNameBuilder;

    public MainWindow()
    {
        InitializeComponent();
        _fileNameBuilder = new FileNameBuilder();

        DataContext = _viewModel;
        Loaded += (_, _) => Keyboard.Focus(this);
        _originalFolderIndex = new CurrentFolderIndex(this);
        _processedFolderIndex = new CurrentFolderIndex(this);

    }

    private int TotalImagesToReview
    {
        get => field;
        set => field = value;
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
        var initialFolderPath = _fileProcessor.GetInitialReviewFolderPath(originalFolder);
        if (Directory.Exists(initialFolderPath))
        {
            var folderName = Path.GetFileName(initialFolderPath);
            var result = MessageBox.Show(this,
                $"{folderName} already exists. Do you want to do review {folderName} again?",
                "Initial Review",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                _isInitialReview = false;
                _viewModel.IsInitialReview = false;
                _viewModel.InitialReviewButtonText = "Start Initial Review...";
                return;
            }

            _fileProcessor.ClearDirectory(initialFolderPath);
        }

        _initialReviewFolder = _fileProcessor.EnsureInitialReviewFolder(originalFolder);
        _fileNameBuilder.Reset(_fileProcessor.ListImageFiles(_initialReviewFolder));
        _suggestedNames.Clear();

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

    private static List<int> CollectMissing(ReadOnlySpan<int> a)
    {
        var missing = new List<int>();
        if (a == null || a.Length < 2) return missing;

        for (int i = 1; i < a.Length; i++)
        {
            int prev = a[i - 1];
            int cur = a[i];

            // ÃÂµÃ‘ÂÃÂ»ÃÂ¸ ÃÂµÃ‘ÂÃ‘â€šÃ‘Å’ duplicates ÃÂ¸ÃÂ»ÃÂ¸ ÃÂ¼ÃÂ°Ã‘ÂÃ‘ÂÃÂ¸ÃÂ² ÃÂ½ÃÂµ Ã‘ÂÃ‘â€šÃ‘â‚¬ÃÂ¾ÃÂ³ÃÂ¾ ÃÂ²ÃÂ¾ÃÂ·Ã‘â‚¬ÃÂ°Ã‘ÂÃ‘â€šÃÂ°Ã‘Å½Ã‘â€°ÃÂ¸ÃÂ¹ Ã¢â‚¬â€ ÃÂ¿Ã‘â‚¬ÃÂ¾ÃÂ¿Ã‘Æ’Ã‘ÂÃÂºÃÂ°ÃÂµÃÂ¼ "ÃÂ¿ÃÂ»ÃÂ¾Ã‘â€¦ÃÂ¸ÃÂµ" ÃÂ¿ÃÂ°Ã‘â‚¬Ã‘â€¹
            if (cur <= prev) continue;

            // ÃÂ´ÃÂ¾ÃÂ±ÃÂ°ÃÂ²ÃÂ»Ã‘ÂÃÂµÃÂ¼ prev+1 ... cur-1
            for (int x = prev + 1; x < cur; x++)
                missing.Add(x);
        }

        return missing;
    }

    private async Task FinishInitialReviewAsync()
    {
        ReviewStat taskResult = await PersistInitialReviewResultsAsync();
        var missingPages = new List<int>();
        if (taskResult.SavedPages.Count > 0)
        {
            var savedPages = taskResult.SavedPages;
            savedPages.Sort();
            ReadOnlySpan<int> savedPagesSpan = CollectionsMarshal.AsSpan(savedPages);
            missingPages = CollectMissing(savedPagesSpan);
            

        }
        var totalImagesToReview = TotalImagesToReview;
        ClearReviewState(clearProcessed: true);
        _isInitialReview = false;
        _initialReviewFolder = null;
        _viewModel.IsInitialReview = false;
        _viewModel.InitialReviewButtonText = "Start Initial Review...";
        _lastSuggestedNumber = null;
        _suggestedNames.Clear();
        var reviewedCount = totalImagesToReview - taskResult.notReviewed;
        double sourceQuality = Math.Round((double)taskResult.ApprovedPages.Count / reviewedCount, 2) * 100;
        bool isSuccessedReview = true;
        List<string> reportRows = new(7);

        reportRows.Add($"Total images: {totalImagesToReview}\n");
        reportRows.Add($"Approved: {taskResult.ApprovedPages.Count}\n\n");
        if (taskResult.notReviewed > 0)
        {
            isSuccessedReview = false;
            reportRows.Add($"Not reviewed: {taskResult.notReviewed}\n\n");
        }
        if (taskResult.BadOriginalPages.Count > 0)
        {
            isSuccessedReview = false;
            var badOriginalPagesNumbers = string.Join(", ", taskResult.BadOriginalPages);
            reportRows.Add($"Bad Originals ({taskResult.BadOriginalPages.Count}): {badOriginalPagesNumbers}\n\n");
        }
        if (taskResult.OvercuttedPages.Count > 0)
        {
            isSuccessedReview = false;
            var overcuttedPagesNumbers = string.Join(", ", taskResult.OvercuttedPages);
            reportRows.Add($"Overcutted ({taskResult.OvercuttedPages.Count}): {overcuttedPagesNumbers}\n\n");
        }
        if (missingPages.Count > 0)
        {
            isSuccessedReview = false;
            var missingPagesString = string.Join(", ", missingPages);
            reportRows.Add($"Missing pages ({missingPages.Count}): {missingPagesString}\n\n");
        }
        reportRows.Add($"Estimated source images quality: {sourceQuality}");

        string statString = BuildReportMessage(reportRows);

        var messageBoxImage = isSuccessedReview ? MessageBoxImage.Information : MessageBoxImage.Warning;
        
        MessageBox.Show(this, $"Initial Review Finished:\n\n{statString}", "Review report", MessageBoxButton.OK, messageBoxImage);
    }

    private static string BuildReportMessage(IReadOnlyList<string> reportRows)
    {
        if (reportRows == null || reportRows.Count == 0)
            return string.Empty;

        // ÃÅ“ÃÂ¾ÃÂ¶ÃÂ½ÃÂ¾ ÃÂ¿Ã‘â‚¬ÃÂ¸ÃÂºÃÂ¸ÃÂ½Ã‘Æ’Ã‘â€šÃ‘Å’ capacity, Ã‘â€¡Ã‘â€šÃÂ¾ÃÂ±Ã‘â€¹ Ã‘â‚¬ÃÂµÃÂ¶ÃÂµ Ã‘â‚¬ÃÂ¾Ã‘Â StringBuilder
        int totalLen = 0;
        for (int i = 0; i < reportRows.Count; i++)
            totalLen += reportRows[i]?.Length ?? 0;

        var sb = new StringBuilder(totalLen);

        for (int i = 0; i < reportRows.Count; i++)
            sb.Append(reportRows[i]);

        return sb.ToString();
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
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(FocusCurrentFileNameField));
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

    private void OriginalViewbox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isInitialReview)
        {
            return;
        }

        if (OriginalViewbox == null || _viewModel.OriginalImagePreview == null)
        {
            return;
        }

        var pos = e.GetPosition(OriginalViewbox);
        EnableMagnifier(pos);
        OriginalViewbox.CaptureMouse();
        e.Handled = true;
    }

    private void OriginalViewbox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        DisableMagnifier();
        OriginalViewbox?.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OriginalViewbox_LostMouseCapture(object sender, MouseEventArgs e)
    {
        DisableMagnifier();
    }

    private void OriginalViewbox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_magnifierEnabled || _magnifierAdorner == null || OriginalViewbox == null)
        {
            return;
        }

        var pos = e.GetPosition(OriginalViewbox);
        _magnifierAdorner.UpdatePosition(pos);
    }

    private void OriginalViewbox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_magnifierEnabled || _magnifierAdorner == null)
        {
            return;
        }

        double deltaZoom = e.Delta > 0 ? MagnifierZoomStep : -MagnifierZoomStep;
        _magnifierZoom = Math.Max(MagnifierMinZoom, Math.Min(MagnifierMaxZoom, _magnifierZoom + deltaZoom));
        _magnifierAdorner.UpdateZoom(_magnifierZoom);
        e.Handled = true;
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

    private void FileNameTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        if (element.DataContext is not ImageFileItem item)
        {
            return;
        }

        if (!ReferenceEquals(_viewModel.SelectedOriginalFile, item))
        {
            _viewModel.SelectedOriginalFile = item;
        }
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

    private static bool TryGetNumericPrefix(ReadOnlySpan<char> s, out int value, out int prefixLen)
    {
        value = 0;
        prefixLen = 0;
        if (s.IsEmpty) return false;

        int i = 0;

        while (i < s.Length && char.IsDigit(s[i]))
            i++;

        prefixLen = i;
        if (prefixLen == 0) return false;

        return int.TryParse(s.Slice(0, prefixLen), out value);
    }

    private async Task<ReviewStat> PersistInitialReviewResultsAsync()
    {
        if (string.IsNullOrWhiteSpace(_initialReviewFolder))
        {
            return new ReviewStat();
        }
        int notReviewed = 0;
        HashSet<int> approvedPages = new();
        List<int> badOriginalPages = new();
        List<int> overcuttedPages = new();
        List<int> savedPages = new();
        var items = _viewModel.OriginalFiles.ToList();
        await Task.Run(() =>
        {
            var rejectedFolder = _fileProcessor.EnsureRejectedFolder(_initialReviewFolder);

            foreach (var item in items)
            {
                var newFileName = _fileNameBuilder.BuildReviewedFileName(item.FilePath, item.NewFileName, out bool hasPageNumber);
                _ = TryGetNumericPrefix(newFileName, out int pageNumber, out int prefixLen);
                int currentPageNumber = pageNumber;
                if (currentPageNumber >= 0 && hasPageNumber)
                {
                    savedPages.Add(currentPageNumber);
                }
                switch (item.ReviewStatus)
                {
                    case ImageFileItem.ReviewStatusType.Pending:
                        //var skippedFolder = _fileProcessor.EnsureSkippedFolder(_initialReviewFolder);
                        //_fileProcessor.SaveFile(item.FilePath, p => p, skippedFolder);
                        notReviewed++;
                        continue;

                    case ImageFileItem.ReviewStatusType.Approved:
                        approvedPages.Add(currentPageNumber);
                        _fileProcessor.SaveFile(item.FilePath, p => p, _initialReviewFolder, _ => newFileName);
                        continue;

                    case ImageFileItem.ReviewStatusType.Rejected:
                        var suffix = item.RejectReason switch
                        {
                            ImageFileItem.RejectReasonType.BadOriginal => "_bo",
                            ImageFileItem.RejectReasonType.Overcutted => "_ct",
                            _ => "_rejected",
                        };
                        if (item.RejectReason == ImageFileItem.RejectReasonType.BadOriginal && hasPageNumber)
                        {
                            badOriginalPages.Add(currentPageNumber);
                        }
                        else if (item.RejectReason == ImageFileItem.RejectReasonType.Overcutted && hasPageNumber)
                        {
                            overcuttedPages.Add(currentPageNumber);
                        }

                        //var rejectedBaseName = BuildReviewedFileName(item.FilePath, item.NewFileName);
                        _fileProcessor.SaveFile(item.FilePath, p => p, rejectedFolder, _ => newFileName);
                        var suffixed = _fileProcessor.BuildSuffixedFileName(newFileName, suffix);
                        _fileProcessor.SaveFile(item.FilePath, p => p, _initialReviewFolder, _ => suffixed);
                        continue;
                    default:
                        continue;
                }

            }
        });
        badOriginalPages.RemoveAll(approvedPages.Contains);
        overcuttedPages.RemoveAll(approvedPages.Contains);
        return new ReviewStat
        {
            notReviewed = notReviewed,
            ApprovedPages = approvedPages.ToList(),
            BadOriginalPages = badOriginalPages,
            OvercuttedPages = overcuttedPages,
            SavedPages = savedPages
        };
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

        CaptureLastSuggestedNumber();
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
        TotalImagesToReview = Math.Max(0, _originalFolderIndex.LastIndex + 1);
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

                if (ShouldSuggestName(item))
                {
                    var suggestion = GetNextSuggestedName();
                    item.NewFileName = suggestion;
                    _suggestedNames[item] = suggestion;
                    textBox.Text = suggestion;
                }

                textBox.Focus();
                textBox.SelectAll();
            }));
    }

    private void CaptureLastSuggestedNumber()
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

        if (TryGetNumericPrefix(item.NewFileName, out var number))
        {
            _lastSuggestedNumber = number;
        }
    }

    private string GetNextSuggestedName()
    {
        var next = _lastSuggestedNumber.HasValue ? _lastSuggestedNumber.Value + 1 : 0;
        return next.ToString("D3");
    }

    private bool ShouldSuggestName(ImageFileItem item)
    {
        if (string.IsNullOrWhiteSpace(item.NewFileName))
        {
            return true;
        }

        return _suggestedNames.TryGetValue(item, out var suggested)
            && string.Equals(item.NewFileName, suggested, StringComparison.Ordinal);
    }

    private static bool TryGetNumericPrefix(string name, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var span = name.AsSpan().Trim();
        var i = 0;
        while (i < span.Length && char.IsDigit(span[i]))
        {
            i++;
        }

        if (i == 0)
        {
            return false;
        }

        return int.TryParse(span[..i], out number);
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

    private void EnableMagnifier(Point center)
    {
        if (_magnifierEnabled || OriginalViewbox == null || OriginalImage == null)
        {
            return;
        }

        var layer = AdornerLayer.GetAdornerLayer(OriginalViewbox);
        if (layer == null)
        {
            return;
        }

        _magnifierAdorner = new MagnifierAdorner(OriginalViewbox, OriginalImage, layer, _magnifierZoom, _magnifierSize);
        _magnifierEnabled = true;
        _magnifierAdorner.UpdatePosition(center);
    }

    private void DisableMagnifier()
    {
        _magnifierEnabled = false;
        _magnifierAdorner?.Remove();
        _magnifierAdorner = null;
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

    private sealed class MagnifierAdorner : Adorner
    {
        private readonly FrameworkElement _sourceElement;
        private readonly AdornerLayer _layer;
        private Point _position;
        private double _zoom;
        private double _lensSize;

        public MagnifierAdorner(UIElement adornedElement,
                                FrameworkElement sourceElement,
                                AdornerLayer layer,
                                double initialZoom,
                                double lensSize)
            : base(adornedElement)
        {
            _sourceElement = sourceElement;
            _layer = layer;
            _zoom = initialZoom;
            _lensSize = lensSize;

            IsHitTestVisible = false;
            _layer.Add(this);
        }

        public void UpdatePosition(Point position)
        {
            _position = position;
            InvalidateVisual();
        }

        public void UpdateZoom(double zoom)
        {
            _zoom = zoom;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            if (AdornedElement is not FrameworkElement element || !element.IsVisible)
            {
                return;
            }

            var size = element.RenderSize;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return;
            }

            var imageRect = GetSourceRect(element);
            if (imageRect.Width <= 0 || imageRect.Height <= 0)
            {
                return;
            }

            var sourceSize = _sourceElement.RenderSize;
            if (sourceSize.Width <= 0 || sourceSize.Height <= 0)
            {
                return;
            }

            var lensSize = _lensSize;
            if (lensSize <= 0)
            {
                return;
            }

            double half = lensSize / 2.0;
            if (lensSize > imageRect.Width || lensSize > imageRect.Height)
            {
                lensSize = Math.Min(imageRect.Width, imageRect.Height);
                half = lensSize / 2.0;
            }
            double minX = imageRect.Left + half;
            double maxX = imageRect.Right - half;
            double minY = imageRect.Top + half;
            double maxY = imageRect.Bottom - half;
            double cx = Math.Max(minX, Math.Min(_position.X, maxX));
            double cy = Math.Max(minY, Math.Min(_position.Y, maxY));
            var center = new Point(cx, cy);
            var lensRect = new Rect(center.X - half, center.Y - half, lensSize, lensSize);

            double viewW = lensSize / _zoom;
            double viewH = lensSize / _zoom;
            if (viewW > sourceSize.Width) viewW = sourceSize.Width;
            if (viewH > sourceSize.Height) viewH = sourceSize.Height;

            double lensLeft = center.X - half;
            double lensTop = center.Y - half;
            double travelX = Math.Max(1.0, imageRect.Width - lensSize);
            double travelY = Math.Max(1.0, imageRect.Height - lensSize);
            double relX = (lensLeft - imageRect.Left) / travelX;
            double relY = (lensTop - imageRect.Top) / travelY;
            relX = Math.Max(0.0, Math.Min(1.0, relX));
            relY = Math.Max(0.0, Math.Min(1.0, relY));

            double maxOffsetX = Math.Max(0.0, sourceSize.Width - viewW);
            double maxOffsetY = Math.Max(0.0, sourceSize.Height - viewH);
            double vx = maxOffsetX * relX;
            double vy = maxOffsetY * relY;
            var viewbox = new Rect(vx, vy, viewW, viewH);

            var brush = new VisualBrush(_sourceElement)
            {
                Viewbox = viewbox,
                ViewboxUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill
            };

            drawingContext.PushClip(new RectangleGeometry(lensRect));
            drawingContext.DrawRectangle(brush, null, lensRect);
            drawingContext.Pop();

            var borderBrush = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));
            borderBrush.Freeze();
            var borderPen = new Pen(borderBrush, 1.5);
            borderPen.Freeze();
            drawingContext.DrawRectangle(null, borderPen, lensRect);
        }

        private Rect GetSourceRect(FrameworkElement adornedElement)
        {
            if (_sourceElement == null || adornedElement == null)
            {
                return Rect.Empty;
            }

            try
            {
                var bounds = VisualTreeHelper.GetDescendantBounds(_sourceElement);
                if (bounds.IsEmpty)
                {
                    return Rect.Empty;
                }

                var transform = _sourceElement.TransformToVisual(adornedElement);
                return transform.TransformBounds(bounds);
            }
            catch
            {
                return Rect.Empty;
            }
        }

        public void Remove()
        {
            _layer.Remove(this);
        }
    }

}
