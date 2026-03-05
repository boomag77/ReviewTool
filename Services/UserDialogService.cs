using Microsoft.Win32;
using ReviewTool.Interfaces;
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
            var dialog = new OpenFolderDialog
            {
                Title = title,
                // If initialDirectory is null, last used directory will be opened, or MyDocuments if no last used directory
                InitialDirectory = initialDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Multiselect = false
            };
            var result = dialog.ShowDialog();
            if (result == true)
            {
                return dialog.FolderName;
            }
            return string.Empty;
        }

        public string[] SelectFolders(string title, string? initialDirectory)
        {
            var dialog = new OpenFolderDialog
            {
                Title = title,
                // If initialDirectory is null, last used directory will be opened, or MyDocuments if no last used directory
                InitialDirectory = initialDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Multiselect = true
            };
            var result = dialog.ShowDialog();
            if (result == true)
            {
                return dialog.FolderNames;
            }
            return Array.Empty<string>();
        }
    }
}