using NyvexGeneration.Core;
using NyvexGeneration.Models;

namespace NyvexGeneration.Services;

public interface ILogService
{
    /// <summary>Disparado a cada novo registro, possivelmente em outra thread.</summary>
    event EventHandler<LogEntry>? EntryAdded;

    /// <summary>Registra um evento. Credenciais e tokens são removidos automaticamente.</summary>
    void Log(LogSeverity severity, string category, string message, string? technicalDetails = null);

    IReadOnlyList<LogEntry> GetRecent(int count);

    /// <summary>Limpa os registros em memória (os arquivos de log permanecem).</summary>
    void Clear();
}
