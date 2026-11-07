using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;
using NyvexGeneration.Services;

namespace NyvexGeneration.Infrastructure;

public sealed class DialogService : IDialogService
{
    public string? PickSaveFile(string title, string defaultFileName, string filter)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = defaultFileName,
            Filter = filter,
            AddExtension = true,
            OverwritePrompt = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowMessage(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetDataObject(text, true);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }

    public void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception)
        {
            // Sem ação: a pasta pode não existir mais.
        }
    }
}
