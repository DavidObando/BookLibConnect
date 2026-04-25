using System;
using System.Text;
using Oahu.Cli.App.Doctor;
using Oahu.Cli.App.Paths;
using Spectre.Console;

namespace Oahu.Cli.Commands;

/// <summary>
/// Pure rendering of a <see cref="DoctorReport"/> in pretty / JSON formats.
/// Separated from <see cref="DoctorCommand"/> so it can be unit-tested without
/// going through System.CommandLine.
/// </summary>
public static class DoctorRender
{
    public static void Pretty(DoctorReport report, GlobalOptions globals)
    {
        var console = SpectreConsoleFactory.Create(globals);

        var table = new Table()
            .Border(globals.UseAscii ? TableBorder.Ascii : TableBorder.Rounded)
            .AddColumn(new TableColumn("[bold]Status[/]").NoWrap())
            .AddColumn(new TableColumn("[bold]Check[/]"))
            .AddColumn(new TableColumn("[bold]Detail[/]"));

        foreach (var c in report.Checks)
        {
            var (icon, colour) = c.Severity switch
            {
                DoctorSeverity.Ok => (globals.UseAscii ? "OK " : "✓", "green"),
                DoctorSeverity.Warning => (globals.UseAscii ? "WARN" : "!", "yellow"),
                DoctorSeverity.Error => (globals.UseAscii ? "FAIL" : "✗", "red"),
                _ => ("?", "grey"),
            };
            table.AddRow(
                $"[{colour}]{Markup.Escape(icon)}[/]",
                Markup.Escape(c.Title),
                Markup.Escape(c.Message));

            if (!string.IsNullOrEmpty(c.Hint))
            {
                table.AddRow(string.Empty, string.Empty, $"[grey]→ {Markup.Escape(c.Hint!)}[/]");
            }
        }

        console.Write(table);
        console.MarkupLine(string.Empty);
        console.MarkupLine($"[grey]Config dir:[/] {Markup.Escape(CliPaths.ConfigDir)}");
        console.MarkupLine($"[grey]Log dir:   [/] {Markup.Escape(CliPaths.LogDir)}");
        console.MarkupLine($"[grey]User-data: [/] {Markup.Escape(CliPaths.SharedUserDataDir)}");

        if (report.HasErrors)
        {
            console.MarkupLine(string.Empty);
            console.MarkupLine("[red]✗ doctor found errors. Fix them and re-run.[/]");
        }
        else if (report.HasWarnings)
        {
            console.MarkupLine(string.Empty);
            console.MarkupLine("[yellow]! doctor completed with warnings.[/]");
        }
        else
        {
            console.MarkupLine(string.Empty);
            console.MarkupLine("[green]✓ doctor: all checks passed.[/]");
        }
    }

    public static void Json(DoctorReport report)
    {
        var sb = new StringBuilder();
        sb.Append("{\"_schemaVersion\":1,\"hasErrors\":")
          .Append(report.HasErrors ? "true" : "false")
          .Append(",\"hasWarnings\":")
          .Append(report.HasWarnings ? "true" : "false")
          .Append(",\"checks\":[");

        for (var i = 0; i < report.Checks.Count; i++)
        {
            var c = report.Checks[i];
            if (i > 0)
            {
                sb.Append(',');
            }
            sb.Append('{')
              .Append("\"id\":").Append(JsonString(c.Id)).Append(',')
              .Append("\"title\":").Append(JsonString(c.Title)).Append(',')
              .Append("\"severity\":").Append(JsonString(c.Severity.ToString().ToLowerInvariant())).Append(',')
              .Append("\"message\":").Append(JsonString(c.Message));
            if (!string.IsNullOrEmpty(c.Hint))
            {
                sb.Append(',').Append("\"hint\":").Append(JsonString(c.Hint!));
            }
            sb.Append('}');
        }

        sb.Append("]}");
        CliEnvironment.Out.WriteLine(sb.ToString());
    }

    private static string JsonString(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20)
                    {
                        sb.Append("\\u").Append(((int)ch).ToString("X4"));
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
