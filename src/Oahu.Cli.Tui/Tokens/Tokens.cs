using Oahu.Cli.Tui.Themes;

namespace Oahu.Cli.Tui.Tokens;

/// <summary>
/// The single set of semantic tokens every widget reads from. Components consume tokens,
/// never raw <see cref="Spectre.Console.Color"/> values — that way swapping the theme (Mono /
/// HighContrast / Default) recolours the whole UI in one place.
///
/// The properties forward to <see cref="Theme.Current"/> at read time so callers always see
/// the live theme without needing to subscribe to change events.
/// </summary>
public static class Tokens
{
    public static SemanticColor TextPrimary => Theme.Current.TextPrimary;

    public static SemanticColor TextSecondary => Theme.Current.TextSecondary;

    public static SemanticColor TextTertiary => Theme.Current.TextTertiary;

    public static SemanticColor StatusInfo => Theme.Current.StatusInfo;

    public static SemanticColor StatusSuccess => Theme.Current.StatusSuccess;

    public static SemanticColor StatusWarning => Theme.Current.StatusWarning;

    public static SemanticColor StatusError => Theme.Current.StatusError;

    public static SemanticColor Brand => Theme.Current.Brand;

    public static SemanticColor Selected => Theme.Current.Selected;

    public static SemanticColor BorderNeutral => Theme.Current.BorderNeutral;

    public static SemanticColor BackgroundSecondary => Theme.Current.BackgroundSecondary;

    public static SemanticColor DiffAdd => Theme.Current.DiffAdd;

    public static SemanticColor DiffRemove => Theme.Current.DiffRemove;
}
