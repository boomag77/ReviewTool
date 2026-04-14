using ReviewTool.Interfaces;
using ReviewTool.Models;
using System.Collections.Frozen;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace ReviewTool.Services;

internal class MappingProcessor : IMappingProcessor, IDisposable
{

    private const string RescanFlagName = "Rescan";
    private const string BadOriginalFlagName = "BadOriginal";
    private const string PendingFlagName = "Pending";

    private readonly IFileProcessor _fileProcessor;
    private readonly IStatusAffixResolver _statusAffixResolver;

    public event Action<int, int>? MappingProgressUpdated;
    public event Action<string, string>? MappingCompleted;


    private readonly IUserDialogService _userDialogService;

    public MappingProcessor(IFileProcessor fileProcessor,
                            IUserDialogService userDialogService,
                            IStatusAffixResolver statusAffixResolver)
    {
        _fileProcessor = fileProcessor;
        _userDialogService = userDialogService;
        _statusAffixResolver = statusAffixResolver;
    }   

    private static bool IsSeparator(char c) => c == '\\';

    private sealed class OcsState
    {
        public string? CurrentSectionFolderPath { get; set; }
        public int CurrentNumber { get; set; }
    }

    private static bool IsFirstPageFlag(string flagName)
    {
        return string.Equals(flagName?.Trim(), "First page", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildOcsNumberedName(int number, ReadOnlySpan<char> extension)
    {
        return string.Concat(number.ToString("D5"), extension);
    }


    public async Task<bool> TryPerformMappingToAsync(string originalImagesFolderPath,
                                                     IReadOnlyList<ImageFileMappingInfo> mappingInfo,
                                                     CancellationTokenSource mappingCts,
                                                     bool isOcsEnabled)
    {
        string initialReviewFolder = _fileProcessor.EnsureInitialReviewFolder(originalImagesFolderPath);

        if (string.IsNullOrWhiteSpace(originalImagesFolderPath)
            || string.IsNullOrWhiteSpace(initialReviewFolder)
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
        var originalFilesPaths = _fileProcessor.GetImageFilesInDirectory(originalImagesFolderPath);
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
            var res = _userDialogService.ShowQuestion(
                "Original file names do not match the mapping file.\n\nDo you want to proceed anyway?",
                "Mapping mismatch");
            //var res = MessageBox.Show(_mainWindow,
            //    "Original file names do not match the mapping file.\n\nDo you want to proceed anyway?",
            //    "Mapping mismatch",
            //    MessageBoxButton.YesNo,
            //    MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes)
                return false;
        }
        int totalFilesToMap = originalFilesPaths.Count;

        var mappingItemsDict = BuildFrozenNameToExt(mappingInfo);


        //var bookName = Path.GetFileName(_originalFolderPath) ?? "undefined";
        var bookName = Path.GetFileName(Path.TrimEndingDirectorySeparator(originalImagesFolderPath));
        //_originalFolderPath = originalImagesFolderPath;
        if (string.IsNullOrWhiteSpace(bookName))
        {
            bookName = "undefined";
        }

        var issueReport = new StringBuilder();
        var totalMappedFiles = 0;
        var approvedCount = 0;
        var rejectedCount = 0;
        var missingCount = 0;
        //var mappingCts = new CancellationTokenSource();
        //var progressDialog = _mainWindow.CreateMappingProgressDialog(totalFilesToMap, mappingCts);
        //progressDialog.Show();
        MappingProgressUpdated?.Invoke(0, totalFilesToMap);
        var wasCanceled = false;

        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(initialReviewFolder);
                //var rejectedFolder = _fileProcessor.EnsureRejectedFolder(_initialReviewFolder);
                var pagesWithNumbers = new HashSet<int>();
                var maxPageNumber = 0;

                var ocsState = new OcsState();

                foreach (var origFilePath in originalFilesPaths)
                {
                    if (mappingCts.IsCancellationRequested) { return; }

                    string origFileNoExt = Path.GetFileNameWithoutExtension(origFilePath);
                    if (!mappingItemsDict.TryGetValue(origFileNoExt, out ImageFileMappingInfo item))
                    {
                        ReadOnlySpan<char> notMappedSuffix = "_not_mapped_";
                        string notMappedFileName = string.Concat(notMappedSuffix, Path.GetFileName(origFilePath.AsSpan()));
                        _fileProcessor.SaveFile(origFilePath, initialReviewFolder, notMappedFileName);
                        //_fileProcessor.SaveFile(origFilePath, p => p, _initialReviewFolder, _ => notMappedFileName);
                        continue;
                    }

                    var statusType = ParseStatusTypeFromMapping(item.ReviewStatus);
                    var rejectFlagName = NormalizeFlagName(item.RejectReason);

                    if (TryGetNumericPrefix(item.NewName.AsSpan(), out var pageNumber, out _)
                        && pageNumber > 0)
                    {
                        pagesWithNumbers.Add(pageNumber);
                        maxPageNumber = Math.Max(maxPageNumber, pageNumber);
                    }

                    ReadOnlySpan<char> sourceExt = Path.GetExtension(origFilePath.AsSpan());

                    if (isOcsEnabled)
                    {
                        var isFirstPage = IsFirstPageFlag(rejectFlagName);

                        if (isFirstPage)
                        {
                            var sectionFolderName = item.OriginalName;
                            var sectionFolderPath = Path.Combine(initialReviewFolder, sectionFolderName);
                            Directory.CreateDirectory(sectionFolderPath);

                            ocsState.CurrentSectionFolderPath = sectionFolderPath;
                            ocsState.CurrentNumber = 1;
                        }

                        if (!string.IsNullOrWhiteSpace(ocsState.CurrentSectionFolderPath))
                        {
                            var ocsFileName = BuildOcsNumberedName(ocsState.CurrentNumber, sourceExt);
                            _fileProcessor.SaveFile(origFilePath, ocsState.CurrentSectionFolderPath, ocsFileName);
                            ocsState.CurrentNumber++;

                            totalMappedFiles++;
                            MappingProgressUpdated?.Invoke(totalMappedFiles, totalFilesToMap);
                            continue;
                        }
                    }

                    switch (statusType)
                    {
                        case ReviewStatusType.Pending:
                            ReadOnlySpan<char> originalBase = item.NewName.AsSpan();
                            var pendingPrefix = GetPrefixForStatus(statusType, rejectFlagName);
                            var pendingSuffix = GetSuffixForStatus(statusType, rejectFlagName);
                            string notReviewedFileName = string.Concat(pendingPrefix.AsSpan(), originalBase, pendingSuffix.AsSpan(), sourceExt);
                            _fileProcessor.SaveFile(origFilePath, initialReviewFolder, notReviewedFileName);
                            issueReport.Append(bookName);
                            issueReport.Append('\t');
                            issueReport.Append(item.NewName.AsSpan());
                            issueReport.Append('\t');
                            issueReport.AppendLine("Not Reviewed");
                            break;

                        case ReviewStatusType.Accepted:
                            var approvedPrefix = GetPrefixForStatus(statusType, rejectFlagName);
                            var approvedSuffix = GetSuffixForStatus(statusType, rejectFlagName);
                            string approvedFileName = string.Concat(approvedPrefix.AsSpan(), item.NewName.AsSpan(), approvedSuffix.AsSpan(), sourceExt);
                            _fileProcessor.SaveFile(origFilePath, initialReviewFolder, approvedFileName);
                            approvedCount++;
                            break;

                        case ReviewStatusType.Rejected:
                            rejectedCount++;
                            var rejectedPrefix = GetPrefixForStatus(statusType, rejectFlagName);
                            var rejectedSuffix = GetSuffixForStatus(statusType, rejectFlagName);
                            string suffixed = string.Concat(rejectedPrefix.AsSpan(), item.NewName.AsSpan(), rejectedSuffix.AsSpan(), sourceExt);
                            _fileProcessor.SaveFile(origFilePath, initialReviewFolder, suffixed);
                            issueReport.Append(bookName);
                            issueReport.Append('\t');
                            issueReport.Append(item.NewName.AsSpan());
                            issueReport.Append('\t');
                            issueReport.AppendLine(string.IsNullOrWhiteSpace(rejectFlagName) ? "Rejected" : rejectFlagName);
                            break;
                    }

                    totalMappedFiles++;
                    MappingProgressUpdated?.Invoke(totalMappedFiles, totalFilesToMap);
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
        catch (Exception ex)
        {
            _userDialogService.ShowError($"An error occurred during mapping: {ex.Message}", "Mapping Error");
            return false;
        }
        if (mappingCts.IsCancellationRequested || wasCanceled)
        {
            _userDialogService.ShowInfo("Mapping was canceled.", "Mapping Canceled");
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
        MappingCompleted?.Invoke(summaryText, reportText);
        return true;
    }

    
    private static string NormalizeFlagName(string flagName)
    {
        if (string.IsNullOrWhiteSpace(flagName)
            || string.Equals(flagName, "None", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return flagName.Trim();
    }

    private static ReviewStatusType ParseStatusTypeFromMapping(string statusValue)
    {
        if (string.IsNullOrWhiteSpace(statusValue))
        {
            return ReviewStatusType.Pending;
        }

        if (string.Equals(statusValue, "Approved", StringComparison.OrdinalIgnoreCase)
            || string.Equals(statusValue, "Accepted", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewStatusType.Accepted;
        }

        if (string.Equals(statusValue, "Rejected", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewStatusType.Rejected;
        }

        if (string.Equals(statusValue, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewStatusType.Pending;
        }

        return ReviewStatusType.Pending;
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

    private string GetPrefixForStatus(ReviewStatusType statusType, string flagName)
    {
        if (!string.IsNullOrWhiteSpace(flagName)
            && _statusAffixResolver.TryGetReviewStatusByFlagName(flagName, out var statusByFlag)
            && !string.IsNullOrWhiteSpace(statusByFlag.StatusFlag.Prefix))
        {
            return NormalizePrefixForFileName(statusByFlag.StatusFlag.Prefix!);
        }

        if (_statusAffixResolver.TryGetRequiredReviewStatus(statusType, out var requiredStatus)
            && !string.IsNullOrWhiteSpace(requiredStatus.StatusFlag.Prefix))
        {
            return NormalizePrefixForFileName(requiredStatus.StatusFlag.Prefix!);
        }

        return statusType == ReviewStatusType.Pending ? "_not_reviewed_" : string.Empty;
    }

    private string GetSuffixForStatus(ReviewStatusType statusType, string flagName)
    {
        if (!string.IsNullOrWhiteSpace(flagName)
            && _statusAffixResolver.TryGetReviewStatusByFlagName(flagName, out var reviewStatus)
            && !string.IsNullOrWhiteSpace(reviewStatus.StatusFlag.Suffix))
        {
            return NormalizeSuffixForFileName(reviewStatus.StatusFlag.Suffix!);
        }

        if (_statusAffixResolver.TryGetRequiredReviewStatus(statusType, out var requiredStatus)
            && !string.IsNullOrWhiteSpace(requiredStatus.StatusFlag.Suffix))
        {
            return NormalizeSuffixForFileName(requiredStatus.StatusFlag.Suffix!);
        }

        if (statusType != ReviewStatusType.Rejected)
        {
            return string.Empty;
        }

        if (string.Equals(flagName, RescanFlagName, StringComparison.OrdinalIgnoreCase))
        {
            return "_rs";
        }

        if (string.Equals(flagName, BadOriginalFlagName, StringComparison.OrdinalIgnoreCase))
        {
            return "_bo";
        }

        return "_rejected";
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

    private static string NormalizePrefixForFileName(string prefix)
    {
        var normalizedToken = NormalizeFileNameAffixToken(prefix);
        if (string.IsNullOrWhiteSpace(normalizedToken))
        {
            return string.Empty;
        }

        return normalizedToken + "_";
    }

    private static string NormalizeSuffixForFileName(string suffix)
    {
        var normalizedToken = NormalizeFileNameAffixToken(suffix);
        if (string.IsNullOrWhiteSpace(normalizedToken))
        {
            return string.Empty;
        }

        return "_" + normalizedToken;
    }

    private static string NormalizeFileNameAffixToken(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return string.Empty;
        }

        var trimmedToken = rawToken.Trim();
        while (trimmedToken.Length > 0 &&
               (trimmedToken[0] == '_' || trimmedToken[0] == '-' || trimmedToken[0] == ' '))
        {
            trimmedToken = trimmedToken[1..];
        }

        while (trimmedToken.Length > 0 &&
               (trimmedToken[^1] == '_' || trimmedToken[^1] == '-' || trimmedToken[^1] == ' '))
        {
            trimmedToken = trimmedToken[..^1];
        }

        return trimmedToken;
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

    public void Dispose()
    {
        
    }
}
