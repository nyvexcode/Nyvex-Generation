namespace NyvexGeneration.Core;

/// <summary>
/// Resultado de uma consulta pública. Nenhum valor significa "username livre":
/// a busca pública não confirma disponibilidade de registro.
/// </summary>
public enum LookupStatus
{
    NotChecked = 0,
    Checking = 1,

    /// <summary>A busca pública retornou uma conta com este username.</summary>
    AccountFound = 2,

    /// <summary>A busca pública não retornou correspondência. Disponibilidade NÃO confirmada.</summary>
    NoPublicMatch = 3,

    /// <summary>A consulta não produziu resposta utilizável (429, timeout, resposta inválida).</summary>
    Inconclusive = 4,

    /// <summary>Falha de rede ou conexão.</summary>
    ConnectionError = 5
}
