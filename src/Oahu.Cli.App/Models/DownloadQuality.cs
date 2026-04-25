namespace Oahu.Cli.App.Models;

/// <summary>Audible download quality, as the CLI exposes it (no leakage of <c>Oahu.Core.EDownloadQuality</c>).</summary>
public enum DownloadQuality
{
    Normal,
    High,
    Extreme,
}
