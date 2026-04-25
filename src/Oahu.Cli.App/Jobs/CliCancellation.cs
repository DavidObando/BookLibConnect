using System.Threading;
using Oahu.Core;

namespace Oahu.Cli.App.Jobs;

/// <summary>
/// CLI-side <see cref="ICancellation"/> for <c>DownloadDecryptJob&lt;T&gt;</c>.
/// Mirrors <c>Oahu.App.Avalonia.SimpleCancellation</c> so the CLI does not
/// reference the Avalonia GUI assembly.
/// </summary>
public sealed class CliCancellation : ICancellation
{
    public CliCancellation(CancellationToken token)
    {
        CancellationToken = token;
    }

    public CancellationToken CancellationToken { get; }
}
