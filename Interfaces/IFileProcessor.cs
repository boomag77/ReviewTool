using System.Windows.Media.Imaging;

namespace ReviewTool.Interfaces
{
    public interface IFileProcessor
    {
        bool IsSupportedImageFile(string filePath);
        IReadOnlyList<string> GetImageFilesInDirectory(string directoryPath);
        void SaveFile(string origFilePath, string initialReviewFolder, string notMappedFileName);
        BitmapSource LoadBitmapImage(string imageFilePath);
        string GetInitialReviewFolderPath(string originalImagesFolderPath);
        string EnsureInitialReviewFolder(string originalImagesFolderPath);

        Task<int> GetMaxDigitsInImageFiles(string originalFolder);
        void ClearDirectory(string directoryPath);
    }
}
