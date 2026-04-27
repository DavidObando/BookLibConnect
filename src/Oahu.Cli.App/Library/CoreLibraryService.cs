using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oahu.BooksDatabase;
using Oahu.Cli.App.Core;
using Oahu.Cli.App.Models;
using Oahu.Core;

namespace Oahu.Cli.App.Library;

/// <summary>
/// Core-backed <see cref="ILibraryService"/>. Reads books through
/// <see cref="AudibleClient.Api"/>'s <c>GetBooks()</c> (which queries the local
/// books DB scoped to the active profile) and synchronises with Audible via
/// <c>GetLibraryAsync(resync)</c>.
/// </summary>
public sealed class CoreLibraryService : ILibraryService
{
    private readonly AudibleClient client;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private bool refreshed;

    public CoreLibraryService()
        : this(CoreEnvironment.Client)
    {
    }

    public CoreLibraryService(AudibleClient client)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<IReadOnlyList<LibraryItem>> ListAsync(
        LibraryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureFreshAsync(cancellationToken).ConfigureAwait(false);
        var api = client.Api;
        if (api is null)
        {
            // No active profile — empty library, not an error. Commands surface
            // the "no active profile" hint when results are empty.
            return Array.Empty<LibraryItem>();
        }

        IEnumerable<Book> books = api.GetBooks() ?? Enumerable.Empty<Book>();
        filter ??= new LibraryFilter();

        IEnumerable<LibraryItem> items = books.Select(MapBook);
        items = ApplyFilter(items, filter);
        return items.ToArray();
    }

    public async Task<LibraryItem?> GetAsync(string asin, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(asin);
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureFreshAsync(cancellationToken).ConfigureAwait(false);
        var api = client.Api;
        if (api is null)
        {
            return null;
        }

        var book = api.GetBooks()?.FirstOrDefault(
            b => string.Equals(b.Asin, asin, StringComparison.OrdinalIgnoreCase));
        return book is null ? null : MapBook(book);
    }

    public async Task<int> SyncAsync(string profileAlias, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileAlias);
        cancellationToken.ThrowIfCancellationRequested();

        await CoreEnvironment.EnsureProfileLoadedAsync().ConfigureAwait(false);

