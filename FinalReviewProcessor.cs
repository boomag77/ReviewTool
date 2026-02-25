using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ReviewTool;

internal sealed class FinalReviewProcessor
{
    private const int ThumbnailHeightPx = 104;

    private readonly FileProcessor _fileProcessor;
    private readonly Dictionary<int, string> _imagePathByThumbnailIndex = new();

    public FinalReviewProcessor(FileProcessor fileProcessor)
    {
        _fileProcessor = fileProcessor;
    }

    public List<FinalReviewThumbnailItem> BuildThumbnailItems(IReadOnlyList<string> imagePaths)
    {
        _imagePathByThumbnailIndex.Clear();
        var thumbnailItems = new List<FinalReviewThumbnailItem>(imagePaths.Count);
        for (var index = 0; index < imagePaths.Count; index++)
        {
            var imagePath = imagePaths[index];
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                continue;
            }

            _imagePathByThumbnailIndex[index] = imagePath;
            thumbnailItems.Add(new FinalReviewThumbnailItem
            {
                Index = index,
                Label = Path.GetFileNameWithoutExtension(imagePath),
                FilePath = imagePath,
                Thumbnail = null
            });
        }

        return thumbnailItems;
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
            cancellationToken.ThrowIfCancellationRequested();
            return LoadBitmapFromPath(imagePath);
        }, cancellationToken);
    }

    private Task<BitmapSource?> LoadThumbnailBitmapAsync(string imagePath, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                return null;
            }

            if (IsTiffFile(imagePath))
            {
                var orientedBitmap = LoadBitmapFromPath(imagePath);
                if (orientedBitmap is null)
                {
                    return null;
                }

                if (orientedBitmap.PixelHeight <= ThumbnailHeightPx || orientedBitmap.PixelHeight <= 0)
                {
                    return orientedBitmap;
                }

                var scaleRatio = ThumbnailHeightPx / (double)orientedBitmap.PixelHeight;
                cancellationToken.ThrowIfCancellationRequested();
                var downscaledBitmap = new TransformedBitmap(orientedBitmap, new ScaleTransform(scaleRatio, scaleRatio));
                downscaledBitmap.Freeze();
                return (BitmapSource)downscaledBitmap;
            }

            // Fast decode path for non-TIFF thumbnails to keep UI responsive.
            var fastThumbnail = new BitmapImage();
            fastThumbnail.BeginInit();
            fastThumbnail.CacheOption = BitmapCacheOption.OnLoad;
            fastThumbnail.DecodePixelHeight = ThumbnailHeightPx;
            fastThumbnail.UriSource = new Uri(imagePath, UriKind.Absolute);
            fastThumbnail.EndInit();
            fastThumbnail.Freeze();
            return (BitmapSource)fastThumbnail;
        }, cancellationToken);
    }

    private static bool IsTiffFile(string imagePath)
    {
        var extension = Path.GetExtension(imagePath);
        return extension.Equals(".tif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
    }
}
