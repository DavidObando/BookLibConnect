using System;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Config;
using Oahu.Cli.App.Models;
using Oahu.Cli.Tui.Screens;
using Oahu.Cli.Tui.Themes;
using Xunit;

namespace Oahu.Cli.Tests.Tui;

[Collection("EnvVarSerial")]
public class SettingsScreenTests : IDisposable
{
    public SettingsScreenTests() => Theme.Reset();

    public void Dispose() => Theme.Reset();

    private static ConsoleKeyInfo Key(char ch, ConsoleKey k = ConsoleKey.NoName, ConsoleModifiers mod = 0)
        => new(ch, k, shift: (mod & ConsoleModifiers.Shift) != 0, alt: (mod & ConsoleModifiers.Alt) != 0, control: (mod & ConsoleModifiers.Control) != 0);

    [Fact]
    public void Navigate_Fields()
    {
        var screen = new SettingsScreen(() => new FakeConfigService());
        screen.Reload();
        Assert.Equal(0, screen.CursorIndex);
        screen.HandleKey(Key('j', ConsoleKey.J));
        Assert.Equal(1, screen.CursorIndex);
        screen.HandleKey(Key('k', ConsoleKey.K));
        Assert.Equal(0, screen.CursorIndex);
    }

    [Fact]
    public void Toggle_Boolean_Field()
    {
        var svc = new FakeConfigService();
        var screen = new SettingsScreen(() => svc);
        screen.Reload();

        // Move to "Keep encrypted files" (index 3)
        screen.HandleKey(Key('j', ConsoleKey.J));
        screen.HandleKey(Key('j', ConsoleKey.J));
        screen.HandleKey(Key('j', ConsoleKey.J));
        Assert.Equal(3, screen.CursorIndex);

        // Toggle it
        screen.HandleKey(Key(' ', ConsoleKey.Spacebar));

        // Save and verify
        screen.Save();
        Assert.True(svc.Saved?.KeepEncryptedFiles);
    }

    [Fact]
    public void Save_Persists_Config()
    {
        var svc = new FakeConfigService();
        var screen = new SettingsScreen(() => svc);
        screen.Reload();
        screen.Save();
        Assert.NotNull(svc.Saved);
    }

    [Fact]
    public void Title_Is_Settings()
    {
        var screen = new SettingsScreen(() => new FakeConfigService());
        Assert.Equal("Settings", screen.Title);
        Assert.Equal('6', screen.NumberKey);
    }

    [Fact]
    public void Render_Returns_Renderable()
    {
        var screen = new SettingsScreen(() => new FakeConfigService());
        var r = screen.Render(80, 20);
        Assert.NotNull(r);
    }

    [Fact]
    public void Cycle_Theme_Updates_Config_And_Live_Theme()
    {
        var svc = new FakeConfigService();
        var screen = new SettingsScreen(() => svc);
        screen.Reload();

        // Move cursor to the Theme row (index 7).
        for (var i = 0; i < 7; i++)
        {
            screen.HandleKey(Key('j', ConsoleKey.J));
        }
        Assert.Equal(7, screen.CursorIndex);

        var startName = Theme.Current.Name;

        // First cycle from "default" (null) → next available theme.
        screen.HandleKey(Key(' ', ConsoleKey.Spacebar));
        Assert.NotEqual(startName, Theme.Current.Name);

        // Persist and confirm.
        screen.Save();
        Assert.NotNull(svc.Saved);
        Assert.Equal(Theme.Current.Name, svc.Saved!.Theme);
    }

    [Fact]
    public void Cycle_Theme_Wraps_Through_All_Available_Themes()
    {
        var svc = new FakeConfigService();
        var screen = new SettingsScreen(() => svc);
        screen.Reload();
        for (var i = 0; i < 7; i++)
        {
            screen.HandleKey(Key('j', ConsoleKey.J));
        }

        var seen = new System.Collections.Generic.HashSet<string>();
        for (var i = 0; i < Theme.Available.Count + 1; i++)
        {
            screen.HandleKey(Key(' ', ConsoleKey.Spacebar));
            seen.Add(Theme.Current.Name);
        }
        // After cycling through all available, every name has been visited.
        foreach (var t in Theme.Available)
        {
            Assert.Contains(t.Name, seen);
        }
    }

    private sealed class FakeConfigService : IConfigService
    {
        public string Path => "<memory>";

        public OahuConfig? Saved { get; set; }

        public Task<OahuConfig> LoadAsync(CancellationToken ct = default)
            => Task.FromResult(OahuConfig.Default);

        public Task SaveAsync(OahuConfig config, CancellationToken ct = default)
        {
            Saved = config;
            return Task.CompletedTask;
        }
    }
}
