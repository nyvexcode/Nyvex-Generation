using NyvexGeneration.Models;

namespace NyvexGeneration.Services;

public interface IRobloxApiService
{
    /// <summary>Consulta um username via endpoint público. Implementação na Etapa 4.</summary>
    Task<LookupResult> CheckUsernameAsync(string username, CancellationToken cancellationToken);

    /// <summary>Consulta vários usernames em lote, respeitando limites. Implementação na Etapa 4.</summary>
    Task<IReadOnlyList<LookupResult>> CheckBatchAsync(
        IReadOnlyCollection<string> usernames,
        CancellationToken cancellationToken);
}
