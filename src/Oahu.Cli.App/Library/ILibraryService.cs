using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Models;

namespace Oahu.Cli.App.Library;

public sealed record LibraryFilter
{
    public string? Search { get; init; }

    public string? Author { get; init; }

    public string? Series { get; init; }

    public bool AvailableOnly { get; init; } = true;
}

/// <summary>
/// Library boundary — list/show/sync. Phase 3 ships this interface plus a fake.
/// The Core-backed <c>BookLibraryService</c> wrapping <c>Oahu.Core.IBookLibrary</c>
/// + <c>BooksDbContext</c> lands in Phase 4 alongside <c>library list/sync/show</c>.
/// </summary>
public interface ILibraryService
{
    Task<IReadOnlyList<LibraryItem>> ListAsync(
        LibraryFilter? filter = null,
        CancellationToken cancellationToken = default);

    Task<LibraryItem?> GetAsync(string asin, CancellationToken cancellationToken = default);

    /// <summary>Pulls the latest library snapshot from Audible. Returns the new item count.</summary>
    Task<int> SyncAsync(string profileAlias, CancellationToken cancellationToken = default);
}
