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
