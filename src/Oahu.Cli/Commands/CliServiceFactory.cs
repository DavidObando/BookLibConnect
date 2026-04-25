using System;
using Oahu.Cli.App.Auth;
using Oahu.Cli.App.Library;

namespace Oahu.Cli.Commands;

/// <summary>
/// Service-resolution seam for the auth and library commands.
///
/// 4b.1 wires both to the in-memory <see cref="FakeAuthService"/> /
/// <see cref="FakeLibraryService"/>. 4b.2 swaps in the Core-backed
/// <c>CoreAuthService</c> / <c>CoreLibraryService</c> wrappers that talk to
/// <c>Oahu.Core.AudibleClient</c> against the GUI-shared profile config and
/// library cache. Tests override these factories to inject seeded fakes.
/// </summary>
internal static class CliServiceFactory
{
    private static readonly object Lock = new();
    private static IAuthService? authSingleton;
    private static ILibraryService? librarySingleton;

    public static Func<IAuthService> AuthServiceFactory { get; set; } = () =>
    {
        lock (Lock)
        {
            return authSingleton ??= new FakeAuthService();
        }
    };

    public static Func<ILibraryService> LibraryServiceFactory { get; set; } = () =>
    {
        lock (Lock)
        {
            return librarySingleton ??= new FakeLibraryService();
        }
    };

    /// <summary>Test hook: drop cached singletons so the next resolve produces a fresh fake.</summary>
    public static void Reset()
    {
        lock (Lock)
        {
            authSingleton = null;
            librarySingleton = null;
        }
    }
}
