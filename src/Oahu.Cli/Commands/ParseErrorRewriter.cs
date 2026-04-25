using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;

namespace Oahu.Cli.Commands;

/// <summary>
/// Catches parse errors before <c>InvokeAsync</c> and emits the design-spec
/// formatting (per §4.2 / §10): a short error, a "Try …help" footer, and a
/// "Did you mean: <i>x</i>" suggestion for unknown subcommands when there is
/// a close match (Levenshtein ≤ 2 within the eligible command set).
/// </summary>
internal static class ParseErrorRewriter
{
    public const string HelpHint = "Try 'oahu-cli --help' for more information.";

    /// <summary>
    /// Inspects <paramref name="parseResult"/>; if there are errors writes them
    /// to <paramref name="error"/> in the canonical format and returns the
    /// <c>2</c> usage exit code. Returns <c>null</c> when no rewriting is needed
    /// (caller should proceed to invoke).
    /// </summary>
    public static int? RewriteIfNeeded(ParseResult parseResult, TextWriter error)
    {
        if (parseResult.Errors.Count == 0)
        {
            return null;
        }

        foreach (var err in parseResult.Errors)
        {
            error.WriteLine($"oahu-cli: {err.Message}");
        }

        var suggestion = SuggestSubcommand(parseResult);
        if (suggestion is not null)
        {
            error.WriteLine();
            error.WriteLine($"Did you mean: oahu-cli {suggestion}?");
        }

        error.WriteLine();
        error.WriteLine(HelpHint);
        return 2;
    }

    /// <summary>
    /// Looks for the first unrecognised token at the root level and proposes a
    /// nearest-neighbour subcommand. Returns null if no plausible match.
    /// </summary>
    public static string? SuggestSubcommand(ParseResult parseResult)
    {
        var unmatched = parseResult.UnmatchedTokens;
        if (unmatched.Count == 0)
        {
            return null;
        }

        var first = unmatched[0];
        if (string.IsNullOrEmpty(first) || first.StartsWith('-'))
        {
            return null;
        }

        var commands = parseResult.RootCommandResult.Command.Subcommands
            .SelectMany(c => new[] { c.Name }.Concat(c.Aliases))
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return SuggestNearest(first, commands);
    }

    public static string? SuggestNearest(string input, IReadOnlyList<string> candidates, int maxDistance = 2)
    {
        if (string.IsNullOrEmpty(input) || candidates.Count == 0)
        {
            return null;
        }

        string? best = null;
        int bestDist = int.MaxValue;
        foreach (var c in candidates)
        {
            var d = Levenshtein(input, c);
            if (d < bestDist)
            {
                bestDist = d;
                best = c;
            }
        }
        return bestDist <= maxDistance ? best : null;
    }

    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }
        if (b.Length == 0)
        {
            return a.Length;
        }

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }
        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }
}
