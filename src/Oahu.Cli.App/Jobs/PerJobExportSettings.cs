using System;
using Oahu.Core;

namespace Oahu.Cli.App.Jobs;

/// <summary>
/// Per-job decorator over <see cref="IExportSettings"/> that overrides
/// <see cref="IExportSettings.ExportToAax"/> and (optionally)
/// <see cref="IExportSettings.ExportDirectory"/>, delegating any non-overridden
/// member to the wrapped instance.
///
/// <para>
/// Mirrors the rationale of <see cref="PerJobDownloadSettings"/>: lets a CLI
/// invocation flip AAX export on or aim it at a different directory without
/// mutating the GUI-shared <c>OahuUserSettings.ExportSettings</c>.
/// </para>
/// </summary>
public sealed class PerJobExportSettings : IExportSettings
{
    private readonly IExportSettings inner;
    private readonly bool? exportToAaxOverride;
    private readonly string? exportDirectoryOverride;

    public PerJobExportSettings(IExportSettings inner, bool? exportToAax = null, string? exportDirectory = null)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        exportToAaxOverride = exportToAax;
        exportDirectoryOverride = exportDirectory;
    }

    public bool? ExportToAax => exportToAaxOverride ?? inner.ExportToAax;

    public string ExportDirectory => exportDirectoryOverride ?? inner.ExportDirectory;
}
