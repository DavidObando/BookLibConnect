using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Models;

namespace Oahu.Cli.App.Config;

/// <summary>Loads and persists the user's <see cref="OahuConfig"/>. Implementations are thread-safe.</summary>
public interface IConfigService
{
    /// <summary>Path to the underlying config file (or "&lt;memory&gt;" for in-memory test impls).</summary>
    string Path { get; }

    Task<OahuConfig> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(OahuConfig config, CancellationToken cancellationToken = default);
}
