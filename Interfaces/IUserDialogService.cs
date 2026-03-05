using System.Windows;

namespace ReviewTool.Interfaces
{
    public interface IUserDialogService
    {
        MessageBoxResult ShowQuestion(string text, string title);
        void ShowInfo(string text, string title);
        void ShowError(string text, string title);
    }
}
