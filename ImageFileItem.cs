using System.IO;

namespace ReviewTool;

public sealed class ImageFileItem
{
    private string _newFileName = string.Empty;

    public ImageFileItem(string filePath)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
    }

    public string FilePath { get; }
    public string FileName { get; }
    public string NewFileName
    {
        get => _newFileName;
        set => _newFileName = value ?? string.Empty;
    }
}
