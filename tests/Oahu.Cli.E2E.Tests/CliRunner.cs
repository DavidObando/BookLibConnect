using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Oahu.Cli.E2E.Tests;

/// <summary>
/// Subprocess-based runner for the built <c>oahu-cli</c> binary. Resolves the
/// CLI assembly via <c>dotnet</c> at the build output of the
/// <c>src/Oahu.Cli</c> project. Each spawn uses isolated <c>--config-dir</c>
/// and <c>--log-dir</c> flags so tests do not stomp on the developer's home.
/// </summary>
internal sealed class CliRunner
{
    private static readonly string CliDll = ResolveCliDll();

    public string ConfigDir { get; }

    public string LogDir { get; }

    public string DataDir { get; }

    public CliRunner()
    {
        var root = Path.Combine(Path.GetTempPath(), "oahu-cli-e2e-" + Guid.NewGuid().ToString("N"));
        ConfigDir = Path.Combine(root, "config");
        LogDir = Path.Combine(root, "logs");
        DataDir = Path.Combine(root, "data");
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(LogDir);
        Directory.CreateDirectory(DataDir);
    }

    public async Task<CliResult> RunAsync(params string[] args) => await RunAsync(includeIsolation: true, args).ConfigureAwait(false);

    public async Task<CliResult> RunRawAsync(params string[] args) => await RunAsync(includeIsolation: false, args).ConfigureAwait(false);

    private async Task<CliResult> RunAsync(bool includeIsolation, string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(CliDll);
        if (includeIsolation)
        {
            psi.ArgumentList.Add("--config-dir");
            psi.ArgumentList.Add(ConfigDir);
            psi.ArgumentList.Add("--log-dir");
            psi.ArgumentList.Add(LogDir);
        }
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        psi.Environment["OAHU_NO_TUI"] = "1";
        psi.Environment["NO_COLOR"] = "1";

        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        var exited = await Task.Run(() => p.WaitForExit(60_000)).ConfigureAwait(false);
        if (!exited)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("CLI process did not exit within 60 seconds.");
        }
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new CliResult(p.ExitCode, stdout, stderr);
    }

    private static string ResolveCliDll()
    {
        var asmDir = Path.GetDirectoryName(typeof(CliRunner).Assembly.Location)!;
        var configFolder = new DirectoryInfo(asmDir).Parent?.Name ?? "Debug";
        var tfm = new DirectoryInfo(asmDir).Name;
        var candidate = Path.GetFullPath(Path.Combine(
            asmDir, "..", "..", "..", "..", "..",
            "src", "Oahu.Cli", "bin", configFolder, tfm, "oahu-cli.dll"));
        if (!File.Exists(candidate))
        {
            throw new FileNotFoundException(
                $"Could not locate built oahu-cli.dll. Build src/Oahu.Cli first. Tried: {candidate}");
        }
        return candidate;
    }
}

internal sealed record CliResult(int ExitCode, string StdOut, string StdErr)
{
    public string AllOutput => new StringBuilder().Append(StdOut).Append(StdErr).ToString();
}
