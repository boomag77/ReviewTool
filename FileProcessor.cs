using System.Collections.Frozen;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ReviewTool;

public sealed class FileProcessor
{
    private readonly FrozenSet<string> _imageExtensions =
        new[] { ".bmp", ".gif", ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".webp" }
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public string EnsureInitialReviewFolder(string originalFolder)
    {
        var initialFolder = GetInitialReviewFolderPath(originalFolder);
        EnsureDirectory(initialFolder);
        return initialFolder;
    }

    public string GetInitialReviewFolderPath(string originalFolder)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(originalFolder);
        var folderName = Path.GetFileName(trimmed);
        var parent = Path.GetDirectoryName(trimmed);
        var initialName = $"{folderName}_IR";
        return string.IsNullOrWhiteSpace(parent) ? initialName : Path.Combine(parent, initialName);
    }

    public string EnsureRejectedFolder(string initialFolder)
    {
        var rejectedFolder = Path.Combine(initialFolder, "Rejected");
        EnsureDirectory(rejectedFolder);
        return rejectedFolder;
    }

    public string EnsureSkippedFolder(string initialFolder)
    {
        var skippedFolder = Path.Combine(initialFolder, "Skipped");
        EnsureDirectory(skippedFolder);
        return skippedFolder;
    }

    public void SaveFile<T>(T item, Func<T, string> sourcePathSelector, string destinationFolder, Func<T, string>? destinationNameSelector = null)
    {
        var sourcePath = sourcePathSelector(item);
        var fileName = destinationNameSelector is null ? Path.GetFileName(sourcePath) : destinationNameSelector(item);
        var destinationPath = Path.Combine(destinationFolder, fileName);
        EnsureDirectory(destinationFolder);
        File.Copy(sourcePath, destinationPath, true);
    }

    public string BuildSuffixedFileName(string sourcePath, string suffix)
    {
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var ext = Path.GetExtension(sourcePath);
        return string.Concat(name, suffix, ext);
    }

    public async Task<int> GetMaxDigitsInImageFiles(string folderPath)
    {
        var maxDigits = 0;
        var imageFiles = ListImageFiles(folderPath);
        foreach (var file in imageFiles)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var digitCount = 0;
            for (var i = name.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(name[i]))
                {
                    digitCount++;
                }
                else
                {
                    break;
                }
            }
            if (digitCount > maxDigits)
            {
                maxDigits = digitCount;
            }
        }
        return maxDigits;
    }

    public bool IsSupportedImage(string filePath)
    {
        return _imageExtensions.Contains(Path.GetExtension(filePath));
    }

    public IReadOnlyList<string> ListImageFiles(string folderPath)
    {
        return Directory.EnumerateFiles(folderPath)
            .Where(IsSupportedImage)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public BitmapSource LoadBitmapImage(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var oriented = ApplyExifOrientation(frame);
        oriented.Freeze();
        return oriented;
    }

    private void EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    public void ClearDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(path))
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }

        foreach (var dir in Directory.GetDirectories(path))
        {
            var info = new DirectoryInfo(dir) { Attributes = FileAttributes.Normal };
            info.Delete(true);
        }
    }

    private BitmapSource ApplyExifOrientation(BitmapFrame frame)
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
