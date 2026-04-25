using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Models;

namespace Oahu.Cli.App.Library;

/// <summary>In-memory <see cref="ILibraryService"/> for tests and offline development.</summary>
public sealed class FakeLibraryService : ILibraryService
{
    private readonly List<LibraryItem> items;

    public FakeLibraryService(IEnumerable<LibraryItem>? items = null)
    {
        this.items = (items ?? Enumerable.Empty<LibraryItem>()).ToList();
    }

    public Task<IReadOnlyList<LibraryItem>> ListAsync(
        LibraryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        filter ??= new LibraryFilter();
        IEnumerable<LibraryItem> q = items;
        if (filter.AvailableOnly)
        {
            q = q.Where(i => i.IsAvailable);
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            q = q.Where(i => i.Title.Contains(filter.Search, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(filter.Author))
        {
            q = q.Where(i => i.Authors.Any(a => a.Contains(filter.Author!, StringComparison.OrdinalIgnoreCase)));
        }
        if (!string.IsNullOrWhiteSpace(filter.Series))
        {
            q = q.Where(i => string.Equals(i.Series, filter.Series, StringComparison.OrdinalIgnoreCase));
        }
        return Task.FromResult<IReadOnlyList<LibraryItem>>(q.ToArray());
    }

    public Task<LibraryItem?> GetAsync(string asin, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(asin);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.FirstOrDefault(i => string.Equals(i.Asin, asin, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<int> SyncAsync(string profileAlias, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileAlias);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(items.Count);
    }

    public void Seed(LibraryItem item) => items.Add(item);
}
