using System;
using System.Collections.Generic;
using Oahu.Cli.Tui.Tokens;
using Spectre.Console;

namespace Oahu.Cli.Tui.Themes;

/// <summary>
/// A complete palette: every widget consumes <see cref="Tokens.Tokens"/>, which in turn
/// reads from <see cref="Current"/>. Switching theme is a single <see cref="Use(string)"/>
/// call — no widget needs to subscribe.
/// </summary>
public sealed class Theme
{
    public static Theme Current { get; private set; } = Themes.Default;

    public static IReadOnlyList<Theme> Available { get; } = new[]
    {
        Themes.Default,
        Themes.Mono,
        Themes.HighContrast,
        Themes.Colorblind,
    };

    public required string Name { get; init; }

    public required SemanticColor TextPrimary { get; init; }

    public required SemanticColor TextSecondary { get; init; }

    public required SemanticColor TextTertiary { get; init; }

    public required SemanticColor StatusInfo { get; init; }

    public required SemanticColor StatusSuccess { get; init; }

    public required SemanticColor StatusWarning { get; init; }

    public required SemanticColor StatusError { get; init; }

    public required SemanticColor Brand { get; init; }

    public required SemanticColor Selected { get; init; }

    public required SemanticColor BorderNeutral { get; init; }

    public required SemanticColor BackgroundSecondary { get; init; }

    public required SemanticColor DiffAdd { get; init; }

    public required SemanticColor DiffRemove { get; init; }

    /// <summary>Switch the active theme by name (case-insensitive).</summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> matches no built-in theme.</exception>
    public static void Use(string name)
    {
        foreach (var t in Available)
        {
            if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                Current = t;
                return;
            }
        }
        throw new ArgumentException($"Unknown theme '{name}'. Known: {string.Join(", ", AvailableNames())}.", nameof(name));
    }

    /// <summary>Reset to the <see cref="Themes.Default"/> theme. Useful for tests.</summary>
    public static void Reset() => Current = Themes.Default;

    public static IEnumerable<string> AvailableNames()
    {
        foreach (var t in Available)
        {
            yield return t.Name;
        }
    }
}

/// <summary>Built-in theme palettes. Add new ones here and append to <see cref="Theme.Available"/>.</summary>
public static class Themes
{
    public static Theme Default { get; } = new()
    {
        Name = "Default",
        TextPrimary = new(Color.White),
        TextSecondary = new(Color.Grey85),
        TextTertiary = new(Color.Grey50),
        StatusInfo = new(Color.SkyBlue1),
        StatusSuccess = new(Color.Green),
        StatusWarning = new(Color.Yellow),
        StatusError = new(Color.Red),
        Brand = new(Color.Aqua),
        Selected = new(Color.DodgerBlue1),
        BorderNeutral = new(Color.Grey50),
        BackgroundSecondary = new(Color.Grey15),
        DiffAdd = new(Color.Green),
        DiffRemove = new(Color.Red),
    };

    /// <summary>
    /// Monochrome theme used automatically when <c>NO_COLOR</c> is set, when stdout is
    /// redirected, or when a screen reader is detected. Every token resolves to the
    /// terminal's default foreground so no ANSI colour escape is ever emitted.
    /// </summary>
    public static Theme Mono { get; } = new()
    {
        Name = "Mono",
        TextPrimary = new(Color.Default),
        TextSecondary = new(Color.Default),
        TextTertiary = new(Color.Default),
        StatusInfo = new(Color.Default),
        StatusSuccess = new(Color.Default),
        StatusWarning = new(Color.Default),
        StatusError = new(Color.Default),
        Brand = new(Color.Default),
        Selected = new(Color.Default),
        BorderNeutral = new(Color.Default),
        BackgroundSecondary = new(Color.Default),
        DiffAdd = new(Color.Default),
        DiffRemove = new(Color.Default),
    };

    /// <summary>
    /// High-contrast theme: maximum-contrast palette suitable for low-vision users and
    /// for the accessibility audit (APCA Lc ≥ 30 for body text, per Phase 9).
    /// </summary>
    public static Theme HighContrast { get; } = new()
    {
        Name = "HighContrast",
        TextPrimary = new(Color.White),
        TextSecondary = new(Color.White),
        TextTertiary = new(Color.Silver),
        StatusInfo = new(Color.Aqua),
        StatusSuccess = new(Color.Lime),
        StatusWarning = new(Color.Yellow),
        StatusError = new(Color.Red),
        Brand = new(Color.Aqua),
        Selected = new(Color.Yellow),
        BorderNeutral = new(Color.White),
        BackgroundSecondary = new(Color.Black),
        DiffAdd = new(Color.Lime),
        DiffRemove = new(Color.Red),
    };

    /// <summary>
    /// Colorblind-safe theme using the Okabe-Ito palette: avoids red/green
    /// pairings that the most common forms of color vision deficiency
    /// (deuteranopia, protanopia) confuse. Status semantics are conveyed by
    /// blue (info), bluish-green (success), orange/yellow (warning), and
    /// vermillion (error) — all distinguishable by deuteranopes/protanopes.
    /// </summary>
    public static Theme Colorblind { get; } = new()
    {
        Name = "Colorblind",
        TextPrimary = new(Color.White),
        TextSecondary = new(Color.Grey85),
        TextTertiary = new(Color.Grey50),
        StatusInfo = new(Color.SkyBlue1),
        StatusSuccess = new(Color.MediumSpringGreen),
        StatusWarning = new(Color.Orange1),
        StatusError = new(Color.IndianRed1),
        Brand = new(Color.MediumPurple1),
        Selected = new(Color.Yellow),
        BorderNeutral = new(Color.Grey50),
        BackgroundSecondary = new(Color.Grey15),
        DiffAdd = new(Color.SkyBlue1),
        DiffRemove = new(Color.Orange1),
    };
}
