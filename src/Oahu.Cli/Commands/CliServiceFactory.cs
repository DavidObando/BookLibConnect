using System;
using Oahu.Cli.App.Auth;
using Oahu.Cli.App.Core;
using Oahu.Cli.App.Library;

namespace Oahu.Cli.Commands;

/// <summary>
/// Service-resolution seam for the auth and library commands.
///
/// 4b.1 wired both to the in-memory <see cref="FakeAuthService"/> /
/// <see cref="FakeLibraryService"/>. 4b.2 swaps the defaults to the
/// Core-backed wrappers (<see cref="CoreAuthService"/> /
/// <see cref="CoreLibraryService"/>) that talk to <c>Oahu.Core.AudibleClient</c>
/// against the GUI-shared profile config and library cache. Tests override
/// the factories to inject seeded fakes.
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
            return authSingleton ??= new CoreAuthService();
        }
    };

    public static Func<ILibraryService> LibraryServiceFactory { get; set; } = () =>
    {
        lock (Lock)
        {
            return librarySingleton ??= new CoreLibraryService();
        }
    };

    /// <summary>Test hook: drop cached singletons so the next resolve produces a fresh instance.</summary>
    public static void Reset()
    {
        lock (Lock)
        {
            authSingleton = null;
            librarySingleton = null;
        }
    }
}
