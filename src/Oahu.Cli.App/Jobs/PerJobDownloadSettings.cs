using System;
using Oahu.BooksDatabase;
using Oahu.Core;

namespace Oahu.Cli.App.Jobs;

/// <summary>
/// Per-job decorator over <see cref="IDownloadSettings"/> that overrides only
/// <see cref="IDownloadSettings.DownloadQuality"/> while delegating every other
/// member (and the <c>ChangedSettings</c> event) to the wrapped instance.
///
/// <para>
/// This is the seam used to honour <c>JobRequest.Quality</c> without mutating
/// the process-wide GUI-shared <see cref="OahuUserSettings.DownloadSettings"/>.
/// Critical for future concurrency and for not bleeding state across jobs in
/// the same process.
/// </para>
/// </summary>
public sealed class PerJobDownloadSettings : IDownloadSettings
{
    private readonly IDownloadSettings inner;
    private readonly EDownloadQuality quality;

    public PerJobDownloadSettings(IDownloadSettings inner, EDownloadQuality quality)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.quality = quality;
    }

    public event EventHandler ChangedSettings
    {
        add => inner.ChangedSettings += value;
        remove => inner.ChangedSettings -= value;
    }

    public bool AutoRefresh => inner.AutoRefresh;

    public bool AutoUpdateLibrary => inner.AutoUpdateLibrary;

    public bool AutoOpenDownloadDialog => inner.AutoOpenDownloadDialog;

    public bool IncludeAdultProducts => inner.IncludeAdultProducts;

    public bool HideUnavailableProducts => inner.HideUnavailableProducts;

    public bool MultiPartDownload => inner.MultiPartDownload;

    public bool KeepEncryptedFiles => inner.KeepEncryptedFiles;

    public EDownloadQuality DownloadQuality => quality;

    public string DownloadDirectory => inner.DownloadDirectory;

    public EInitialSorting InitialSorting => inner.InitialSorting;
}