        // Switch to the requested profile if it's not the active one. The CLI lets
        // any signed-in alias be sync'd; without this, SyncAsync silently sync'd
        // whichever profile the GUI happened to have last activated, even when the
        // caller passed a different alias.
        var aliases = client.GetAccountAliases()?.ToDictionary(a => a.AccountId, a => a.Alias, StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var activeKey = client.ProfileKey;
        var activeAlias = activeKey is not null && aliases.TryGetValue(activeKey.AccountId, out var aliasForActive)
            ? aliasForActive
            : null;

        if (!string.Equals(activeAlias, profileAlias, StringComparison.Ordinal))
        {
            var profiles = await client.GetProfilesAsync().ConfigureAwait(false);
            var key = profiles?.FirstOrDefault(p =>
                aliases.TryGetValue(p.AccountId, out var alias) &&
                string.Equals(alias, profileAlias, StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    $"No profile with alias '{profileAlias}'. Sign in with `oahu-cli auth login` first.");
            await client.ChangeProfileAsync(key, aliasChanged: false).ConfigureAwait(false);
        }

        var api = client.Api
            ?? throw new InvalidOperationException(
                $"Failed to load profile '{profileAlias}' for sync.");

        // resync=true forces a full library refresh; the CLI surface does not
        // (yet) distinguish full vs incremental, so we do a full pull every
        // time. 4c's job-runner will introduce an incremental option.
        var libraryResponse = await api.GetLibraryAsync(resync: true).ConfigureAwait(false);
        if (libraryResponse is null)
        {
            // AudibleApi.GetLibraryAsync silently returns null on any HTTP error
            // (see SendForStringAsync's catch in src/Oahu.Core/AudibleApi.cs).
            // Surface that as a real failure rather than reporting "0 books"
            // and leaving the user wondering why a fresh sign-in produced
            // an empty library.
            throw new InvalidOperationException(
                $"Library sync for '{profileAlias}' failed: the Audible API returned no data. "
                + "This usually indicates an authentication or network problem; check the Oahu log "
                + "(under ~/Library/Application Support/Oahu/log) for HTTP details.");
        }

        var books = api.GetBooks();
        return books?.Count() ?? 0;
    }

    /// <inheritdoc/>
    public async Task EnsureFreshAsync(CancellationToken cancellationToken = default)
    {
        if (refreshed)
        {
            return;
        }

        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (refreshed)
            {
                return;
            }

            await CoreEnvironment.EnsureProfileLoadedAsync().ConfigureAwait(false);
            var api = client.Api;
            if (api is null)
            {
                // No active profile — nothing to refresh.
                refreshed = true;
                return;
            }

            // Incremental refresh (resync: false) like the GUI does on startup.
            // Only fetches items newer than the last sync point.
            await api.GetLibraryAsync(resync: false).ConfigureAwait(false);
            refreshed = true;
        }
        catch
        {
            // Network/auth failures are non-fatal for EnsureFreshAsync — the user
            // still gets whatever is in the local cache. An explicit `library sync`
            // surfaces errors properly.
            refreshed = true;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private static IEnumerable<LibraryItem> ApplyFilter(IEnumerable<LibraryItem> items, LibraryFilter filter)
    {
        if (filter.AvailableOnly)
        {
            items = items.Where(i => i.IsAvailable);
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            items = items.Where(i => i.Title.Contains(filter.Search, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(filter.Author))
        {
            items = items.Where(i => i.Authors.Any(
                a => a.Contains(filter.Author!, StringComparison.OrdinalIgnoreCase)));
        }
        if (!string.IsNullOrWhiteSpace(filter.Series))
        {
            items = items.Where(i => string.Equals(i.Series, filter.Series, StringComparison.OrdinalIgnoreCase));
        }
        return items;
    }

    private static LibraryItem MapBook(Book book)
    {
        var seriesEntry = book.Series?.FirstOrDefault();
        double? seriesPosition = null;
        if (seriesEntry is not null)
        {
            // SeriesBook.BookNumber may be 0 (unset); SubNumber is optional.
            // We use a /1000 offset so SubNumber up to 999 maps unambiguously
            // (e.g. Book 1, SubNumber 5 -> 1.005), avoiding collision when
            // SubNumber >= 10 (the previous /10 formula made 1.12 == 2.2).
            if (seriesEntry.SubNumber.HasValue)
            {
                seriesPosition = seriesEntry.BookNumber + (seriesEntry.SubNumber.Value / 1000.0);
            }
            else if (seriesEntry.BookNumber > 0)
            {
                seriesPosition = seriesEntry.BookNumber;
            }
        }

        TimeSpan? runtime = book.RunTimeLengthSeconds.HasValue
            ? TimeSpan.FromSeconds(book.RunTimeLengthSeconds.Value)
            : null;

        DateTimeOffset? purchase = book.PurchaseDate.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(book.PurchaseDate.Value, DateTimeKind.Utc))
            : null;

        // "Available" in the CLI sense = not soft-deleted from the user's library.
        bool available = !(book.Deleted ?? false);

        // Multi-part is signalled by more than one Component on the book.
        bool multiPart = (book.Components?.Count ?? 0) > 1;

        return new LibraryItem
        {
            Asin = book.Asin ?? string.Empty,
            Title = book.Title ?? string.Empty,
            Subtitle = string.IsNullOrWhiteSpace(book.Subtitle) ? null : book.Subtitle,
            Authors = book.Authors?.Select(a => a.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToArray()
                ?? Array.Empty<string>(),
            Narrators = book.Narrators?.Select(n => n.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToArray()
                ?? Array.Empty<string>(),
            Series = seriesEntry?.Series?.Title,
            SeriesPosition = seriesPosition,
            Runtime = runtime,
            PurchaseDate = purchase,
            IsAvailable = available,
            HasMultiplePartFiles = multiPart,
        };
    }
}
