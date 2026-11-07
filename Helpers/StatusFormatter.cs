using NyvexGeneration.Core;

namespace NyvexGeneration.Helpers;

public static class StatusFormatter
{
    public static string Text(LookupStatus status) => status switch
    {
        LookupStatus.NotChecked => "Não consultado",
        LookupStatus.Checking => "Consultando…",
        LookupStatus.AccountFound => "Conta encontrada",
        LookupStatus.NoPublicMatch => "Nenhuma correspondência",
        LookupStatus.Inconclusive => "Consulta inconclusiva",
        LookupStatus.ConnectionError => "Erro de conexão",
        _ => status.ToString()
    };

    public static string ConnectionText(ConnectionState state) => state switch
    {
        ConnectionState.Online => "Roblox acessível",
        ConnectionState.RateLimited => "Limitado (HTTP 429)",
        ConnectionState.Offline => "Sem conexão",
        _ => "Aguardando consulta"
    };
}
