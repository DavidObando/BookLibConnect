using System;
using System.Collections.Generic;
using System.Linq;
using Oahu.Cli.App.Config;
using Oahu.Cli.App.Models;
using Oahu.Cli.Tui.Shell;
using Oahu.Cli.Tui.Themes;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Screens;

/// <summary>
/// Settings screen (tab 6). Displays and edits <see cref="OahuConfig"/>
/// fields. Per design TUI-exploration §8.
/// </summary>
public sealed class SettingsScreen : ITabScreen
{
    private const int ThemeFieldIndex = 7;
    private const string DefaultThemeName = "Default";

    // Editable fields in display order.
    private static readonly string[] FieldNames =
    {
        "Download directory",
        "Default quality",
        "Max parallel jobs",
        "Keep encrypted files",
        "Multi-part download",
        "Export to AAX",
        "Export directory",
        "Theme",
    };

    private readonly Func<IConfigService> configServiceFactory;
    private OahuConfig config = OahuConfig.Default;
    private bool loaded;
    private int cursor;
    private string? toast;

    public SettingsScreen(Func<IConfigService> configServiceFactory)
    {
        this.configServiceFactory = configServiceFactory ?? throw new ArgumentNullException(nameof(configServiceFactory));
    }

    public string Title => "Settings";

    public char NumberKey => '6';

    public int CursorIndex => cursor;

    public IEnumerable<KeyValuePair<string, string?>> Hints
    {
        get
        {
            yield return new("↑↓", "navigate");
            yield return new("Enter/Space", "toggle");
            yield return new("s", "save");
        }
    }

    public IRenderable Render(int width, int height)
    {
        EnsureLoaded();
        var lines = new List<IRenderable>();

        var primary = Tokens.Tokens.TextPrimary.Value.ToMarkup();
        var secondary = Tokens.Tokens.TextSecondary.Value.ToMarkup();
        var tertiary = Tokens.Tokens.TextTertiary.Value.ToMarkup();
        var brand = Tokens.Tokens.Brand.Value.ToMarkup();

        lines.Add(new Markup($"[{primary} bold]Settings[/]"));
        lines.Add(new Markup(string.Empty));

        var values = GetFieldValues();
        for (var i = 0; i < FieldNames.Length; i++)
        {
            var isCursor = i == cursor;
            var pointer = isCursor ? $"[{brand}]❯[/]" : " ";
            var style = isCursor ? $"bold {primary}" : secondary;
            lines.Add(new Markup($"  {pointer} [{style}]{Markup.Escape(FieldNames[i])}[/]  [{tertiary}]{Markup.Escape(values[i])}[/]"));
        }

        if (toast is not null)
        {
            lines.Add(new Markup(string.Empty));
            lines.Add(new Markup($"  [{Tokens.Tokens.StatusSuccess.Value.ToMarkup()}]{Markup.Escape(toast)}[/]"));
        }

        return new Padder(new Rows(lines)).Padding(2, 1, 2, 1);
    }

    public bool HandleKey(ConsoleKeyInfo key)
    {
        toast = null;
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                cursor = Math.Max(0, cursor - 1);
                return true;
            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                cursor = Math.Min(FieldNames.Length - 1, cursor + 1);
                return true;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                ToggleOrCycle();
                return true;
            case ConsoleKey.S when key.Modifiers == 0:
                Save();
                return true;
        }
        return false;
    }

    /// <summary>Reload config from disk.</summary>
    public void Reload()
    {
        try
        {
            var svc = configServiceFactory();
            config = svc.LoadAsync().GetAwaiter().GetResult();
            loaded = true;
        }
        catch
        {
            // Keep defaults.
        }
    }

    /// <summary>Persist config.</summary>
    public void Save()
    {
        try
        {
            var svc = configServiceFactory();
            svc.SaveAsync(config).GetAwaiter().GetResult();
            toast = "✓ Settings saved";
        }
        catch (Exception ex)
        {
            toast = $"✗ {ex.Message}";
        }
    }

    internal void EnsureLoaded()
    {
        if (!loaded)
        {
            Reload();
        }
    }

    private void ToggleOrCycle()
    {
        config = cursor switch
        {
            1 => config with
            {
                DefaultQuality = config.DefaultQuality switch
                {
                    DownloadQuality.High => DownloadQuality.Normal,
                    _ => DownloadQuality.High,
                },
            },
            2 => config with { MaxParallelJobs = config.MaxParallelJobs >= 4 ? 1 : config.MaxParallelJobs + 1 },
            3 => config with { KeepEncryptedFiles = !config.KeepEncryptedFiles },
            4 => config with { MultiPartDownload = !config.MultiPartDownload },
            5 => config with { ExportToAax = !config.ExportToAax },
            ThemeFieldIndex => CycleTheme(config),
            _ => config,
        };

        if (cursor == ThemeFieldIndex)
        {
            // Apply immediately so the user sees the new palette take effect
            // even before pressing 's' to persist.
            try
            {
                Theme.Use(config.Theme ?? DefaultThemeName);
            }
            catch
            {
                // Defensive: should be impossible since CycleTheme only uses known names.
            }
        }
    }

    private static OahuConfig CycleTheme(OahuConfig cfg)
    {
        var names = Theme.AvailableNames().ToArray();
        var current = cfg.Theme ?? DefaultThemeName;
        var idx = Array.FindIndex(names, n => string.Equals(n, current, StringComparison.OrdinalIgnoreCase));
        var next = names[(idx + 1) % names.Length];
        return cfg with { Theme = next };
    }

    private string[] GetFieldValues()
    {
        return new[]
        {
            config.DownloadDirectory,
            config.DefaultQuality.ToString(),
            config.MaxParallelJobs.ToString(),
            config.KeepEncryptedFiles ? "on" : "off",
            config.MultiPartDownload ? "on" : "off",
            config.ExportToAax ? "on" : "off",
            string.IsNullOrEmpty(config.ExportDirectory) ? "(none)" : config.ExportDirectory,
            config.Theme ?? $"{DefaultThemeName} (default)",
        };
    }
}
