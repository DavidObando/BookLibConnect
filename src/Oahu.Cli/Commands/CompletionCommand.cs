using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using Oahu.Cli.App.Errors;

namespace Oahu.Cli.Commands;

/// <summary>
/// <c>oahu-cli completion &lt;shell&gt;</c> — emit a static shell-completion script
/// for one of bash/zsh/fish/pwsh.
///
/// The scripts delegate to <c>oahu-cli</c> for the canonical subcommand list.
/// They intentionally hard-code the v1 surface so completions work on systems
/// that block subprocess calls during tab-completion (e.g. corp-managed pwsh).
/// </summary>
public static class CompletionCommand
{
    public static readonly string[] SupportedShells = new[] { "bash", "zsh", "fish", "pwsh" };

    public static readonly string[] V1Subcommands = new[]
    {
        "tui", "doctor", "ui-preview",
        "auth", "library", "queue", "download", "convert",
        "history", "config", "completion",
    };

    public static Command Create()
    {
        var shellArg = new Argument<string>("shell")
        {
            Description = "One of: bash, zsh, fish, pwsh.",
        };
        shellArg.AcceptOnlyFromAmong(SupportedShells);
        var cmd = new Command("completion", "Print a shell-completion script for the named shell.")
        {
            shellArg,
        };
        cmd.SetAction(parse =>
        {
            var shell = parse.GetValue(shellArg)!;
            var script = Render(shell);
            CliEnvironment.Out.Write(script);
            return ExitCodes.Success;
        });
        return cmd;
    }

    public static string Render(string shell) => shell.ToLowerInvariant() switch
    {
        "bash" => RenderBash(),
        "zsh" => RenderZsh(),
        "fish" => RenderFish(),
        "pwsh" => RenderPwsh(),
        _ => throw new ArgumentException($"Unsupported shell '{shell}'. Valid: {string.Join(", ", SupportedShells)}"),
    };

    private static string RenderBash()
    {
        var subs = string.Join(' ', V1Subcommands);
        return $@"# oahu-cli bash completion. Source this file or copy into /etc/bash_completion.d/.
_oahu_cli_complete() {{
  local cur prev
  cur=""${{COMP_WORDS[COMP_CWORD]}}""
  prev=""${{COMP_WORDS[COMP_CWORD-1]}}""
  if [[ ${{COMP_CWORD}} -eq 1 ]]; then
    COMPREPLY=( $(compgen -W ""{subs}"" -- ""$cur"") )
    return 0
  fi
  return 0
}}
complete -F _oahu_cli_complete oahu-cli
";
    }

    private static string RenderZsh()
    {
        var subs = string.Join(' ', V1Subcommands);
        return $@"#compdef oahu-cli
# oahu-cli zsh completion.
_oahu_cli() {{
  local -a subcommands
  subcommands=({subs})
  if (( CURRENT == 2 )); then
    _describe 'oahu-cli command' subcommands
    return
  fi
}}
_oahu_cli ""$@""
";
    }

    private static string RenderFish()
    {
        var lines = string.Join('\n', Array.ConvertAll(V1Subcommands, sub =>
            $"complete -c oahu-cli -n '__fish_use_subcommand' -a {sub}"));
        return $@"# oahu-cli fish completion. Drop into ~/.config/fish/completions/.
{lines}
";
    }

    private static string RenderPwsh()
    {
        var subs = string.Join(", ", Array.ConvertAll(V1Subcommands, s => $"'{s}'"));
        return $@"# oahu-cli PowerShell completion. dot-source this file from your profile.
Register-ArgumentCompleter -Native -CommandName oahu-cli -ScriptBlock {{
  param($wordToComplete, $commandAst, $cursorPosition)
  $tokens = $commandAst.CommandElements
  if ($tokens.Count -le 2) {{
    $subcommands = @({subs})
    $subcommands | Where-Object {{ $_ -like ""$wordToComplete*"" }} | ForEach-Object {{
      [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
    }}
  }}
}}
";
    }
}
