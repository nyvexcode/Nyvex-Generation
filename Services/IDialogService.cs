namespace NyvexGeneration.Services;

public interface IDialogService
{
    /// <summary>Abre o diálogo "Salvar como". Retorna null se o usuário cancelar.</summary>
    string? PickSaveFile(string title, string defaultFileName, string filter);

    bool Confirm(string title, string message);

    void ShowMessage(string title, string message);

    /// <summary>Copia texto para a área de transferência. Retorna false se falhar.</summary>
    bool CopyToClipboard(string text);

    void OpenFolder(string path);
}
