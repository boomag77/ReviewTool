using Microsoft.Win32;
using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Policy;
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
        public string[] NotReviewedPages;
        public string[] ApprovedPages;
        public string[] BadOriginalPages;
        public string[] RescanPages;
        public string[] SavedPages;
        public int[] MissingPages;
        public int MaxPageNumber;
    }

    public static readonly RoutedCommand NextImageCommand = new();
    public static readonly RoutedCommand PreviousImageCommand = new();
    public static readonly RoutedCommand BadOriginalCommand = new();
    public static readonly RoutedCommand RescanCommand = new();

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

    private string _originalFolderPath = string.Empty;

    private List<ImageFileMappingInfo> _capturedMappingInfo = new();

    private readonly CancellationTokenSource _cts;

    public MainWindow()
    {
        InitializeComponent();
        //_fileNameBuilder = new FileNameBuilder();

        DataContext = _viewModel;
        Loaded += (_, _) => Keyboard.Focus(this);
        _originalFolderIndex = new CurrentFolderIndex(this);
        _processedFolderIndex = new CurrentFolderIndex(this);

    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _cts?.Cancel();
        _processedFolderIndexCts?.Cancel();
        _originalFolderIndexCts?.Cancel();
        base.OnClosing(e);
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

        _originalFolderPath = originalFolder;

        var imageFilesDigitsNumber = _fileProcessor.GetMaxDigitsInImageFiles(originalFolder);

        _isInitialReview = true;
        _isFinalReview = false;
        _viewModel.IsInitialReview = true;
        _viewModel.InitialReviewButtonText = "Finish Initial Review";
        _viewModel.FinalReviewButtonText = "Start Final Review...";
        //_fileNameBuilder.Reset(_fileProcessor.ListImageFiles(_initialReviewFolder));
        _suggestedNames.Clear();

        await BuildFoldersIndexesAsync(originalFolder, isOriginal: true);
        ReadOnlySpan<char> span = _originalFolderIndex.LastIndex.ToString().AsSpan();
        
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
        var actionResult = MessageBox.Show(
            this,
            "Choose what to do next:\n\nYes = Perform renumbering and save files\nNo = Only save review results to file",
            "Initial Review",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (actionResult == MessageBoxResult.Cancel)
        {
            return;
        }
        ReviewStat capturedtaskResult = new();
        if (_capturedMappingInfo.Count == 0)
        {
            (ReviewStat taskResult, List<ImageFileMappingInfo> mappingInfo) = await CreateInitialReviewResultAsync();
            capturedtaskResult = taskResult;
            _capturedMappingInfo = mappingInfo;
        }
        

        if (actionResult == MessageBoxResult.Yes)
        {
            var initialFolderPath = _fileProcessor.GetInitialReviewFolderPath(_originalFolderPath);
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
                    return;
                }

                _fileProcessor.ClearDirectory(initialFolderPath);
            }

            _initialReviewFolder = _fileProcessor.EnsureInitialReviewFolder(_originalFolderPath);
            if (!await TryPerformMappingToAsync(_originalFolderPath, _capturedMappingInfo))
            {
                return;
            }
        }
        else
        {
            if (!TryCreateAndSaveTsvFile(capturedtaskResult, _capturedMappingInfo))
            {
                return;
            }
        }

        _capturedMappingInfo.Clear();

        ClearReviewState(clearProcessed: true);
        _isInitialReview = false;
        _initialReviewFolder = null;
        _viewModel.IsInitialReview = false;
        _viewModel.InitialReviewButtonText = "Start Initial Review...";
        _lastSuggestedNumber = null;
        _suggestedNames.Clear();
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

    private void RescanCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        Rescan_Click(sender, new RoutedEventArgs());
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

        if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is ImageFileItem removedItem)
        {
            CaptureLastSuggestedNumber(removedItem);
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

    private async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_isInitialReview)
            {
                //await SaveInitialReviewRejectedAsync("_rs");
                SetReviewStatusForCurrentImage(ImageFileItem.ReviewStatusType.Rejected,
                                               ImageFileItem.RejectReasonType.Rescan,
                                               null);

                await NavigateImages(1);
                return;
            }

            Debug.WriteLine("Rescan clicked.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error handling rescan: {ex}");
        }
    }

    private async void PerformMapping_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var originalFolder = SelectFolder("Select original images folder");
            if (string.IsNullOrWhiteSpace(originalFolder))
            {
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Select mapping TSV file",
                Filter = "TSV files (*.tsv)|*.tsv|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var mappingInfo = ParseMappingInfoFrom(dialog.FileName);
            if (mappingInfo.Count == 0)
            {
                MessageBox.Show(this, "Mapping file is empty or invalid.", "Mapping", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var initialReviewFolderPath = _fileProcessor.GetInitialReviewFolderPath(originalFolder);
            if (Directory.Exists(initialReviewFolderPath))
            {
                var result = MessageBox.Show(this,
                    $"{Path.GetFileName(initialReviewFolderPath)} already exists. Do you want to proceed?",
                    "Initial Review",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question);
                if (result != MessageBoxResult.OK)
                {
                    return;
                }
            }

            _initialReviewFolder = _fileProcessor.EnsureInitialReviewFolder(originalFolder);
            await TryPerformMappingToAsync(originalFolder, mappingInfo);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"An error occurred:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private struct ImageFileMappingInfo
    {
        public string OriginalName;
        public string NewName;
        public string ReviewStatus;
        public string RejectReason;
        public string ReviewDate;
    }

    private async Task<(ReviewStat folderStat, List<ImageFileMappingInfo>)> CreateInitialReviewResultAsync()
    {
        string date = DateTime.UtcNow.ToString("yyyy-MM-dd");

        HashSet<int> pagesWithPageNumbers = new();
        HashSet<string> notReviewedPages = new();
        HashSet<string> approvedPages = new();
        HashSet<string> badOriginalPages = new();
        HashSet<string> rescanPages = new();
        HashSet<int> missingPages = new();

        var items = _viewModel.OriginalFiles.ToList();
        var itemsCount = items.Count;
        var maxIndex = itemsCount;

        var maxDigitsLen = maxIndex.ToString().Length;
        var fileNameBuilder = new FileNameBuilder(maxDigitsLen);
        int maxPageNumber = 0;
        List<ImageFileMappingInfo> folderMappingInfo = new List<ImageFileMappingInfo>(itemsCount);
        await Task.Run(() =>
        {
            //var rejectedFolder = _fileProcessor.EnsureRejectedFolder(_initialReviewFolder);

            for (int i = 0; i < itemsCount; i++)
            {

                var newFileName = fileNameBuilder.BuildReviewedFileName(items[i].FilePath, items[i].NewFileName, out bool hasPageNumber);
                if (TryGetNumericPrefix(newFileName.AsSpan(), out var pageNumber, out _)
                    && pageNumber > 0)
                {
                    pagesWithPageNumbers.Add(pageNumber);
                    maxPageNumber = Math.Max(maxPageNumber, pageNumber);
                }
                switch (items[i].ReviewStatus)
                {
                    case ImageFileItem.ReviewStatusType.Pending:

                        ReadOnlySpan<char> originalFileName = Path.GetFileNameWithoutExtension(items[i].FilePath.AsSpan());
                        var notReviewedFileName = string.Concat("_nr_", originalFileName);
                        notReviewedPages.Add(originalFileName.ToString());
                        break;

                    case ImageFileItem.ReviewStatusType.Approved:
                        approvedPages.Add(newFileName);
                        break;

                    case ImageFileItem.ReviewStatusType.Rejected:
                        if (items[i].RejectReason == ImageFileItem.RejectReasonType.BadOriginal)
                        {
                            badOriginalPages.Add(newFileName);
                        }
                        else if (items[i].RejectReason == ImageFileItem.RejectReasonType.Rescan)
                        {
                            rescanPages.Add(newFileName);
                        }
                        break;
                }

                ImageFileMappingInfo itemMappingInfo = new ImageFileMappingInfo
                {
                    OriginalName = items[i].FileName,
                    NewName = newFileName,
                    ReviewStatus = items[i].ReviewStatus.ToString(),
                    RejectReason = items[i].RejectReason.ToString(),
                    ReviewDate = date
                };
                folderMappingInfo.Add(itemMappingInfo);
            }
        });
        for (int i = 1; i <= maxPageNumber; i++)
        {
            if (!pagesWithPageNumbers.Contains(i))
            {
                missingPages.Add(i);
            }
        }
        return (new ReviewStat
        {
            NotReviewedPages = notReviewedPages.ToArray(),
            ApprovedPages = approvedPages.ToArray(),
            BadOriginalPages = badOriginalPages.ToArray(),
            RescanPages = rescanPages.ToArray(),
            MissingPages = missingPages.ToArray(),
            MaxPageNumber = maxPageNumber,

        }, folderMappingInfo);
    }

    private bool TryCreateAndSaveTsvFile(ReviewStat folderStat, List<ImageFileMappingInfo> mappingInfo)
    {
        

        static string Sanitize(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        }

        static string JoinItems<T>(IEnumerable<T>? items)
        {
            if (items == null)
            {
                return string.Empty;
            }

            return string.Join(", ", items);
        }

        var statsBuilder = new StringBuilder();
        statsBuilder.AppendLine("Parameter\tCount\tPagesList");
        statsBuilder.AppendLine($"Approved\t{folderStat.ApprovedPages?.Length ?? 0}\t{Sanitize(JoinItems(folderStat.ApprovedPages ?? Array.Empty<string>()))}");
        statsBuilder.AppendLine($"Not reviewed\t{folderStat.NotReviewedPages?.Length ?? 0}\t{Sanitize(JoinItems(folderStat.NotReviewedPages ?? Array.Empty<string>()))}");
        statsBuilder.AppendLine($"Bad originals\t{folderStat.BadOriginalPages?.Length ?? 0}\t{Sanitize(JoinItems(folderStat.BadOriginalPages ?? Array.Empty<string>()))}");
        statsBuilder.AppendLine($"Rescan\t{folderStat.RescanPages?.Length ?? 0}\t{Sanitize(JoinItems(folderStat.RescanPages ?? Array.Empty<string>()))}");
        statsBuilder.AppendLine($"Missing pages\t{folderStat.MissingPages?.Length ?? 0}\t{Sanitize(JoinItems(folderStat.MissingPages ?? Array.Empty<int>()))}");

        var mappingBuilder = new StringBuilder();
        mappingBuilder.AppendLine("OriginalName\tNewName\tReviewStatus\tRejectReason\tReviewDate");
        if (mappingInfo != null)
        {
            foreach (var item in mappingInfo)
            {
                mappingBuilder.Append(Sanitize(item.OriginalName));
                mappingBuilder.Append('\t');
                mappingBuilder.Append(Sanitize(item.NewName));
                mappingBuilder.Append('\t');
                mappingBuilder.Append(Sanitize(item.ReviewStatus));
                mappingBuilder.Append('\t');
                mappingBuilder.Append(Sanitize(item.RejectReason));
                mappingBuilder.Append('\t');
                mappingBuilder.AppendLine(Sanitize(item.ReviewDate));
            }
        }

        var baseName = string.IsNullOrWhiteSpace(_originalFolderPath)
            ? "InitialReviewMapping"
            : Path.GetFileName(Path.TrimEndingDirectorySeparator(_originalFolderPath));
        var saveDialog = new SaveFileDialog
        {
            Title = "Save mapping file",
            Filter = "IR mapping TSV files (*.tsv)|*.tsv|All files (*.*)|*.*",
            FileName = $"{baseName}.tsv",
            InitialDirectory = _originalFolderPath
        };

        if (saveDialog.ShowDialog() != true)
        {
            return false;
        }

        var mappingPath = saveDialog.FileName;
        try
        {
            File.WriteAllText(mappingPath, mappingBuilder.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to save mapping file:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        return true;
    }

    private static List<ImageFileMappingInfo> ParseMappingInfoFrom(string mappingInfoFilePath)
    {
        var result = new List<ImageFileMappingInfo>();
        if (string.IsNullOrWhiteSpace(mappingInfoFilePath) || !File.Exists(mappingInfoFilePath))
        {
            return result;
        }

        bool isFirstLine = true;
        foreach (var line in File.ReadLines(mappingInfoFilePath))
        {
            if (isFirstLine)
            {
                isFirstLine = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length < 5)
            {
                continue;
            }

            result.Add(new ImageFileMappingInfo
            {
                OriginalName = parts[0],
                NewName = parts[1],
                ReviewStatus = parts[2],
                RejectReason = parts[3],
                ReviewDate = parts[4]
            });
        }

        return result;
    }

    private async Task<bool> TryPerformMappingToAsync(string originalImagesFolderPath, List<ImageFileMappingInfo> mappingInfo)
    {
        if (string.IsNullOrWhiteSpace(originalImagesFolderPath)
            || string.IsNullOrWhiteSpace(_initialReviewFolder)
            || mappingInfo is null
            || mappingInfo.Count == 0)
        {
            return false;
        }

        var folderFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _fileProcessor.ListImageFiles(originalImagesFolderPath))
        {
            var name = Path.GetFileName(path);
            if (!string.IsNullOrWhiteSpace(name))
            {
                folderFiles.Add(name);
            }
        }

        var mappingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in mappingInfo)
        {
            var name = item.OriginalName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                mappingFiles.Add(name);
            }
        }

        if (!folderFiles.SetEquals(mappingFiles))
        {
            MessageBox.Show(this,
                "Original file names do not match the mapping file. Mapping was not performed.",
                "Mapping mismatch",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        var issueReport = new StringBuilder();
        var totalFiles = 0;
        var approvedCount = 0;
        var rejectedCount = 0;
        var missingCount = 0;

        await Task.Run(() =>
        {
            Directory.CreateDirectory(_initialReviewFolder);
            var rejectedFolder = _fileProcessor.EnsureRejectedFolder(_initialReviewFolder);
            var pagesWithNumbers = new HashSet<int>();
            var maxPageNumber = 0;

            foreach (var item in mappingInfo)
            {
                if (string.IsNullOrWhiteSpace(item.OriginalName)
                    || string.IsNullOrWhiteSpace(item.NewName))
                {
                    continue;
                }

                totalFiles++;
                var sourcePath = Path.Combine(originalImagesFolderPath, item.OriginalName);
                if (!File.Exists(sourcePath))
                {
                    continue;
                }

                if (!Enum.TryParse(item.ReviewStatus, true, out ImageFileItem.ReviewStatusType status))
                {
                    status = ImageFileItem.ReviewStatusType.Pending;
                }

                if (!Enum.TryParse(item.RejectReason, true, out ImageFileItem.RejectReasonType rejectReason))
                {
                    rejectReason = ImageFileItem.RejectReasonType.None;
                }

                if (TryGetNumericPrefix(item.NewName.AsSpan(), out var pageNumber, out _)
                    && pageNumber > 0)
                {
                    pagesWithNumbers.Add(pageNumber);
                    maxPageNumber = Math.Max(maxPageNumber, pageNumber);
                }

                switch (status)
                {
                    case ImageFileItem.ReviewStatusType.Pending:
                        var originalBase = Path.GetFileNameWithoutExtension(item.OriginalName);
                        var notReviewedFileName = string.Concat("_nr_", originalBase);
                        _fileProcessor.SaveFile(sourcePath, p => p, _initialReviewFolder, _ => notReviewedFileName);
                        issueReport.Append(item.NewName);
                        issueReport.Append('\t');
                        issueReport.AppendLine("Not Reviewed");
                        break;

                    case ImageFileItem.ReviewStatusType.Approved:
                        _fileProcessor.SaveFile(sourcePath, p => p, _initialReviewFolder, _ => item.NewName);
                        approvedCount++;
                        break;

                    case ImageFileItem.ReviewStatusType.Rejected:
                        rejectedCount++;
                        var suffix = rejectReason switch
                        {
                            ImageFileItem.RejectReasonType.BadOriginal => "_bo",
                            ImageFileItem.RejectReasonType.Rescan => "_rs",
                            _ => "_rejected",
                        };
                        _fileProcessor.SaveFile(sourcePath, p => p, rejectedFolder, _ => item.NewName);
                        var suffixed = _fileProcessor.BuildSuffixedFileName(item.NewName, suffix);
                        _fileProcessor.SaveFile(sourcePath, p => p, _initialReviewFolder, _ => suffixed);
                        var issueText = rejectReason == ImageFileItem.RejectReasonType.None
                            ? "Rejected"
                            : rejectReason.ToString();
                        issueReport.Append(item.NewName);
                        issueReport.Append('\t');
                        issueReport.AppendLine(issueText);
                        break;
                }
            }

            for (var i = 1; i <= maxPageNumber; i++)
            {
                if (!pagesWithNumbers.Contains(i))
                {
                    issueReport.Append(i);
                    issueReport.Append('\t');
                    issueReport.AppendLine("Missing");
                    missingCount++;
                }
            }
            if (rejectedCount == 0)
            {
                Directory.Delete(rejectedFolder, true);
            }
        });

        var reportText = issueReport.ToString();
        if (!string.IsNullOrWhiteSpace(reportText))
        {
            Clipboard.SetText(reportText);
        }

        MessageBox.Show(this,
            $"Mapping performed successfully.\n\nTotal files: {totalFiles}\nApproved: {approvedCount}\nRejected: {rejectedCount}\nMissing: {missingCount}\n\nIssue report has been copied to the clipboard.",
            "Mapping complete",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return true;
    }


    private async Task<ReviewStat> PersistInitialReviewResultsAsync()
    {
        if (string.IsNullOrWhiteSpace(_initialReviewFolder))
        {
            return new ReviewStat();
        }
        HashSet<int> pagesWithPageNumbers = new();
        HashSet<string> notReviewedPages = new();
        HashSet<string> approvedPages = new();
        HashSet<string> badOriginalPages = new();
        HashSet<string> rescanPages = new();
        HashSet<int> missingPages = new();

        var items = _viewModel.OriginalFiles.ToList();
        var itemsCount = items.Count;
        var maxIndex = itemsCount;
        
        var maxDigitsLen = maxIndex.ToString().Length;
        var fileNameBuilder = new FileNameBuilder(maxDigitsLen);
        int maxPageNumber = 0;
        await Task.Run(() =>
        {
            var rejectedFolder = _fileProcessor.EnsureRejectedFolder(_initialReviewFolder);

            for (int i = 0; i < itemsCount; i++)
            {

                var newFileName = fileNameBuilder.BuildReviewedFileName(items[i].FilePath, items[i].NewFileName, out bool hasPageNumber);
                if (TryGetNumericPrefix(newFileName.AsSpan(), out var pageNumber, out _)
                    && pageNumber > 0)
                {
                    pagesWithPageNumbers.Add(pageNumber);
                    maxPageNumber = Math.Max(maxPageNumber, pageNumber);
                }
                switch (items[i].ReviewStatus)
                {
                    case ImageFileItem.ReviewStatusType.Pending:

                        ReadOnlySpan<char> originalFileName = Path.GetFileNameWithoutExtension(items[i].FilePath.AsSpan());
                        var notReviewedFileName = string.Concat("_nr_", originalFileName);
                        notReviewedPages.Add(originalFileName.ToString());
                        _fileProcessor.SaveFile(items[i].FilePath, p => p, _initialReviewFolder, _ => notReviewedFileName);
                        continue;

                    case ImageFileItem.ReviewStatusType.Approved:
                        approvedPages.Add(newFileName);
                        _fileProcessor.SaveFile(items[i].FilePath, p => p, _initialReviewFolder, _ => newFileName);
                        continue;

                    case ImageFileItem.ReviewStatusType.Rejected:
                        var suffix = items[i].RejectReason switch
                        {
                            ImageFileItem.RejectReasonType.BadOriginal => "_bo",
                            ImageFileItem.RejectReasonType.Rescan => "_rs",
                            _ => "_rejected",
                        };
                        if (items[i].RejectReason == ImageFileItem.RejectReasonType.BadOriginal && hasPageNumber)
                        {
                            badOriginalPages.Add(newFileName);
                        }
                        else if (items[i].RejectReason == ImageFileItem.RejectReasonType.Rescan && hasPageNumber)
                        {
                            rescanPages.Add(newFileName);
                        }

                        //var rejectedBaseName = BuildReviewedFileName(item.FilePath, item.NewFileName);
                        _fileProcessor.SaveFile(items[i].FilePath, p => p, rejectedFolder, _ => newFileName);
                        var suffixed = _fileProcessor.BuildSuffixedFileName(newFileName, suffix);
                        _fileProcessor.SaveFile(items[i].FilePath, p => p, _initialReviewFolder, _ => suffixed);
                        continue;
                }
            }
        });
        for (int i = 1; i <= maxPageNumber; i++)
        {
            if (!pagesWithPageNumbers.Contains(i))
            {
                missingPages.Add(i);
            }
        }
        return new ReviewStat
        {
            NotReviewedPages = notReviewedPages.ToArray(),
            ApprovedPages = approvedPages.ToArray(),
            BadOriginalPages = badOriginalPages.ToArray(),
            RescanPages = rescanPages.ToArray(),
            MissingPages = missingPages.ToArray(),
            MaxPageNumber = maxPageNumber,
            
        };
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

        CaptureLastSuggestedNumber(item);
    }

    private void CaptureLastSuggestedNumber(ImageFileItem item)
    {
        if (TryGetNumericPrefix(item.NewFileName, out var number))
        {
            _lastSuggestedNumber = number;
        }
    }

    private string GetNextSuggestedName()
    {
        var next = _lastSuggestedNumber.HasValue ? _lastSuggestedNumber.Value + 1 : 0;
        string format = TotalImagesToReview < 1000 ? "D3" : "D4";
        return next.ToString(format);
    }

    private bool ShouldSuggestName(ImageFileItem item)
    {
        if (item.ReviewStatus != ImageFileItem.ReviewStatusType.Pending)
        {
            return false;
        }

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
