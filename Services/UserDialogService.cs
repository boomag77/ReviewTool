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
        public void ShowError(string text, string title) =>
            MessageBox.Show(_owner, text, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
