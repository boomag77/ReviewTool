using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ReviewTool;

internal sealed class ReviewProcessor
{
    private const int MinThumbnailHeightPx = 48;
    private int _thumbnailHeightPx = 104;

    private readonly FileProcessor _fileProcessor;
    private readonly Dictionary<int, string> _imagePathByThumbnailIndex = new();

    private readonly ConcurrentDictionary<string, int> _exifOrientationCache = new();

    public ReviewProcessor(FileProcessor fileProcessor)
    {
        _fileProcessor = fileProcessor;
    }

    public void SetThumbnailHeight(int thumbnailHeightPx)
    {
        _thumbnailHeightPx = Math.Max(MinThumbnailHeightPx, thumbnailHeightPx);
    }

    public ReviewThumbnailIndex BuildThumbnailIndex(IReadOnlyList<string> imagePaths,
                                                         IReadOnlyList<ReviewStatus> availableStatuses)
    {
        _ = availableStatuses;
        _imagePathByThumbnailIndex.Clear();
        _exifOrientationCache.Clear();
        var thumbnailItems = new List<ReviewThumbnailItem>(imagePaths.Count);
        var detectedFilterCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var filterLabelByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < imagePaths.Count; index++)
        {
            var imagePath = imagePaths[index];
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                continue;
            }

            var fileNameWithExtension = Path.GetFileName(imagePath);
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(imagePath);
            var detectedFilter = DetectFilterFromFileName(fileNameWithoutExtension);
            _imagePathByThumbnailIndex[index] = imagePath;
            thumbnailItems.Add(new ReviewThumbnailItem
            {
                Index = index,
                Label = fileNameWithExtension,
                FilePath = imagePath,
                Thumbnail = null,
                FlagName = detectedFilter?.FilterKey
            });

