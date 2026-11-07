namespace NyvexGeneration.Services;

/// <summary>
/// Regras de formato de username: 3–20 caracteres; letras, números e no máximo um "_";
/// sem "_" no início/fim; não pode ser apenas numérico.
/// </summary>
public sealed class UsernameValidator : IUsernameValidator
{
    public const int MinLength = 3;
    public const int MaxLength = 20;

    public bool IsValid(string? username, out string? error)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            error = "O username está vazio.";
            return false;
        }

        if (username.Length < MinLength || username.Length > MaxLength)
        {
            error = $"O username deve ter entre {MinLength} e {MaxLength} caracteres.";
            return false;
        }

        var underscores = 0;
        var allDigits = true;

        foreach (var c in username)
        {
            var isAsciiLetter = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
            var isDigit = c is >= '0' and <= '9';

            if (!isAsciiLetter && !isDigit && c != '_')
            {
                error = "O username só pode conter letras (A-Z), números e um underscore.";
                return false;
            }

            if (c == '_')
            {
                underscores++;
            }

            if (!isDigit)
            {
                allDigits = false;
            }
        }

        if (underscores > 1)
        {
            error = "O username pode conter no máximo um underscore.";
            return false;
        }

        if (username[0] == '_' || username[^1] == '_')
        {
            error = "O username não pode começar ou terminar com underscore.";
            return false;
        }

        if (allDigits)
        {
            error = "O username não pode conter apenas números.";
            return false;
        }

        error = null;
        return true;
    }
}
