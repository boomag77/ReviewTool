using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ReviewTool;

internal sealed class FinalReviewProcessor
{
    private const int MinThumbnailHeightPx = 48;
    private int _thumbnailHeightPx = 104;

    private readonly FileProcessor _fileProcessor;
    private readonly Dictionary<int, string> _imagePathByThumbnailIndex = new();

    public FinalReviewProcessor(FileProcessor fileProcessor)
    {
        _fileProcessor = fileProcessor;
    }

    public void SetThumbnailHeight(int thumbnailHeightPx)
    {
        _thumbnailHeightPx = Math.Max(MinThumbnailHeightPx, thumbnailHeightPx);
    }

    public FinalReviewThumbnailIndex BuildThumbnailIndex(IReadOnlyList<string> imagePaths,
                                                         IReadOnlyList<ReviewStatus> availableStatuses)
    {
        _imagePathByThumbnailIndex.Clear();
        var statusAffixes = BuildStatusAffixes(availableStatuses);
        var thumbnailItems = new List<FinalReviewThumbnailItem>(imagePaths.Count);
        var detectedFlagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < imagePaths.Count; index++)
        {
            var imagePath = imagePaths[index];
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                continue;
            }

            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(imagePath);
            var matchedFlagName = DetectFlagNameFromFileName(fileNameWithoutExtension, statusAffixes);
            _imagePathByThumbnailIndex[index] = imagePath;
            thumbnailItems.Add(new FinalReviewThumbnailItem
            {
                Index = index,
                Label = fileNameWithoutExtension,
                FilePath = imagePath,
                Thumbnail = null,
                FlagName = matchedFlagName
            });

            if (!string.IsNullOrWhiteSpace(matchedFlagName))
            {
                detectedFlagCounts.TryGetValue(matchedFlagName, out var count);
                detectedFlagCounts[matchedFlagName] = count + 1;
            }
        }

        var filters = new List<FinalReviewThumbnailFilterItem>
        {
            new FinalReviewThumbnailFilterItem
            {
                FlagName = null,
                ButtonLabel = $"All ({thumbnailItems.Count})"
            }
        };
        foreach (var kv in detectedFlagCounts.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            filters.Add(new FinalReviewThumbnailFilterItem
            {
                FlagName = kv.Key,
                ButtonLabel = $"{kv.Key} ({kv.Value})"
            });
        }

        return new FinalReviewThumbnailIndex
        {
            Items = thumbnailItems,
            Filters = filters
        };
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
            fastThumbnail.Freeze();
            return (BitmapSource)fastThumbnail;
        }, cancellationToken);
    }

    private static bool IsTiffFile(ReadOnlySpan<char> imagePathSpan)
    {
        return imagePathSpan.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) ||
           imagePathSpan.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase);
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
}

internal sealed class FinalReviewThumbnailIndex
{
    public List<FinalReviewThumbnailItem> Items { get; init; } = new();
    public List<FinalReviewThumbnailFilterItem> Filters { get; init; } = new();
}
