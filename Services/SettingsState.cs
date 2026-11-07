using NyvexGeneration.Models;

namespace NyvexGeneration.Services;

/// <summary>Configurações vigentes em memória, lidas pelos serviços a cada operação.</summary>
public sealed class SettingsState
{
    private AppSettings _current = new AppSettings().Normalize();

    public event EventHandler? Changed;

    public AppSettings Current => _current;

    public void Update(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _current = settings.Clone().Normalize();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
