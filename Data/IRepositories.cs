using NyvexGeneration.Models;

namespace NyvexGeneration.Data;

public interface ILookupHistoryRepository
{
    Task SaveAsync(IEnumerable<LookupResult> results, CancellationToken cancellationToken);

    /// <summary>Retorna o resultado mais recente de cada username, do mais novo ao mais antigo.</summary>
    Task<IReadOnlyList<LookupResult>> GetRecentAsync(int limit, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}

public interface IGenerationHistoryRepository
{
    Task SaveAsync(IEnumerable<string> usernames, DateTimeOffset generatedAt, CancellationToken cancellationToken);

    Task<IReadOnlySet<string>> GetAllUsernamesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<GenerationHistoryItem>> GetRecentAsync(int limit, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}

public interface IFavoritesRepository
{
    Task AddAsync(string username, CancellationToken cancellationToken);

    Task RemoveAsync(string username, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetAllAsync(CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}

public interface ISettingsRepository
{
    /// <summary>Retorna as configurações salvas ou os padrões se não houver nada salvo.</summary>
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}

public interface IStatsRepository
{
    Task AddAsync(string key, long delta, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, long>> GetAllAsync(CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}

public interface IBackupService
{
    /// <summary>Copia o banco para o arquivo de destino (sobrescreve se existir).</summary>
    Task BackupAsync(string destinationFile, CancellationToken cancellationToken);
}
