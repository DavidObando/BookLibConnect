using System.CommandLine;
using System.IO;
using Microsoft.Extensions.Logging;
using Oahu.Cli.Commands;
using Xunit;

namespace Oahu.Cli.Tests.Commands;

public class ParseErrorRewriterTests
{
    private static RootCommand BuildRoot()
        => RootCommandFactory.Create(() => new LoggerFactory());

    [Fact]
    public void NoErrors_ReturnsNull()
    {
        var root = BuildRoot();
        var pr = root.Parse(new[] { "doctor", "--skip-network", "--json" });
        var sw = new StringWriter();
        Assert.Null(ParseErrorRewriter.RewriteIfNeeded(pr, sw));
        Assert.Empty(sw.ToString());
    }

    [Fact]
    public void UnknownSubcommand_AppendsHelpHintAndSuggestion()
    {
        var root = BuildRoot();
        var pr = root.Parse(new[] { "doctorr" });
        var sw = new StringWriter();
        var code = ParseErrorRewriter.RewriteIfNeeded(pr, sw);
        Assert.Equal(2, code);
        var output = sw.ToString();
        Assert.Contains("Did you mean: oahu-cli doctor", output);
        Assert.Contains(ParseErrorRewriter.HelpHint, output);
    }

    [Fact]
    public void UnknownSubcommand_NoCloseMatch_OmitsSuggestion()
    {
        var root = BuildRoot();
        var pr = root.Parse(new[] { "xyzzy" });
        var sw = new StringWriter();
        var code = ParseErrorRewriter.RewriteIfNeeded(pr, sw);
        Assert.Equal(2, code);
        Assert.DoesNotContain("Did you mean", sw.ToString());
        Assert.Contains(ParseErrorRewriter.HelpHint, sw.ToString());
    }

    [Theory]
    [InlineData("kitten", new[] { "kitchen", "kitten", "mitten" }, "kitten")]
    [InlineData("docter", new[] { "doctor", "queue", "config" }, "doctor")]
    [InlineData("zzzzzzz", new[] { "doctor", "config" }, null)]
    public void SuggestNearest_PicksClosestWithinThreshold(string input, string[] candidates, string? expected)
    {
        Assert.Equal(expected, ParseErrorRewriter.SuggestNearest(input, candidates));
    }
}
