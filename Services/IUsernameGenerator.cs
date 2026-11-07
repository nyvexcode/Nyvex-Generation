using NyvexGeneration.Models;

namespace NyvexGeneration.Services;

public interface IUsernameGenerator
{
    /// <summary>Retorna uma mensagem de erro se as opções forem inválidas; caso contrário, null.</summary>
    string? ValidateOptions(GenerationOptions options);

    /// <summary>
    /// Gera usernames únicos (minúsculos). Usernames presentes em <paramref name="exclude"/> são ignorados.
    /// Pode retornar menos que o solicitado se o espaço de combinações se esgotar.
    /// </summary>
    IReadOnlyList<string> Generate(GenerationOptions options, IReadOnlySet<string>? exclude = null);
}

public interface IUsernameValidator
{
    bool IsValid(string? username, out string? error);
}
