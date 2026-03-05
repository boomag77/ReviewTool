using System.Windows;

namespace ReviewTool.Interfaces
{
    public interface IUserDialogService
    {
        MessageBoxResult ShowQuestion(string text, string title);
        void ShowInfo(string text, string title);
        void ShowWarning(string text, string title);
        void ShowError(string text, string title);

        string SelectFolder(string title, string? initialDirectory);

        string[] SelectFolders(string title, string? initialDirectory);
    }
}
