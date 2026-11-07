using System.Security.Cryptography;
using NyvexGeneration.Core;
using NyvexGeneration.Models;

namespace NyvexGeneration.Services;

public sealed class UsernameGenerator : IUsernameGenerator
{
    public const int MaxQuantity = 100_000;

    private const string LetterAlphabet = "abcdefghijklmnopqrstuvwxyz";
    private const string DigitAlphabet = "0123456789";
    private const long EnumerationLimit = 300_000;

    private readonly IUsernameValidator _validator;
    private readonly Func<int, int> _nextInt;

    public UsernameGenerator(IUsernameValidator validator)
        : this(validator, RandomNumberGenerator.GetInt32)
    {
    }

    /// <param name="nextInt">Retorna um inteiro em [0, limiteExclusivo). Injetável para testes determinísticos.</param>
    public UsernameGenerator(IUsernameValidator validator, Func<int, int> nextInt)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _nextInt = nextInt ?? throw new ArgumentNullException(nameof(nextInt));
    }

    public string? ValidateOptions(GenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Length < GenerationOptions.MinSupportedLength || options.Length > GenerationOptions.MaxSupportedLength)
        {
            return $"O comprimento deve estar entre {GenerationOptions.MinSupportedLength} e {GenerationOptions.MaxSupportedLength}.";
        }

        if (options.Quantity < 1 || options.Quantity > MaxQuantity)
        {
            return $"A quantidade deve estar entre 1 e {MaxQuantity}.";
        }

        var prefix = Normalize(options.Prefix);
        var suffix = Normalize(options.Suffix);

        if (!IsAlphanumeric(prefix) || !IsAlphanumeric(suffix))
        {
            return "Prefixo e sufixo aceitam apenas letras e números.";
        }

        if (prefix.Length + suffix.Length >= options.Length)
        {
            return "Prefixo e sufixo ocupam todo o comprimento; reduza-os ou aumente o comprimento.";
        }

        if (BuildAlphabet(options).Length == 0)
        {
            return "Nenhum caractere disponível: escolha um conjunto ou reduza as exclusões.";
        }

        return null;
    }

    public IReadOnlyList<string> Generate(GenerationOptions options, IReadOnlySet<string>? exclude = null)
    {
        var error = ValidateOptions(options);
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(options));
        }

        var prefix = Normalize(options.Prefix);
        var suffix = Normalize(options.Suffix);
        var alphabet = BuildAlphabet(options);
        var randomLength = options.Length - prefix.Length - suffix.Length;
        var capacity = Capacity(alphabet.Length, randomLength);

        return capacity <= EnumerationLimit && options.Quantity * 2L > capacity
            ? GenerateByEnumeration(options.Quantity, prefix, suffix, alphabet, randomLength, exclude)
            : GenerateRandomly(options.Quantity, prefix, suffix, alphabet, randomLength, exclude);
    }

    private List<string> GenerateRandomly(
        int quantity, string prefix, string suffix, string alphabet, int randomLength, IReadOnlySet<string>? exclude)
    {
        var results = new List<string>(quantity);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var chars = new char[randomLength];
        var maxAttempts = quantity * 20L + 2000;

        for (long attempt = 0; attempt < maxAttempts && results.Count < quantity; attempt++)
        {
            for (var i = 0; i < randomLength; i++)
            {
                chars[i] = alphabet[_nextInt(alphabet.Length)];
            }

            var candidate = prefix + new string(chars) + suffix;
            if (Accept(candidate, seen, exclude))
            {
                results.Add(candidate);
            }
        }

        return results;
    }

    private List<string> GenerateByEnumeration(
        int quantity, string prefix, string suffix, string alphabet, int randomLength, IReadOnlySet<string>? exclude)
    {
        var capacity = Capacity(alphabet.Length, randomLength);
        var all = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var chars = new char[randomLength];

        for (long index = 0; index < capacity; index++)
        {
            var value = index;
            for (var i = randomLength - 1; i >= 0; i--)
            {
                chars[i] = alphabet[(int)(value % alphabet.Length)];
                value /= alphabet.Length;
            }

            var candidate = prefix + new string(chars) + suffix;
            if (Accept(candidate, seen, exclude))
            {
                all.Add(candidate);
            }
        }

        // Fisher-Yates
        for (var i = all.Count - 1; i > 0; i--)
        {
            var j = _nextInt(i + 1);
            (all[i], all[j]) = (all[j], all[i]);
        }

        return all.Count > quantity ? all.GetRange(0, quantity) : all;
    }

    private bool Accept(string candidate, HashSet<string> seen, IReadOnlySet<string>? exclude)
    {
        if (exclude is not null && exclude.Contains(candidate))
        {
            return false;
        }

        if (!_validator.IsValid(candidate, out _))
        {
            return false;
        }

        return seen.Add(candidate);
    }

    private static string BuildAlphabet(GenerationOptions options)
    {
        var builder = new System.Text.StringBuilder();

        if (options.CharacterSet.HasFlag(CharacterSets.Letters))
        {
            builder.Append(LetterAlphabet);
        }

        if (options.CharacterSet.HasFlag(CharacterSets.Digits))
        {
            builder.Append(DigitAlphabet);
        }

        var excluded = Normalize(options.ExcludedCharacters);
        return excluded.Length == 0
            ? builder.ToString()
            : new string(builder.ToString().Where(c => !excluded.Contains(c)).ToArray());
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static bool IsAlphanumeric(string value) => value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9');

    private static long Capacity(int alphabetSize, int length)
    {
        long capacity = 1;
        for (var i = 0; i < length; i++)
        {
            capacity *= alphabetSize;
            if (capacity > long.MaxValue / 64)
            {
                return long.MaxValue / 64;
            }
        }

        return capacity;
    }
}
