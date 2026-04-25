using System;
using System.IO;
using Spectre.Console;

namespace Oahu.Cli.Output;

/// <summary>
/// Builds the right <see cref="IOutputWriter"/> for the requested format. Test code
/// can substitute the underlying <see cref="TextWriter"/> / <see cref="IAnsiConsole"/>.
/// </summary>
public static class OutputWriterFactory
{
    public static IOutputWriter Create(OutputContext context, TextWriter? writer = null, IAnsiConsole? console = null)
    {
        return context.Format switch
        {
            OutputFormat.Json => new JsonOutputWriter(context, writer ?? Console.Out),
            OutputFormat.Plain => new PlainOutputWriter(context, writer ?? Console.Out),
            OutputFormat.Pretty => new PrettyOutputWriter(context, console ?? AnsiConsole.Console),
            _ => new PlainOutputWriter(context, writer ?? Console.Out),
        };
    }
}
