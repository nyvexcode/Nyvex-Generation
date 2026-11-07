using NyvexGeneration.Core;

namespace NyvexGeneration.Models;

public sealed class GenerationOptions
{
    public const int MinSupportedLength = 3;
    public const int MaxSupportedLength = 5;

    public int Length { get; set; } = 4;

    public CharacterSets CharacterSet { get; set; } = CharacterSets.LettersAndDigits;

    public int Quantity { get; set; } = 100;

    public string Prefix { get; set; } = string.Empty;

    public string Suffix { get; set; } = string.Empty;

    public string ExcludedCharacters { get; set; } = string.Empty;
}
