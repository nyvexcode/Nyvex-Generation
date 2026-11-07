using NyvexGeneration.Core;
using NyvexGeneration.Models;
using NyvexGeneration.Services;
using Xunit;

namespace NyvexGeneration.Tests;

public class UsernameValidatorTests
{
    private readonly UsernameValidator _validator = new();

    [Theory]
    [InlineData("abc")]
    [InlineData("a1b2")]
    [InlineData("ab_cd")]
    [InlineData("Nyvex9")]
    [InlineData("abcdefghijklmnopqrst")]
    public void AcceptsValidUsernames(string name)
    {
        Assert.True(_validator.IsValid(name, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    [InlineData("abcdefghijklmnopqrstu")]
    [InlineData("_abc")]
    [InlineData("abc_")]
    [InlineData("a_b_c")]
    [InlineData("1234")]
    [InlineData("ab cd")]
    [InlineData("ab-cd")]
    [InlineData("açb")]
    public void RejectsInvalidUsernames(string? name)
    {
        Assert.False(_validator.IsValid(name, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}

public class UsernameGeneratorTests
{
    private static UsernameGenerator Create(int seed = 42)
    {
        var random = new Random(seed);
        return new UsernameGenerator(new UsernameValidator(), random.Next);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void GeneratesRequestedLengthAndQuantity(int length)
    {
        var result = Create().Generate(new GenerationOptions { Length = length, Quantity = 200 });

        Assert.Equal(200, result.Count);
        Assert.All(result, u => Assert.Equal(length, u.Length));
    }

    [Fact]
    public void NeverProducesDuplicates()
    {
        var result = Create().Generate(new GenerationOptions { Length = 4, Quantity = 3000 });

        Assert.Equal(result.Count, result.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void OnlyLettersProducesOnlyLowercaseLetters()
    {
        var result = Create().Generate(new GenerationOptions { Length = 4, Quantity = 200, CharacterSet = CharacterSets.Letters });

        Assert.All(result, u => Assert.True(u.All(c => c is >= 'a' and <= 'z')));
    }

    [Fact]
    public void LettersAndDigitsNeverProducesAllDigits()
    {
        var result = Create().Generate(new GenerationOptions { Length = 3, Quantity = 5000 });
        var validator = new UsernameValidator();

        Assert.All(result, u => Assert.True(validator.IsValid(u, out _)));
    }

    [Fact]
    public void DigitsOnlyYieldsNothingBecauseAllNumericIsInvalid()
    {
        var result = Create().Generate(new GenerationOptions { Length = 4, Quantity = 50, CharacterSet = CharacterSets.Digits });

        Assert.Empty(result);
    }

    [Fact]
    public void AppliesPrefixAndSuffixKeepingTotalLength()
    {
        var result = Create().Generate(new GenerationOptions { Length = 5, Quantity = 100, Prefix = "Ny", Suffix = "x" });

        Assert.All(result, u =>
        {
            Assert.Equal(5, u.Length);
            Assert.StartsWith("ny", u);
            Assert.EndsWith("x", u);
        });
    }

    [Fact]
    public void ExcludesRequestedCharacters()
    {
        var result = Create().Generate(new GenerationOptions { Length = 4, Quantity = 500, ExcludedCharacters = "aeiou01" });

        Assert.All(result, u => Assert.DoesNotContain(u, c => "aeiou01".Contains(c)));
    }

    [Fact]
    public void SkipsUsernamesInExcludeSet()
    {
        var generator = Create();
        var first = generator.Generate(new GenerationOptions { Length = 3, Quantity = 500, CharacterSet = CharacterSets.Letters });
        var exclude = new HashSet<string>(first);

        var second = Create(7).Generate(new GenerationOptions { Length = 3, Quantity = 500, CharacterSet = CharacterSets.Letters }, exclude);

        Assert.Empty(second.Intersect(first));
        Assert.Equal(500, second.Count);
    }

    [Fact]
    public void ExhaustsSmallSpaceCompletelyWithoutDuplicates()
    {
        var result = Create().Generate(new GenerationOptions { Length = 3, Quantity = 17_576, CharacterSet = CharacterSets.Letters });

        Assert.Equal(17_576, result.Count);
        Assert.Equal(17_576, result.Distinct().Count());
    }

    [Fact]
    public void ReturnsFewerWhenSpaceIsSmallerThanQuantity()
    {
        var options = new GenerationOptions
        {
            Length = 4, Quantity = 100, CharacterSet = CharacterSets.Letters,
            Prefix = "abc", ExcludedCharacters = "bcdefghijklmnopqrstuvwxyz"
        };

        // Resta apenas "a" para o último caractere: um único candidato possível.
        var result = Create().Generate(options);

        Assert.Single(result);
        Assert.Equal("abca", result[0]);
    }

    [Fact]
    public void SameSeedIsDeterministic()
    {
        var options = new GenerationOptions { Length = 4, Quantity = 50 };

        Assert.Equal(Create(1).Generate(options), Create(1).Generate(options));
    }

    [Theory]
    [InlineData(2, 10, "", "", CharacterSets.LettersAndDigits)]
    [InlineData(6, 10, "", "", CharacterSets.LettersAndDigits)]
    [InlineData(4, 0, "", "", CharacterSets.LettersAndDigits)]
    [InlineData(4, 10, "ab", "cd", CharacterSets.LettersAndDigits)]
    [InlineData(4, 10, "a_", "", CharacterSets.LettersAndDigits)]
    [InlineData(4, 10, "", "", CharacterSets.None)]
    public void ValidateOptionsRejectsBadInput(int length, int quantity, string prefix, string suffix, CharacterSets set)
    {
        var generator = Create();
        var options = new GenerationOptions { Length = length, Quantity = quantity, Prefix = prefix, Suffix = suffix, CharacterSet = set };

        Assert.NotNull(generator.ValidateOptions(options));
        Assert.Throws<ArgumentException>(() => generator.Generate(options));
    }

    [Fact]
    public void ValidateOptionsRejectsWhenExclusionsRemoveEverything()
    {
        var options = new GenerationOptions { CharacterSet = CharacterSets.Digits, ExcludedCharacters = "0123456789" };

        Assert.NotNull(Create().ValidateOptions(options));
    }

    [Fact]
    public void ValidateOptionsAcceptsDefaults()
    {
        Assert.Null(Create().ValidateOptions(new GenerationOptions()));
    }
}
