using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Models;

namespace Oahu.Cli.App.Config;

/// <summary>JSON-on-disk implementation of <see cref="IConfigService"/>. Atomic writes via <see cref="AtomicFile"/>.</summary>
public sealed class JsonConfigService : IConfigService
{
    private readonly string path;
    private readonly object writeLock = new();

    public JsonConfigService(string path)
    {
        this.path = path;
    }

    public string Path => path;

    public Task<OahuConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var loaded = AtomicFile.ReadJson<OahuConfig>(path);
        return Task.FromResult(loaded ?? OahuConfig.Default);
    }

    public Task SaveAsync(OahuConfig config, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (writeLock)
        {
            AtomicFile.WriteAllJson(path, config);
        }
        return Task.CompletedTask;
    }
}
