using Microsoft.Win32;
using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
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
    private const bool TraceInputFlow = false;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void TraceInput(string message)
    {
        if (TraceInputFlow)
        {
            Debug.WriteLine(message);
        }
    }

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

    private const int MaxCustomStatusCount = 5;

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
    private readonly Dictionary<ImageFileItem, (int number, int width)> _autoFillSeeds = new();
    private bool _autoFillInProgress;
    private int _lastHandledIndex = -1;
    private bool _transitionInProgress;

    private string _originalFolderPath = string.Empty;

    private List<ImageFileMappingInfo> _capturedMappingInfo = new();

    private readonly CancellationTokenSource _cts;

    private string? _currentReviewerName;

    public MainWindow()
    {
        InitializeComponent();

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
        await AdvanceToIndexAsync(_currentImageIndex);
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

    private void CancelReview_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "The review is in progress!\n\nAre you sure you want to exit without saving any results?",
            $"Cancel Review {_viewModel.TargetFolderDisplayPath}",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        CancelInitialReview();
    }

    private async Task StartInitialReviewAsync()
    {
        if (_isInitialReview)
        {
            await FinishInitialReviewAsync();
            return;
        }

        if (!TryPromptReviewerName(out var reviewerName))
        {
            return;
        }
        _currentReviewerName = reviewerName;

        var originalFolder = SelectFolder("Select original images folder");
        if (string.IsNullOrWhiteSpace(originalFolder))
        {
            return;
        }

        _originalFolderPath = originalFolder;
        _viewModel.TargetFolderDisplayPath = BuildDisplayPath(originalFolder);

        var imageFilesDigitsNumber = _fileProcessor.GetMaxDigitsInImageFiles(originalFolder);

        _isInitialReview = true;
        _isFinalReview = false;
        _viewModel.IsInitialReview = true;
        _viewModel.InitialReviewButtonText = "Finish Initial Review";
        _viewModel.FinalReviewButtonText = "Start Final Review...";
        //_fileNameBuilder.Reset(_fileProcessor.ListImageFiles(_initialReviewFolder));
        _suggestedNames.Clear();
        _viewModel.IsAutoFillEnabled = false;

        await BuildFoldersIndexesAsync(originalFolder, isOriginal: true);
        //ReadOnlySpan<char> span = _originalFolderIndex.LastIndex.ToString().AsSpan();

        if (_originalFolderIndex.LastIndex < 0)
        {
            ResetProcessedIndex();
            return;
        }

        LoadOriginalFilesList(originalFolder);
        ResetProcessedIndex();
        _currentImageIndex = 0;
        await AdvanceToIndexAsync(_currentImageIndex);
        FocusWindowForInputDeferred();
    }


    private async Task FinishInitialReviewAsync()
    {
        var actionResult = ShowInitialReviewFinishDialog();
        if (actionResult == InitialReviewFinishAction.Cancel)
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


        if (actionResult == InitialReviewFinishAction.Apply)
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
                MessageBox.Show(this, "Failed to perform mapping.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                return;
            }
            var originalFolderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(_originalFolderPath)) ?? "mapping_file";
            var fileName = $"{originalFolderName}.tsv";
            var mappingFilePath = Path.Combine(_originalFolderPath, fileName);
            if (!TryCreateAndSaveTsvFile(capturedtaskResult, _capturedMappingInfo, mappingFilePath))
            {
                MessageBox.Show(this, "Failed to create or save mapping file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

            }

        }
        else if (actionResult == InitialReviewFinishAction.SaveToFile)
        {
            if (!TryCreateAndSaveTsvFile(capturedtaskResult, _capturedMappingInfo))
            {
                MessageBox.Show(this, "Failed to create or save mapping file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        _capturedMappingInfo.Clear();

        ClearReviewState(clearProcessed: true);
        _isInitialReview = false;
        _initialReviewFolder = null;
        _viewModel.IsInitialReview = false;
        _viewModel.TargetFolderDisplayPath = string.Empty;
        _viewModel.InitialReviewButtonText = "Start Initial Review...";
        _lastSuggestedNumber = null;
        _suggestedNames.Clear();
        _currentReviewerName = null;
    }

    private void CancelInitialReview()
    {
        _capturedMappingInfo.Clear();
        ClearReviewState(clearProcessed: true);
        _isInitialReview = false;
        _isFinalReview = false;
        _initialReviewFolder = null;
        _originalFolderPath = string.Empty;
        _viewModel.IsInitialReview = false;
        _viewModel.TargetFolderDisplayPath = string.Empty;
        _viewModel.InitialReviewButtonText = "Start Initial Review...";
        _viewModel.FinalReviewButtonText = "Start Final Review...";
        _lastSuggestedNumber = null;
        _suggestedNames.Clear();
        _currentReviewerName = null;
    }

    private static string BuildDisplayPath(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return string.Empty;
        }

        try
        {
            var trimmed = Path.TrimEndingDirectorySeparator(folderPath);
            var folderName = Path.GetFileName(trimmed);
            var parent = Path.GetDirectoryName(trimmed);
            var parentName = string.IsNullOrWhiteSpace(parent) ? string.Empty : Path.GetFileName(parent);
            if (!TryGetProjectName(folderPath, out var projectName))
            {
                projectName = "Undefined";
            }

            if (string.IsNullOrWhiteSpace(folderName))
            {
                return trimmed;
            }

            if (string.IsNullOrWhiteSpace(parentName))
            {
                return $@"...\{folderName}";
            }

            return $@"... {projectName}...\{parentName}\{folderName}";
        }
        catch
        {
            return folderPath;
        }
    }

    private enum InitialReviewFinishAction
    {
        Apply,
        SaveToFile,
        Cancel
    }

    private InitialReviewFinishAction ShowInitialReviewFinishDialog()
    {
        var dialog = new Window
        {
            Owner = this,
            Title = "Initial Review",
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            MinWidth = 420,
            Background = SystemColors.ControlBrush,
        };

        var root = new StackPanel
        {
            Margin = new Thickness(16),
            Orientation = Orientation.Vertical
        };

        var contentRow = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var icon = System.Drawing.SystemIcons.Question;
        var iconSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromWidthAndHeight(32, 32));
        iconSource.Freeze();

        var iconImage = new Image
        {
            Source = iconSource,
            Width = 32,
            Height = 32,
            Margin = new Thickness(0, 2, 12, 0)
        };
        DockPanel.SetDock(iconImage, Dock.Left);
        contentRow.Children.Add(iconImage);

        var text = new TextBlock
        {
            Text = "Choose what to do next:\n\nApply = Perform renumbering and save files\nSave to file = Only save review results to file",
            TextWrapping = TextWrapping.Wrap,
            Foreground = SystemColors.ControlTextBrush,
        };
        contentRow.Children.Add(text);
        root.Children.Add(contentRow);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        InitialReviewFinishAction result = InitialReviewFinishAction.Cancel;

        var applyButton = new Button
        {
            Content = "Apply",
            MinWidth = 80,
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)FindResource("ElevatedRoundedButton")
        };
        applyButton.Click += (_, _) =>
        {
            result = InitialReviewFinishAction.Apply;
            dialog.Close();
        };

        var saveButton = new Button
        {
            Content = "Save to file",
            MinWidth = 90,
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)FindResource("ElevatedRoundedButton")
        };
        saveButton.Click += (_, _) =>
        {
            result = InitialReviewFinishAction.SaveToFile;
            dialog.Close();
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 80,
            Style = (Style)FindResource("ElevatedRoundedButton")
        };
        cancelButton.Click += (_, _) =>
        {
            result = InitialReviewFinishAction.Cancel;
            dialog.Close();
        };

        buttons.Children.Add(applyButton);
        buttons.Children.Add(saveButton);
        buttons.Children.Add(cancelButton);
        root.Children.Add(buttons);

        dialog.Content = root;
        dialog.ShowDialog();
        return result;
    }

    private bool TryPromptReviewerName(out string reviewerName)
    {
        var dialog = new Window
        {
            Owner = this,
            Title = "Reviewer Name",
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            MinWidth = 360,
            Background = SystemColors.ControlBrush
        };

        var root = new StackPanel
        {
            Margin = new Thickness(16),
            Orientation = Orientation.Vertical
        };

        root.Children.Add(new TextBlock
        {
            Text = "Enter reviewer name (letters only, max 15):",
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = SystemColors.ControlTextBrush
        });

        var nameTextBox = new TextBox
        {
            MinWidth = 260,
            MaxLength = 15,
            Text = _currentReviewerName ?? string.Empty
        };
        root.Children.Add(nameTextBox);

        var validationText = new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = Brushes.IndianRed,
            Visibility = Visibility.Collapsed
        };
        root.Children.Add(validationText);

        var buttons = new StackPanel
        {
            Margin = new Thickness(0, 12, 0, 0),
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var okButton = new Button
        {
            Content = "Start Review",
            MinWidth = 100,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Style = (Style)FindResource("ElevatedRoundedButton")
        };
        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 80,
            IsCancel = true,
            Style = (Style)FindResource("ElevatedRoundedButton")
        };

        void RefreshValidation()
        {
            var validationError = ValidateReviewerName(nameTextBox.Text);
            var hasError = validationError is not null;
            validationText.Text = validationError ?? string.Empty;
            validationText.Visibility = hasError ? Visibility.Visible : Visibility.Collapsed;
            okButton.IsEnabled = !hasError;
        }

        nameTextBox.TextChanged += (_, _) => RefreshValidation();
        okButton.Click += (_, _) =>
        {
            if (ValidateReviewerName(nameTextBox.Text) is not null)
            {
                RefreshValidation();
                return;
            }

            dialog.DialogResult = true;
            dialog.Close();
        };
        cancelButton.Click += (_, _) =>
        {
            dialog.DialogResult = false;
            dialog.Close();
        };

        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);
        root.Children.Add(buttons);

        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            nameTextBox.Focus();
            nameTextBox.SelectAll();
            RefreshValidation();
        };

        var accepted = dialog.ShowDialog() == true;
        if (!accepted)
        {
            reviewerName = string.Empty;
            return false;
        }

        reviewerName = nameTextBox.Text.Trim();
        return true;
    }

    private static string? ValidateReviewerName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Name is required.";
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > 15)
        {
            return "Name must be 15 letters or fewer.";
        }

        foreach (var character in trimmedName)
        {
            if (!char.IsLetter(character))
            {
                return "Use letters only (no spaces or symbols).";
            }
        }

        return null;
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
            TraceInput($"NextImageCommand_Executed idx={_currentImageIndex} focus={Keyboard.FocusedElement?.GetType().Name}");
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
            if (_transitionInProgress)
            {
                return;
            }
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

    private async void OriginalFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        TraceInput($"SelectionChanged start idx={_currentImageIndex} focus={Keyboard.FocusedElement?.GetType().Name}");
        if (_transitionInProgress)
        {
            TraceInput("SelectionChanged suppressed (transition)");
            return;
        }
        if (_suppressFileSelection)
        {
            TraceInput("SelectionChanged suppressed");
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

        await AdvanceToIndexAsync(idx);
        TraceInput($"SelectionChanged end idx={_currentImageIndex}");
    }

    private async void OkReview_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_transitionInProgress)
            {
                return;
            }
            if (_isInitialReview)
            {
                SetReviewStatusForCurrentImage(ImageFileItem.ReviewStatusType.Accepted,
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
            if (_transitionInProgress)
            {
                return;
            }
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
            if (_transitionInProgress)
            {
                return;
            }
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

            var (bookName, mappingInfo) = ParseMappingInfoFrom(dialog.FileName);
            if (mappingInfo.Count == 0)
            {
                MessageBox.Show(this, "Mapping file is empty or invalid.", "Mapping", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (bookName != Path.GetFileName(Path.TrimEndingDirectorySeparator(originalFolder)))
            {
                var result = MessageBox.Show(this,
                    $"The mapping file was created for \"{bookName}\", but the selected folder is \"{Path.GetFileName(Path.TrimEndingDirectorySeparator(originalFolder))}\". Do you want to proceed?",
                    "Mapping",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
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
        TraceInput($"FileNameTextBox_PreviewKeyDown Enter idx={_currentImageIndex} focus={Keyboard.FocusedElement?.GetType().Name}");

        e.Handled = true;

        if (!_isInitialReview)
        {
            return;
        }
        if (_transitionInProgress)
        {
            return;
        }
        if (_currentImageIndex == _lastHandledIndex)
        {
            return;
        }
        _lastHandledIndex = _currentImageIndex;

        SetReviewStatusForCurrentImage(ImageFileItem.ReviewStatusType.Accepted,
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

    private void AutoFillCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialReview || !_viewModel.IsAutoFillEnabled)
        {
            return;
        }

        EnsureSuggestedNameForSelectedItem();

        var seedItem = _viewModel.SelectedOriginalFile;
        if (seedItem is null)
        {
            return;
        }

        TryApplyAutoFillFromItem(seedItem, seedItem.NewFileName, force: true);
    }

    private async void ResetReviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isInitialReview || _transitionInProgress)
        {
            return;
        }

        _viewModel.IsAutoFillEnabled = false;
        _lastSuggestedNumber = null;
        _lastHandledIndex = -1;
        _suggestedNames.Clear();
        _autoFillSeeds.Clear();
        _capturedMappingInfo.Clear();

        _autoFillInProgress = true;
        try
        {
            foreach (var item in _viewModel.OriginalFiles)
            {
                item.NewFileName = string.Empty;
                item.RejectReason = ImageFileItem.RejectReasonType.None;
                item.ReviewStatus = ImageFileItem.ReviewStatusType.Pending;
            }
        }
        finally
        {
            _autoFillInProgress = false;
        }

        _currentImageIndex = 0;
        await AdvanceToIndexAsync(_currentImageIndex);
        FocusWindowForInputDeferred();
    }

    private void FileNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_autoFillInProgress || !_isInitialReview || !_viewModel.IsAutoFillEnabled)
        {
            return;
        }

        if (sender is not TextBox textBox || textBox.DataContext is not ImageFileItem item)
        {
            return;
        }

        TryApplyAutoFillFromItem(item, textBox.Text);
    }

    private void TryApplyAutoFillFromItem(ImageFileItem seedItem, string seedText, bool force = false)
    {
        if (!TryGetNumericPrefixInfo(seedText, out var number, out var width))
        {
            return;
        }

        if (!force
            && _autoFillSeeds.TryGetValue(seedItem, out var existing)
            && existing.number == number
            && existing.width == width)
        {
            return;
        }

        _autoFillSeeds[seedItem] = (number, width);
        _lastSuggestedNumber = number;

        var items = _viewModel.OriginalFiles;
        var seedIndex = items.IndexOf(seedItem);
        if (seedIndex < 0)
        {
            return;
        }

        ApplyAutoFill(items, seedIndex, number + 1, width);
    }

    private void ApplyAutoFill(IList<ImageFileItem> items, int seedIndex, int nextNumber, int width)
    {
        if (seedIndex < 0 || seedIndex >= items.Count - 1)
        {
            return;
        }

        _autoFillInProgress = true;
        try
        {
            var number = nextNumber;
            for (var i = seedIndex + 1; i < items.Count; i++)
            {
                var item = items[i];
                var suggestion = number.ToString(GetAutoFillFormat(width));

                // When Auto fill is enabled, always overwrite subsequent items.
                // Status (AC/BO/RS) is independent from numbering, and users may go back to re-seed numbering.
                item.NewFileName = suggestion;
                _suggestedNames[item] = suggestion;
                _autoFillSeeds[item] = (number, width);

                number++;
            }
        }
        finally
        {
            _autoFillInProgress = false;
        }
    }

    private string GetAutoFillFormat(int seedWidth)
    {
        var minWidth = TotalImagesToReview < 1000 ? 3 : 4;
        var width = Math.Max(minWidth, seedWidth);
        return "D" + width.ToString();
    }

    private static bool TryGetNumericPrefixInfo(string name, out int number, out int width)
    {
        number = 0;
        width = 0;
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

        width = i;
        return int.TryParse(span[..i], out number);
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
        public string? ReviewerName;
    }

    private async Task<(ReviewStat folderStat, List<ImageFileMappingInfo>)> CreateInitialReviewResultAsync()
    {
        string date = DateTime.UtcNow.ToString("MM-dd-yyyy HH:mm 'UTC'");



        var items = _viewModel.OriginalFiles.ToArray();
        var itemsCount = items.Length;
        var maxIndex = itemsCount;

        HashSet<int> pagesWithPageNumbers = new(itemsCount);
        HashSet<string> notReviewedPages = new(itemsCount);
        HashSet<string> approvedPages = new(itemsCount);
        HashSet<string> badOriginalPages = new(itemsCount);
        HashSet<string> rescanPages = new(itemsCount);
        HashSet<int> missingPages = new(itemsCount);

        static int GetMaxDigitsInIndex(int index)
        {
            if (index < 0) index = -index; // если вдруг
            if (index < 10) return 1;
            if (index < 100) return 2;
            if (index < 1000) return 3;
            if (index < 10000) return 4;
            if (index < 100000) return 5;
            if (index < 1000000) return 6;
            if (index < 10000000) return 7;
            if (index < 100000000) return 8;
            if (index < 1000000000) return 9;
            return 10;
        }

        var maxDigitsLen = GetMaxDigitsInIndex(itemsCount);
        var fileNameBuilder = new FileNameBuilder(maxDigitsLen);
        int maxPageNumber = 0;
        List<ImageFileMappingInfo> folderMappingInfo = new List<ImageFileMappingInfo>(itemsCount);
        await Task.Run(() =>
        {

            for (int i = 0; i < itemsCount; i++)
            {
                var item = items[i];
                var newFileName = fileNameBuilder.BuildReviewedFileName(item.FilePath, item.NewFileName, out bool hasPageNumber);
                if (TryGetNumericPrefix(newFileName.AsSpan(), out var pageNumber, out _)
                    && pageNumber > 0)
                {
                    pagesWithPageNumbers.Add(pageNumber);
                    maxPageNumber = Math.Max(maxPageNumber, pageNumber);
                }
                switch (item.ReviewStatus)
                {
                    case ImageFileItem.ReviewStatusType.Pending:

                        ReadOnlySpan<char> originalFileName = Path.GetFileNameWithoutExtension(item.FilePath.AsSpan());
                        //var notReviewedFileName = string.Concat("_nr_", originalFileName);
                        notReviewedPages.Add(originalFileName.ToString());
                        break;

                    case ImageFileItem.ReviewStatusType.Accepted:
                        approvedPages.Add(newFileName);
                        break;

                    case ImageFileItem.ReviewStatusType.Rejected:
                        if (item.RejectReason == ImageFileItem.RejectReasonType.BadOriginal)
                        {
                            badOriginalPages.Add(newFileName);
                        }
                        else if (item.RejectReason == ImageFileItem.RejectReasonType.Rescan)
                        {
                            rescanPages.Add(newFileName);
                        }
                        break;
                }

                string statusStr = item.ReviewStatus switch
                {
                    ImageFileItem.ReviewStatusType.Pending => "Pending",
                    ImageFileItem.ReviewStatusType.Accepted => "Approved",
                    ImageFileItem.ReviewStatusType.Rejected => "Rejected",
                    _ => "Pending"
                };

                string rejectStr = item.RejectReason switch
                {
                    ImageFileItem.RejectReasonType.BadOriginal => "BadOriginal",
                    ImageFileItem.RejectReasonType.Rescan => "Rescan",
                    _ => "None"
                };

                ImageFileMappingInfo itemMappingInfo = new ImageFileMappingInfo
                {
                    OriginalName = Path.GetFileNameWithoutExtension(item.FileName),
                    NewName = Path.GetFileNameWithoutExtension(newFileName),
                    ReviewStatus = statusStr,
                    RejectReason = rejectStr,
                    ReviewDate = date,
                    ReviewerName = _currentReviewerName
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


    private bool TryCreateAndSaveTsvFile(ReviewStat folderStat, List<ImageFileMappingInfo> mappingInfo, string? pathToSave = null, bool namesWithExt = false)
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
        mappingBuilder.AppendLine(Path.GetFileName(Path.TrimEndingDirectorySeparator(_originalFolderPath) ?? "undefined"));
        mappingBuilder.AppendLine("OriginalName\tNewName\tReviewStatus\tRejectReason\tReviewDate\tReviewerName");
        if (mappingInfo != null)
        {
            foreach (var item in mappingInfo)
            {
                var originalName = namesWithExt ? item.OriginalName : Path.GetFileNameWithoutExtension(item.OriginalName);
                var newName = namesWithExt ? item.NewName : Path.GetFileNameWithoutExtension(item.NewName);
                mappingBuilder.Append(Sanitize(originalName));
                mappingBuilder.Append('\t');
                mappingBuilder.Append(Sanitize(newName));
                mappingBuilder.Append('\t');
                mappingBuilder.Append(Sanitize(item.ReviewStatus));
                mappingBuilder.Append('\t');
                mappingBuilder.Append(Sanitize(item.RejectReason));
                mappingBuilder.Append('\t');
                mappingBuilder.Append(Sanitize(item.ReviewDate));
                mappingBuilder.Append('\t');
                mappingBuilder.AppendLine(Sanitize(item.ReviewerName));
            }
        }

        var baseName = string.IsNullOrWhiteSpace(_originalFolderPath)
            ? "InitialReviewMapping"
            : Path.GetFileName(Path.TrimEndingDirectorySeparator(_originalFolderPath));

        var mappingFilePath = pathToSave;
        if (string.IsNullOrWhiteSpace(mappingFilePath))
        {
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
            mappingFilePath = saveDialog.FileName;
        }

        try
        {
            File.WriteAllText(mappingFilePath, mappingBuilder.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to save mapping file:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        MessageBox.Show(this,
            "Your initial review results were saved successfully.",
            "Information",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return true;
    }

    private static (string, List<ImageFileMappingInfo>) ParseMappingInfoFrom(string mappingInfoFilePath)
    {
        var result = new List<ImageFileMappingInfo>();
        if (string.IsNullOrWhiteSpace(mappingInfoFilePath) || !File.Exists(mappingInfoFilePath))
        {
            return (string.Empty, result);
        }

        bool isBookNameLine = true;
        bool isFirstLine = true;
        var bookName = string.Empty;
        foreach (var line in File.ReadLines(mappingInfoFilePath))
        {
            if (isBookNameLine)
            {
                bookName = line.Trim();
                isBookNameLine = false;
                continue;
            }
            if (isFirstLine)
            {
                isFirstLine = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            ReadOnlySpan<char> lineSpan = line.AsSpan();
            var parts = line.Split('\t');
            if (parts.Length < 5)
            {
                continue;
            }

            result.Add(new ImageFileMappingInfo
            {
                OriginalName = parts[0].Trim(),
                NewName = parts[1].Trim(),
                ReviewStatus = parts[2].Trim(),
                RejectReason = parts[3].Trim(),
                ReviewDate = parts[4].Trim(),
                ReviewerName = parts.Length > 5 ? parts[5].Trim() : null
            });
        }

        return (bookName, result);
    }

    private async Task<bool> TryPerformMappingToAsync(string originalImagesFolderPath, IReadOnlyList<ImageFileMappingInfo> mappingInfo)
    {
        if (string.IsNullOrWhiteSpace(originalImagesFolderPath)
            || string.IsNullOrWhiteSpace(_initialReviewFolder)
            || mappingInfo is null
            || mappingInfo.Count == 0)
        {
            return false;
        }



        static FrozenDictionary<string, ImageFileMappingInfo> BuildFrozenNameToExt(IReadOnlyList<ImageFileMappingInfo> mappingItems)
        {
            return mappingItems
                .ToFrozenDictionary(x => x.OriginalName, x => x, StringComparer.OrdinalIgnoreCase);
        }

        var folderFilesNoExtSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var originalFilesPaths = _fileProcessor.ListImageFiles(originalImagesFolderPath);
        foreach (var path in originalFilesPaths)
        {
            var nameNoExt = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrWhiteSpace(nameNoExt))
            {
                folderFilesNoExtSet.Add(nameNoExt);
            }
        }

        var mappingFilesNoExtSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in mappingInfo)
        {
            var nameNoExt = item.OriginalName;
            if (!string.IsNullOrWhiteSpace(nameNoExt))
            {
                mappingFilesNoExtSet.Add(nameNoExt);
            }
        }

        if (!folderFilesNoExtSet.SetEquals(mappingFilesNoExtSet))
        {
            var res = MessageBox.Show(this,
                "Original file names do not match the mapping file.\n\nDo you want to proceed anyway?",
                "Mapping mismatch",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes)
                return false;
        }
        int totalFilesToMap = originalFilesPaths.Count;

        var mappingItemsDict = BuildFrozenNameToExt(mappingInfo);


        //var bookName = Path.GetFileName(_originalFolderPath) ?? "undefined";
        var bookName = Path.GetFileName(Path.TrimEndingDirectorySeparator(originalImagesFolderPath));
        _originalFolderPath = originalImagesFolderPath;
        if (string.IsNullOrWhiteSpace(bookName))
        {
            bookName = "undefined";
        }

        var issueReport = new StringBuilder();
        var totalMappedFiles = 0;
        var approvedCount = 0;
        var rejectedCount = 0;
        var missingCount = 0;
        var mappingCts = new CancellationTokenSource();
        var progressDialog = CreateMappingProgressDialog(totalFilesToMap, mappingCts);
        progressDialog.Show();
        var wasCanceled = false;

        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(_initialReviewFolder);
                //var rejectedFolder = _fileProcessor.EnsureRejectedFolder(_initialReviewFolder);
                var pagesWithNumbers = new HashSet<int>();
                var maxPageNumber = 0;

                foreach (var origFilePath in originalFilesPaths)
                {
                    if (mappingCts.IsCancellationRequested) { return; }

                    string origFileNoExt = Path.GetFileNameWithoutExtension(origFilePath);
                    if (!mappingItemsDict.TryGetValue(origFileNoExt, out ImageFileMappingInfo item))
                    {
                        ReadOnlySpan<char> notMappedSuffix = "_not_mapped_";
                        string notMappedFileName = string.Concat(notMappedSuffix, Path.GetFileName(origFilePath.AsSpan()));
                        _fileProcessor.SaveFile(origFilePath, _initialReviewFolder, notMappedFileName);
                        //_fileProcessor.SaveFile(origFilePath, p => p, _initialReviewFolder, _ => notMappedFileName);
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

                    ReadOnlySpan<char> sourceExt = Path.GetExtension(origFilePath.AsSpan());
                    switch (status)
                    {
                        case ImageFileItem.ReviewStatusType.Pending:
                            ReadOnlySpan<char> originalBase = item.NewName.AsSpan();
                            ReadOnlySpan<char> prefix = "_not_reviewed_";
                            string notReviewedFileName = string.Concat(prefix, originalBase, sourceExt);
                            _fileProcessor.SaveFile(origFilePath, _initialReviewFolder, notReviewedFileName);
                            issueReport.Append(bookName);
                            issueReport.Append('\t');
                            issueReport.Append(item.NewName.AsSpan());
                            issueReport.Append('\t');
                            issueReport.AppendLine("Not Reviewed");
                            break;

                        case ImageFileItem.ReviewStatusType.Accepted:
                            string approvedFileName = string.Concat(item.NewName.AsSpan(), sourceExt);
                            _fileProcessor.SaveFile(origFilePath, _initialReviewFolder, approvedFileName);
                            approvedCount++;
                            break;

                        case ImageFileItem.ReviewStatusType.Rejected:
                            rejectedCount++;
                            ReadOnlySpan<char> suffix = rejectReason switch
                            {
                                ImageFileItem.RejectReasonType.BadOriginal => "_bo",
                                ImageFileItem.RejectReasonType.Rescan => "_rs",
                                _ => "_rejected",
                            };
                            string suffixed = string.Concat(item.NewName.AsSpan(), suffix, sourceExt);
                            _fileProcessor.SaveFile(origFilePath, _initialReviewFolder, suffixed);
                            issueReport.Append(bookName);
                            issueReport.Append('\t');
                            issueReport.Append(item.NewName.AsSpan());
                            issueReport.Append('\t');
                            issueReport.AppendLine(rejectReason switch
                            {
                                ImageFileItem.RejectReasonType.BadOriginal => "Bad Original",
                                ImageFileItem.RejectReasonType.Rescan => "Rescan",
                                _ => "Rejected",
                            });
                            break;
                    }

                    totalMappedFiles++;
                    Dispatcher.Invoke(() => UpdateMappingProgress(progressDialog, totalMappedFiles, totalFilesToMap));
                }

                if (mappingCts.IsCancellationRequested)
                {
                    return;
                }
                var missingBlock = new StringBuilder();
                for (var i = 1; i <= maxPageNumber; i++)
                {
                    if (!pagesWithNumbers.Contains(i))
                    {
                        missingBlock.Append(bookName);
                        missingBlock.Append('\t');
                        missingBlock.Append(i);
                        missingBlock.Append('\t');
                        missingBlock.AppendLine("Missing");
                        missingCount++;
                    }
                }
                if (missingCount > 0)
                {
                    issueReport.Insert(0, missingBlock.ToString());
                }
            }, mappingCts.Token);
        }
        catch (OperationCanceledException)
        {
            wasCanceled = true;
        }
        finally
        {
            progressDialog.Dispatcher.Invoke(progressDialog.Close);
        }

        if (mappingCts.IsCancellationRequested || wasCanceled)
        {
            return false;
        }

        var reportText = issueReport.ToString();
        var mappingTargetDisplayPath = BuildDisplayPath(originalImagesFolderPath);
        var summaryText =
            $"{mappingTargetDisplayPath}:\n\n" +
            $"Mapping performed successfully.\n\n" +
            $"Total files: {totalMappedFiles}\n" +
            $"Approved: {approvedCount}\n" +
            $"Rejected: {rejectedCount}\n" +
            $"Missing: {missingCount}";
        ShowMappingCompleteDialog(summaryText, reportText);
        return true;
    }

    private static ReadOnlySpan<char> GetFileNameWithoutExtension(ReadOnlySpan<char> fileName)
    {
        if (fileName.Length == 0)
            return new ReadOnlySpan<char>();

        int i = fileName.Length - 1;
        while (i >= 0 && fileName[i] != '.')
        {
            i--;
        }
        if (i < 0)
        {
            return fileName;
        }
        return fileName.Slice(0, i);
    }

    private static bool IsSeparator(char c) => c == '\\';
    public static bool TryGetProjectName(ReadOnlySpan<char> path, out ReadOnlySpan<char> projectName)
    {
        projectName = default;

        path = TrimTrailingSeparators(path);

        if (path.IsEmpty)
            return false;

        int end = path.Length; // end-exclusive
        int i = end - 1;

        while (true)
        {
            int segStart = LastIndexOfSeparator(path, i) + 1;
            var seg = path.Slice(segStart, end - segStart);

            if (!seg.IsEmpty && char.IsDigit(seg[0]))
            {
                int parentEnd = segStart - 1;
                if (parentEnd < 0)
                    return false;

                while (parentEnd >= 0 && IsSeparator(path[parentEnd]))
                    parentEnd--;

                if (parentEnd < 0)
                    return false;

                int parentStart = LastIndexOfSeparator(path, parentEnd) + 1;
                projectName = path.Slice(parentStart, parentEnd - parentStart + 1);
                return !projectName.IsEmpty;
            }

            int prevSep = segStart - 1;
            if (prevSep <= 0)
                return false;

            while (prevSep >= 0 && IsSeparator(path[prevSep]))
                prevSep--;

            if (prevSep < 0)
                return false;

            end = prevSep + 1;
            i = prevSep;
        }
    }

    private static ReadOnlySpan<char> TrimTrailingSeparators(ReadOnlySpan<char> path)
    {
        int end = path.Length;
        while (end > 0 && IsSeparator(path[end - 1]))
            end--;
        return path[..end];
    }

    private static int LastIndexOfSeparator(ReadOnlySpan<char> path, int fromInclusive)
    {
        for (int i = fromInclusive; i >= 0; i--)
        {
            if (IsSeparator(path[i]))
                return i;
        }
        return -1;
    }

    private void ShowMappingCompleteDialog(string summaryText, string reportText)
    {
        var dialog = new Window
        {
            Owner = this,
            Title = "Mapping complete",
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            MinWidth = 360,
            Background = SystemColors.ControlBrush,
        };

        var root = new StackPanel
        {
            Margin = new Thickness(16),
            Orientation = Orientation.Vertical
        };

        root.Children.Add(new TextBlock
        {
            Text = summaryText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
            Foreground = SystemColors.ControlTextBrush,
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var copyButton = new Button
        {
            Content = "Copy issues to clipboard",
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(12, 4, 12, 4),
            IsEnabled = !string.IsNullOrWhiteSpace(reportText),
            Style = (Style)FindResource("ElevatedRoundedButton")
        };
        copyButton.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(reportText))
            {
                Clipboard.SetText(reportText);
            }
        };

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 80,
            Padding = new Thickness(12, 4, 12, 4),
            Style = (Style)FindResource("ElevatedRoundedButton")
        };
        okButton.Click += (_, _) => dialog.Close();

        buttons.Children.Add(copyButton);
        buttons.Children.Add(okButton);
        root.Children.Add(buttons);

        dialog.Content = root;
        dialog.ShowDialog();
    }

    private Window CreateMappingProgressDialog(int totalFiles, CancellationTokenSource mappingCts)
    {
        var dialog = new Window
        {
            Owner = this,
            Title = "Mapping in progress",
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            MinWidth = 320,
            Background = SystemColors.ControlBrush,
            Tag = new MappingProgressState()
        };

        var state = (MappingProgressState)dialog.Tag;

        var root = new StackPanel
        {
            Margin = new Thickness(16),
            Orientation = Orientation.Vertical
        };

        var text = new TextBlock
        {
            Text = $"Mapping files: 0/{totalFiles}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = SystemColors.ControlTextBrush,
            Margin = new Thickness(0, 0, 0, 8)
        };
        state.StatusText = text;

        var progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = Math.Max(1, totalFiles),
            Value = 0,
            Height = 18,
            Margin = new Thickness(0, 0, 0, 12)
        };
        state.ProgressBar = progress;

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 80,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        cancelButton.Click += (_, _) => mappingCts.Cancel();

        root.Children.Add(text);
        root.Children.Add(progress);
        root.Children.Add(cancelButton);

        dialog.Content = root;
        return dialog;
    }

    private void UpdateMappingProgress(Window dialog, int mappedFiles, int totalFiles)
    {
        if (dialog.Tag is not MappingProgressState state)
        {
            return;
        }

        if (state.ProgressBar != null)
        {
            state.ProgressBar.Maximum = Math.Max(1, totalFiles);
            state.ProgressBar.Value = Math.Min(mappedFiles, totalFiles);
        }

        if (state.StatusText != null)
        {
            state.StatusText.Text = $"Mapping files: {mappedFiles}/{totalFiles}";
        }
    }

    private sealed class MappingProgressState
    {
        public ProgressBar? ProgressBar { get; set; }
        public TextBlock? StatusText { get; set; }
    }



    private async Task NavigateImages(int delta)
    {
        TraceInput($"NavigateImages start idx={_currentImageIndex} delta={delta}");
        if (_transitionInProgress)
        {
            return;
        }
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
        await AdvanceToIndexAsync(nextIndex);
        TraceInput($"NavigateImages end idx={_currentImageIndex}");
    }

    private async Task AdvanceToIndexAsync(int nextIndex)
    {
        TraceInput($"AdvanceToIndexAsync start idx={_currentImageIndex} -> {nextIndex}");
        if (_transitionInProgress)
        {
            return;
        }

        // Transition lock: while we are applying status, changing index, refreshing preview,
        // updating selection, and ensuring a suggested name exists, ignore further user input.
        _transitionInProgress = true;
        _suppressFileSelection = true;
        try
        {
            _currentImageIndex = nextIndex;
            await UpdatePreviewImagesAsync();
            UpdateSelectedOriginalFile();
            EnsureSuggestedNameForSelectedItem();
            FocusCurrentFileNameField();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateSuggestedHeaderLayout));
        }
        finally
        {
            _suppressFileSelection = false;
            _lastHandledIndex = -1;
            _transitionInProgress = false;
        }
        TraceInput($"AdvanceToIndexAsync end idx={_currentImageIndex}");
    }

    private void OriginalViewbox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSuggestedHeaderLayout();
    }

    private void UpdateSuggestedHeaderLayout()
    {
        if (!_isInitialReview)
        {
            return;
        }

        if (SuggestedHeaderCanvas is null || SuggestedHeaderLeft is null || SuggestedHeaderRight is null || OriginalViewbox is null)
        {
            return;
        }

        if (OriginalImage?.Source is not BitmapSource bitmap)
        {
            return;
        }

        var viewW = OriginalViewbox.ActualWidth;
        var viewH = OriginalViewbox.ActualHeight;
        if (viewW <= 0 || viewH <= 0)
        {
            return;
        }

        var imgW = (double)bitmap.PixelWidth;
        var imgH = (double)bitmap.PixelHeight;
        if (imgW <= 0 || imgH <= 0)
        {
            return;
        }

        // Stretch=Uniform centers the image with letterboxing. Compute the displayed rect.
        var scale = Math.Min(viewW / imgW, viewH / imgH);
        var dispW = imgW * scale;
        var left = (viewW - dispW) / 2.0;
        var headerW = SuggestedHeaderCanvas.ActualWidth;
        var offsetX = (headerW - viewW) / 2.0;

        SuggestedHeaderLeft.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SuggestedHeaderRight.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var rightW = SuggestedHeaderRight.DesiredSize.Width;

        Canvas.SetLeft(SuggestedHeaderLeft, offsetX + left);
        Canvas.SetTop(SuggestedHeaderLeft, 0);

        Canvas.SetLeft(SuggestedHeaderRight, offsetX + left + dispW - rightW);
        Canvas.SetTop(SuggestedHeaderRight, 0);
    }

    private async Task HandleOkAsync()
    {
        TraceInput($"HandleOkAsync start idx={_currentImageIndex}");
        if (_transitionInProgress)
        {
            return;
        }
        if (_isInitialReview)
        {
            if (_currentImageIndex == _lastHandledIndex)
            {
                return;
            }
            _lastHandledIndex = _currentImageIndex;

            SetReviewStatusForCurrentImage(ImageFileItem.ReviewStatusType.Accepted,
                                           ImageFileItem.RejectReasonType.None,
                                           null);
        }

        await NavigateImages(1);
        TraceInput($"HandleOkAsync end idx={_currentImageIndex}");
    }

    private async Task UpdatePreviewImagesAsync()
    {
        TraceInput($"UpdatePreviewImagesAsync start idx={_currentImageIndex}");
        var currIdx = _currentImageIndex;
        var task1 = Task.Run(() => LoadBitmapForIndex(_originalFolderIndex, currIdx));
        var task2 = Task.Run(() => LoadBitmapForIndex(_processedFolderIndex, currIdx));
        var bitmaps = await Task.WhenAll(task1, task2);
        if (currIdx != _currentImageIndex)
        {
            TraceInput($"UpdatePreviewImagesAsync stale idx={currIdx} current={_currentImageIndex}");
            return;
        }
        _viewModel.OriginalImagePreview = bitmaps[0];
        _viewModel.ReviewingImagePreview = bitmaps[1];

        UpdatePreviewLabels();
        TraceInput($"UpdatePreviewImagesAsync end idx={_currentImageIndex}");
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

    // Apply suggestion to the view-model immediately (without relying on TextBox creation/focus timing).
    // This prevents "skips" when the user triggers the next action before the UI has a chance to focus/render.
    private void EnsureSuggestedNameForSelectedItem()
    {
        if (!_isInitialReview)
        {
            return;
        }

        var item = _viewModel.SelectedOriginalFile;
        if (item is null)
        {
            return;
        }

        if (!ShouldSuggestName(item))
        {
            return;
        }

        var suggestion = GetNextSuggestedName();
        item.NewFileName = suggestion;
        _suggestedNames[item] = suggestion;
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
                                 .OrderBy(f => Path.GetFileName(f), ExplorerComparer.Instance);
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

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string x, string y);

    private sealed class ExplorerComparer : IComparer<string>
    {
        public static readonly ExplorerComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            return StrCmpLogicalW(x, y);
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

            var scaleX = sourceSize.Width / imageRect.Width;
            var scaleY = sourceSize.Height / imageRect.Height;
            if (!double.IsFinite(scaleX) || scaleX <= 0)
            {
                scaleX = 1.0;
            }
            if (!double.IsFinite(scaleY) || scaleY <= 0)
            {
                scaleY = 1.0;
            }

            double viewW = (lensSize / _zoom) * scaleX;
            double viewH = (lensSize / _zoom) * scaleY;
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
