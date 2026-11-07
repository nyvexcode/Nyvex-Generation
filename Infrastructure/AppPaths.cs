namespace NyvexGeneration.Infrastructure;

/// <summary>
/// Centraliza os caminhos de armazenamento local. Os diretórios são criados na construção.
/// </summary>
public sealed class AppPaths
{
    public AppPaths()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NyvexGeneration"))
    {
    }

    public AppPaths(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("O diretório raiz não pode ser vazio.", nameof(rootDirectory));
        }

        RootDirectory = rootDirectory;
        LogsDirectory = Path.Combine(rootDirectory, "logs");
        BackupsDirectory = Path.Combine(rootDirectory, "backups");
        ExportsDirectory = Path.Combine(rootDirectory, "exports");
        DatabaseFile = Path.Combine(rootDirectory, "nyvex.db");

        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(ExportsDirectory);
    }

    public string RootDirectory { get; }

    public string LogsDirectory { get; }

    public string BackupsDirectory { get; }

    public string ExportsDirectory { get; }

    public string DatabaseFile { get; }
}
