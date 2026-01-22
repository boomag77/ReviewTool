using System.IO;

namespace ReviewTool;

public sealed class FileProcessor
{
    public string EnsureInitialReviewFolder(string originalFolder)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(originalFolder);
        var folderName = Path.GetFileName(trimmed);
        var parent = Path.GetDirectoryName(trimmed);
        var initialName = $"{folderName}_initial";
        var initialFolder = string.IsNullOrWhiteSpace(parent) ? initialName : Path.Combine(parent, initialName);
        EnsureDirectory(initialFolder);
        return initialFolder;
    }

    public string EnsureRejectedFolder(string initialFolder)
    {
        var rejectedFolder = Path.Combine(initialFolder, "Rejected");
        EnsureDirectory(rejectedFolder);
        return rejectedFolder;
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

    private void EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }
}
