using ReviewTool.Models;
using ReviewTool.Interfaces;

namespace ReviewTool.Services
{
    internal sealed class FileSystemService
    {
        private readonly FileProcessor _fileProcessor;

        public FileSystemService()
        {
            _fileProcessor = new FileProcessor();
        }

        public IReadOnlyList<string> GetImageFilesInDirectory(string directoryPath)
        {
            return _fileProcessor.GetImageFilesInDirectory(directoryPath);
        }
    }
}