            if (detectedFilter is not null)
            {
                detectedFilterCounts.TryGetValue(detectedFilter.Value.FilterKey, out var count);
                detectedFilterCounts[detectedFilter.Value.FilterKey] = count + 1;
                filterLabelByKey[detectedFilter.Value.FilterKey] = detectedFilter.Value.FilterTitle;
            }
        }

        var filters = new List<ReviewThumbnailFilterItem>
        {
            new ReviewThumbnailFilterItem
            {
                FlagName = null,
                ButtonLabel = $"All ({thumbnailItems.Count})"
            }
        };
        foreach (var filterKey in detectedFilterCounts.Keys
                     .OrderBy(key => filterLabelByKey[key], StringComparer.OrdinalIgnoreCase))
        {
            filters.Add(new ReviewThumbnailFilterItem
            {
                FlagName = filterKey,
                ButtonLabel = $"{filterLabelByKey[filterKey]} ({detectedFilterCounts[filterKey]})"
            });
        }

        return new ReviewThumbnailIndex
        {
            Items = thumbnailItems,
            Filters = filters
        };
    }

    private static DetectedThumbnailFilter? DetectFilterFromFileName(string fileNameWithoutExtension)
    {
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
        {
            return null;
        }

        var prefixToken = ExtractPrefixToken(fileNameWithoutExtension);
        var suffixToken = ExtractSuffixToken(fileNameWithoutExtension);

        if (IsTextAffixToken(suffixToken))
        {
            return new DetectedThumbnailFilter($"suffix:{suffixToken}", suffixToken);
        }

        if (IsTextAffixToken(prefixToken))
        {
            return new DetectedThumbnailFilter($"prefix:{prefixToken}", prefixToken);
        }

        return null;
    }

    private static string ExtractPrefixToken(string fileNameWithoutExtension)
    {
        var separatorIndex = fileNameWithoutExtension.IndexOf('_');
        if (separatorIndex <= 0)
        {
            return string.Empty;
        }

        var rawPrefix = fileNameWithoutExtension[..separatorIndex];
        return NormalizeAffixToken(rawPrefix);
    }

    private static string ExtractSuffixToken(string fileNameWithoutExtension)
    {
        var separatorIndex = fileNameWithoutExtension.LastIndexOf('_');
        if (separatorIndex < 0 || separatorIndex >= fileNameWithoutExtension.Length - 1)
        {
            return string.Empty;
        }

        var rawSuffix = fileNameWithoutExtension[(separatorIndex + 1)..];
        return NormalizeAffixToken(rawSuffix);
    }

    private static bool IsTextAffixToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var hasLetter = false;
        foreach (var character in token)
        {
            if (char.IsDigit(character))
            {
                return false;
            }

            if (char.IsLetter(character))
            {
                hasLetter = true;
            }
        }

        return hasLetter;
    }

    public Task<BitmapSource?> LoadBitmapForThumbnailIndexAsync(int thumbnailIndex, CancellationToken cancellationToken)
    {
        if (!_imagePathByThumbnailIndex.TryGetValue(thumbnailIndex, out var imagePath))
        {
            return Task.FromResult<BitmapSource?>(null);
        }

        return LoadBitmapFromPathAsync(imagePath, cancellationToken);
    }

    public Task<BitmapSource?> LoadThumbnailForThumbnailIndexAsync(int thumbnailIndex, CancellationToken cancellationToken)
    {
        if (!_imagePathByThumbnailIndex.TryGetValue(thumbnailIndex, out var imagePath))
        {
            return Task.FromResult<BitmapSource?>(null);
        }

        return LoadThumbnailBitmapAsync(imagePath, cancellationToken);
    }

    private BitmapSource? LoadBitmapFromPath(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return null;
        }

        try
        {
            var bitmap = _fileProcessor.LoadBitmapImage(imagePath);
            if (!bitmap.IsFrozen)
            {
                bitmap.Freeze();
            }

            return bitmap;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load bitmap '{imagePath}': {ex.Message}");
            return null;
        }
    }

    private Task<BitmapSource?> LoadBitmapFromPathAsync(string imagePath, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            return LoadBitmapFromPath(imagePath);
        }, cancellationToken);
    }

    private Task<BitmapSource?> LoadThumbnailBitmapAsync(string imagePath, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                return null;
            }

            if (IsTiffFile(imagePath.AsSpan()))
            {
                if (TryLoadTiffThumbnailFast(imagePath, _thumbnailHeightPx, out var fastTiffThumbnail))
                {
                    return fastTiffThumbnail;
                }

                var orientedBitmap = LoadBitmapFromPath(imagePath);
                if (orientedBitmap is null)
                {
                    return null;
                }

                if (orientedBitmap.PixelHeight <= _thumbnailHeightPx || orientedBitmap.PixelHeight <= 0)
                {
                    return orientedBitmap;
                }

                var scaleRatio = _thumbnailHeightPx / (double)orientedBitmap.PixelHeight;
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                var downscaledBitmap = new TransformedBitmap(orientedBitmap, new ScaleTransform(scaleRatio, scaleRatio));
                downscaledBitmap.Freeze();
                return (BitmapSource)downscaledBitmap;
            }

            // Fast decode path for non-TIFF thumbnails to keep UI responsive.
            var fastThumbnail = new BitmapImage();
            fastThumbnail.BeginInit();
            fastThumbnail.CacheOption = BitmapCacheOption.OnLoad;
            fastThumbnail.DecodePixelHeight = _thumbnailHeightPx;
            fastThumbnail.UriSource = new Uri(imagePath, UriKind.Absolute);
            fastThumbnail.EndInit();
                var orientation = ReadExifOrientationFromFile(imagePath);
            var orientedThumbnail = ApplyExifOrientation(fastThumbnail, orientation);
            if (!orientedThumbnail.IsFrozen)
            {
                orientedThumbnail.Freeze();
            }

            return orientedThumbnail;
        }, cancellationToken);
    }

    private bool TryLoadTiffThumbnailFast(string imagePath, int targetHeightPx, out BitmapSource? thumbnailBitmap)
    {
        thumbnailBitmap = null;
        if (targetHeightPx <= 0)
        {
            return false;
        }

        try
        {
            var fastTiffThumbnail = new BitmapImage();
            fastTiffThumbnail.BeginInit();
            fastTiffThumbnail.CacheOption = BitmapCacheOption.OnLoad;
            fastTiffThumbnail.DecodePixelHeight = targetHeightPx;
            fastTiffThumbnail.UriSource = new Uri(imagePath, UriKind.Absolute);
            fastTiffThumbnail.EndInit();

            var orientation = ReadExifOrientationFromFile(imagePath);
            var orientedThumbnail = ApplyExifOrientation(fastTiffThumbnail, orientation);
            if (!orientedThumbnail.IsFrozen)
            {
                orientedThumbnail.Freeze();
            }

            thumbnailBitmap = orientedThumbnail;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Fast TIFF thumbnail decode failed for '{imagePath}': {ex.Message}");
            return false;
        }
    }

    private static bool IsTiffFile(ReadOnlySpan<char> imagePathSpan)
    {
        return imagePathSpan.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) ||
           imagePathSpan.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadExifOrientation(BitmapMetadata? metadata)
    {
        if (metadata is null)
        {
            return 1;
        }

        object? value = null;
        try
        {
            value = metadata.GetQuery("/app1/ifd/{ushort=274}");
        }
        catch
        {
        }

        if (value is null)
        {
            try
            {
                value = metadata.GetQuery("/ifd/{ushort=274}");
            }
            catch
            {
            }
        }

        if (value is ushort ushortValue)
        {
            return ushortValue;
        }

        if (value is short shortValue)
        {
            return shortValue;
        }

        return 1;
    }

    private int ReadExifOrientationFromFile(string imagePath)
    {
        if (_exifOrientationCache.TryGetValue(imagePath, out var cachedOrientation))
        {
            return cachedOrientation;
        }

        try
        {
            using var fileStream = new FileStream(
                imagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var bitmapDecoder = BitmapDecoder.Create(
                fileStream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.None);
            if (bitmapDecoder.Frames.Count == 0)
            {
                return 1;
            }

            var orientationValue = ReadExifOrientation(bitmapDecoder.Frames[0].Metadata as BitmapMetadata);
            _exifOrientationCache[imagePath] = orientationValue;
            return orientationValue;
        }
        catch
        {
            return 1;
        }
    }

    private static BitmapSource ApplyExifOrientation(BitmapSource sourceBitmap, int orientation)
    {
        if (orientation <= 1 || orientation > 8)
        {
            return sourceBitmap;
        }

        return orientation switch
        {
            2 => BuildTransformedBitmap(sourceBitmap, new ScaleTransform(-1, 1)),
            3 => BuildTransformedBitmap(sourceBitmap, new RotateTransform(180)),
            4 => BuildTransformedBitmap(sourceBitmap, new ScaleTransform(1, -1)),
            5 => BuildTransformedBitmap(sourceBitmap, BuildTransformGroup(new RotateTransform(90), new ScaleTransform(-1, 1))),
            6 => BuildTransformedBitmap(sourceBitmap, new RotateTransform(90)),
            7 => BuildTransformedBitmap(sourceBitmap, BuildTransformGroup(new RotateTransform(270), new ScaleTransform(-1, 1))),
            8 => BuildTransformedBitmap(sourceBitmap, new RotateTransform(270)),
            _ => sourceBitmap
        };
    }

    private static BitmapSource BuildTransformedBitmap(BitmapSource sourceBitmap, Transform transform)
    {
        var transformedBitmap = new TransformedBitmap(sourceBitmap, transform);
        transformedBitmap.Freeze();
        return transformedBitmap;
    }

    private static TransformGroup BuildTransformGroup(params Transform[] transforms)
    {
        var transformGroup = new TransformGroup();
        foreach (var transform in transforms)
        {
            transformGroup.Children.Add(transform);
        }

        return transformGroup;
    }

    private static List<StatusAffix> BuildStatusAffixes(IReadOnlyList<ReviewStatus> availableStatuses)
    {
        var affixes = new List<StatusAffix>(availableStatuses.Count);
        foreach (var status in availableStatuses)
        {
            var flagName = status.StatusFlag.Name?.Trim();
            if (string.IsNullOrWhiteSpace(flagName))
            {
                continue;
            }

            var prefixToken = NormalizeAffixToken(status.StatusFlag.Prefix);
            var suffixToken = NormalizeAffixToken(status.StatusFlag.Suffix);
            if (string.IsNullOrWhiteSpace(prefixToken) && string.IsNullOrWhiteSpace(suffixToken))
            {
                continue;
            }

            affixes.Add(new StatusAffix(flagName, prefixToken, suffixToken));
        }

        return affixes;
    }

    private static string? DetectFlagNameFromFileName(string fileNameWithoutExtension, IReadOnlyList<StatusAffix> statusAffixes)
    {
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
        {
            return null;
        }

        foreach (var affix in statusAffixes)
        {
            if (IsMatchByAffix(fileNameWithoutExtension, affix))
            {
                return affix.FlagName;
            }
        }

        return null;
    }

    private static bool IsMatchByAffix(string fileNameWithoutExtension, StatusAffix affix)
    {
        var fileNameSpan = fileNameWithoutExtension.AsSpan();
        if (!string.IsNullOrWhiteSpace(affix.PrefixToken))
        {
            var withSeparator = string.Concat(affix.PrefixToken, "_").AsSpan();
            if (fileNameSpan.StartsWith(withSeparator, StringComparison.OrdinalIgnoreCase)
                || fileNameSpan.StartsWith(affix.PrefixToken.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(affix.SuffixToken))
        {
            var withSeparator = string.Concat("_", affix.SuffixToken).AsSpan();
            if (fileNameSpan.EndsWith(withSeparator, StringComparison.OrdinalIgnoreCase)
                || fileNameSpan.EndsWith(affix.SuffixToken.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeAffixToken(string? rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return string.Empty;
        }

        var token = rawToken.Trim();
        while (token.Length > 0 && (token[0] == '_' || token[0] == '-' || token[0] == ' '))
        {
            token = token[1..];
        }

        while (token.Length > 0 && (token[^1] == '_' || token[^1] == '-' || token[^1] == ' '))
        {
            token = token[..^1];
        }

        return token;
    }

    private readonly record struct StatusAffix(string FlagName, string PrefixToken, string SuffixToken);
    private readonly record struct DetectedThumbnailFilter(string FilterKey, string FilterTitle);
}

internal sealed class ReviewThumbnailIndex
{
    public List<ReviewThumbnailItem> Items { get; init; } = new();
    public List<ReviewThumbnailFilterItem> Filters { get; init; } = new();
}
