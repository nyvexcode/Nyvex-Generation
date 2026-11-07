namespace NyvexGeneration.ViewModels;

public enum DataKind
{
    GenerationHistory,
    LookupHistory,
    Favorites,
    Stats
}

/// <summary>Enviada pelo Messenger quando dados persistidos são limpos nas configurações.</summary>
public sealed record DataChangedMessage(DataKind Kind);

public sealed record NavItem(string Title, string Glyph, object Page);
