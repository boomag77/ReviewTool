using Microsoft.Win32;
using ReviewTool.Interfaces;
using ReviewTool.Models;
using System.IO;
using System.Windows;

namespace ReviewTool.Services
{
    public sealed class UserDialogService : IUserDialogService
    {
        private readonly Window _owner;

        public UserDialogService(Window owner)
        {
            _owner = owner;
        }

        public MessageBoxResult ShowQuestion(string text, string title) =>
            MessageBox.Show(_owner, text, title, MessageBoxButton.YesNo, MessageBoxImage.Question);

        public void ShowInfo(string text, string title) =>
            MessageBox.Show(_owner, text, title, MessageBoxButton.OK, MessageBoxImage.Information);

        public void ShowWarning(string text, string title) =>
            MessageBox.Show(_owner, text, title, MessageBoxButton.OK, MessageBoxImage.Warning);

        public void ShowError(string text, string title) =>
            MessageBox.Show(_owner, text, title, MessageBoxButton.OK, MessageBoxImage.Error);

        public string SelectFolder(string title, string? initialDirectory)
        {
            var initialLoadDirectory = ResolveInitialLoadDirectory(initialDirectory);
            var dialog = new OpenFolderDialog
            {
                Title = title,
                InitialDirectory = initialLoadDirectory,
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                PersistLastLoadDirectory(dialog.FolderName);
                return dialog.FolderName;
            }

            return string.Empty;
        }

        public string[] SelectFolders(string title, string? initialDirectory)
        {
            var initialLoadDirectory = ResolveInitialLoadDirectory(initialDirectory);
            var dialog = new OpenFolderDialog
            {
                Title = title,
                InitialDirectory = initialLoadDirectory,
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                if (dialog.FolderNames.Length > 0)
                {
                    PersistLastLoadDirectory(dialog.FolderNames[0]);
                }

                return dialog.FolderNames;
            }

            return Array.Empty<string>();
        }

        private static string ResolveInitialLoadDirectory(string? requestedInitialDirectory)
        {
            if (!string.IsNullOrWhiteSpace(requestedInitialDirectory) && Directory.Exists(requestedInitialDirectory))
            {
                return requestedInitialDirectory;
            }

            if (AppSettings.TryLoadLastFolderPaths(out var lastFolderToLoad, out _, out _)
                && !string.IsNullOrWhiteSpace(lastFolderToLoad)
                && Directory.Exists(lastFolderToLoad))
            {
                return lastFolderToLoad;
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        private static void PersistLastLoadDirectory(string selectedFolderPath)
        {
            if (string.IsNullOrWhiteSpace(selectedFolderPath))
            {
                return;
            }

            _ = AppSettings.TrySaveLastFolderPaths(selectedFolderPath, null, out _);
        }
    }
}
