using Microsoft.Data.Sqlite;

namespace NyvexGeneration.Data;

public sealed class SqliteDatabase : IBackupService
{
    private readonly string _connectionString;
    private readonly string _filePath;

    public SqliteDatabase(string databaseFile)
    {
        if (string.IsNullOrWhiteSpace(databaseFile))
        {
            throw new ArgumentException("Caminho do banco inválido.", nameof(databaseFile));
        }

        _filePath = databaseFile;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS generation_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT NOT NULL,
                generated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_generation_username ON generation_history(username);

            CREATE TABLE IF NOT EXISTS lookup_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT NOT NULL,
                status INTEGER NOT NULL,
                failure_reason INTEGER NOT NULL,
                details TEXT NOT NULL,
                checked_at TEXT NOT NULL,
                roblox_user_id INTEGER NULL,
                matched_name TEXT NULL,
                http_status INTEGER NULL
            );
            CREATE INDEX IF NOT EXISTS ix_lookup_username ON lookup_history(username);

            CREATE TABLE IF NOT EXISTS favorites (
                username TEXT PRIMARY KEY,
                added_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS stats (
                key TEXT PRIMARY KEY,
                value INTEGER NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task BackupAsync(string destinationFile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(destinationFile))
        {
            throw new ArgumentException("Caminho de backup inválido.", nameof(destinationFile));
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(destinationFile));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(destinationFile))
        {
            File.Delete(destinationFile);
        }

        await using var source = await OpenAsync(cancellationToken).ConfigureAwait(false);
        var destinationString = new SqliteConnectionStringBuilder
        {
            DataSource = destinationFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        await using var destination = new SqliteConnection(destinationString);
        await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
        source.BackupDatabase(destination);
    }
}
