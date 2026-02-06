using System.Collections.Frozen;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
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
        Directory.CreateDirectory(initialFolder);
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

    //public string EnsureRejectedFolder(string initialFolder)
    //{
    //    var rejectedFolder = Path.Combine(initialFolder, "Rejected");
    //    Directory.CreateDirectory(rejectedFolder);
    //    return rejectedFolder;
    //}

    //public string EnsureSkippedFolder(string initialFolder)
    //{
    //    var skippedFolder = Path.Combine(initialFolder, "Skipped");
    //    Directory.CreateDirectory(skippedFolder);
    //    return skippedFolder;
    //}

    public void SaveFile(string sourcePath, string destinationFolder, string newFileName)
    {
        var destinationPath = Path.Combine(destinationFolder, newFileName);
        File.Copy(sourcePath, destinationPath, true);
    }

    //public void SaveFile<T>(T item, Func<T, string> sourcePathSelector, string destinationFolder, Func<T, string>? destinationNameSelector = null)
    //{
    //    var sourcePath = sourcePathSelector(item);
    //    var fileName = destinationNameSelector is null ? Path.GetFileName(sourcePath) : destinationNameSelector(item);
    //    var destinationPath = Path.Combine(destinationFolder, fileName);
    //    Directory.CreateDirectory(destinationFolder);
    //    File.Copy(sourcePath, destinationPath, true);
    //}

    public string BuildSuffixedFileNameWithExtension(string sourcePath, string suffix, string extension)
    {
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        extension = extension[0] == '.' ? extension : "." + extension;
        return string.Concat(name, suffix, extension);
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
        //using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        //var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        //var frame = decoder.Frames[0];
        //var oriented = ApplyExifOrientation(frame);
        //oriented.Freeze();
        //return oriented;
        return LoadBitmapImageGeneric(path)
            ?? throw new InvalidDataException($"Failed to load image from file: {path}");
    }

    private BitmapSource? LoadBitmapImageGeneric(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var extension = Path.GetExtension(path);
        if (extension.Equals(".tif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var tiffImage = TiffReader.LoadImageSourceFromTiff(path).GetAwaiter().GetResult();
                if (tiffImage is BitmapSource tiffBmp)
                {
                    if (!tiffBmp.IsFrozen) tiffBmp.Freeze();
                    return tiffBmp;
                }
            }
            catch
            {
                // fall through to generic loaders
            }
        }

        if (TryLoadWithWic(path, out var wicBmp))
        {
            return wicBmp;
        }

        if (TryLoadWithWicUri(path, out var wicUriBmp))
        {
            return wicUriBmp;
        }

        if (TryLoadWithGdi(path, out var gdiBmp))
        {
            return gdiBmp;
        }

        return null;
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

    private static bool TryLoadWithWic(string path, out BitmapSource? bmp)
    {
        bmp = null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            var decoder = BitmapDecoder.Create(
                fs,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);

            var frame = decoder.Frames[0];
            BitmapSource src = frame;
            int orientation = ReadExifOrientation(frame.Metadata as BitmapMetadata);
            src = ApplyExifOrientation(src, orientation);
            src = EnsureBgr24OrBgra32(src);
            if (!src.IsFrozen) src.Freeze();
            bmp = src;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryLoadWithWicUri(string path, out BitmapSource? bmp)
    {
        bmp = null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile;
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.EndInit();

            BitmapSource src = bi;
            int orientation = ReadExifOrientation(bi.Metadata as BitmapMetadata);
            src = ApplyExifOrientation(src, orientation);
            src = EnsureBgr24OrBgra32(src);
            if (!src.IsFrozen) src.Freeze();
            bmp = src;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryLoadWithGdi(string path, out BitmapSource? bmp)
    {
        bmp = null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var img = Image.FromStream(fs, useEmbeddedColorManagement: false, validateImageData: false);
            using var bmpGdi = new Bitmap(img);
            IntPtr hBitmap = bmpGdi.GetHbitmap();
            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                if (!src.IsFrozen) src.Freeze();
                bmp = src;
                return true;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        catch
        {
            return false;
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private static int ReadExifOrientation(BitmapMetadata? meta)
    {
        if (meta == null) return 1;

        object? v = null;
        try { v = meta.GetQuery("/app1/ifd/{ushort=274}"); } catch { }
        if (v == null)
            try { v = meta.GetQuery("/ifd/{ushort=274}"); } catch { }

        if (v is ushort u) return u;
        if (v is short s) return s;
        return 1;
    }

    private static BitmapSource ApplyExifOrientation(BitmapSource src, int orientation)
    {
        if (orientation <= 1 || orientation > 8) return src;

        BitmapSource Rot90(BitmapSource s) => new TransformedBitmap(s, new RotateTransform(90));
        BitmapSource Rot180(BitmapSource s) => new TransformedBitmap(s, new RotateTransform(180));
        BitmapSource Rot270(BitmapSource s) => new TransformedBitmap(s, new RotateTransform(270));
        BitmapSource FlipH(BitmapSource s) => new TransformedBitmap(s, new ScaleTransform(-1, 1));
        BitmapSource FlipV(BitmapSource s) => new TransformedBitmap(s, new ScaleTransform(1, -1));

        return orientation switch
        {
            2 => FlipH(src),
            3 => Rot180(src),
            4 => FlipV(src),
            5 => FlipH(Rot90(src)),
            6 => Rot90(src),
            7 => FlipV(Rot90(src)),
            8 => Rot270(src),
            _ => src
        };
    }

    private static BitmapSource EnsureBgr24OrBgra32(BitmapSource src)
    {
        if (src.Format == PixelFormats.Bgr24 || src.Format == PixelFormats.Bgra32)
        {
            return src;
        }

        var conv = new FormatConvertedBitmap(src, PixelFormats.Bgr24, null, 0);
        conv.Freeze();
        return conv;
    }
}
