namespace NyvexGeneration.Services;

public interface ICheckCoordinator
{
    bool IsRunning { get; }

    /// <summary>
    /// Consulta os usernames em lotes, atualizando o estado da sessão.
    /// Retorna quantos resultados foram entregues. Duplicados e já consultados são ignorados (exceto com <paramref name="force"/>).
    /// </summary>
    Task<int> RunAsync(IReadOnlyCollection<string> usernames, bool force, CancellationToken cancellationToken = default);

    /// <summary>Solicita cancelamento seguro da execução atual.</summary>
    void Cancel();
}
