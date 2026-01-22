using System.IO;

namespace ReviewTool;

public sealed class ImageFileItem
{
    public ImageFileItem(string filePath)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
    }

    public string FilePath { get; }
    public string FileName { get; }
}
