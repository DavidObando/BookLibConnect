using Spectre.Console;

namespace Oahu.Cli.Tui.Tokens;

/// <summary>
/// A single semantic colour token: a Spectre <see cref="Color"/> wrapped in a strong type
/// so widgets compose with intent (<c>StatusError</c>) rather than raw colour (<c>Red</c>).
/// </summary>
/// <remarks>
/// The type is implicitly convertible to <see cref="Color"/> and to a Spectre <see cref="Style"/>
/// so it can be used wherever Spectre expects either.
/// </remarks>
public readonly record struct SemanticColor(Color Value)
{
    public static implicit operator Color(SemanticColor c) => c.Value;

    public static implicit operator Style(SemanticColor c) => new(c.Value);

    /// <summary>Returns a Spectre markup-fragment opening tag for this colour, e.g. <c>[red]</c>.</summary>
    public string MarkupOpen() => $"[{Value.ToMarkup()}]";

    public override string ToString() => Value.ToMarkup();
}
