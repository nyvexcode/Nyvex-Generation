using System.Windows;
using System.Windows.Media;

namespace NyvexGeneration.Infrastructure;

public sealed record ThemeOption(string Key, string Label, string Accent, string AccentGlow);

/// <summary>Troca as cores de destaque em tempo de execução (recursos usados com DynamicResource).</summary>
public sealed class ThemeService
{
    public static IReadOnlyList<ThemeOption> Themes { get; } = new[]
    {
        new ThemeOption("DarkNeon", "Roxo Neon", "#A855F7", "#C084FC"),
        new ThemeOption("Violet", "Violeta", "#7C3AED", "#A78BFA"),
        new ThemeOption("Magenta", "Magenta", "#D946EF", "#F0ABFC"),
        new ThemeOption("Indigo", "Índigo", "#6366F1", "#A5B4FC")
    };

    public void Apply(string? key)
    {
        var theme = Themes.FirstOrDefault(t => t.Key == key) ?? Themes[0];
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        app.Resources["AccentBrush"] = Create(theme.Accent);
        app.Resources["AccentGlowBrush"] = Create(theme.AccentGlow);
    }

    private static SolidColorBrush Create(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
