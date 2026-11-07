namespace NyvexGeneration.Core;

[Flags]
public enum CharacterSets
{
    None = 0,
    Letters = 1,
    Digits = 2,
    LettersAndDigits = Letters | Digits
}
