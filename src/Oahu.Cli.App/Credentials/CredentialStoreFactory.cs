using System;
using System.IO;
using System.Runtime.InteropServices;
using Oahu.Cli.App.Paths;

namespace Oahu.Cli.App.Credentials;

/// <summary>
/// Picks the platform-appropriate <see cref="ICredentialStore"/>. Returns
/// <see cref="UnsupportedCredentialStore"/> when no native keyring is available so
/// the caller can fail closed with a clear remediation message instead of silently
/// storing secrets in a file (per design / rubber-duck guidance).
/// </summary>
public static class CredentialStoreFactory
{
    public static ICredentialStore Create() => Create(CliPaths.ConfigDir);

    public static ICredentialStore Create(string configDir)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
#pragma warning disable CA1416 // Validate platform compatibility — gate is the if-check above.
            return new WindowsDpapiCredentialStore(Path.Combine(configDir, "credentials.dpapi"));
#pragma warning restore CA1416
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
#pragma warning disable CA1416
            return new MacOsKeychainCredentialStore();
#pragma warning restore CA1416
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // libsecret might still be missing — but that surfaces lazily as
            // CredentialStoreUnavailableException on first use, with a clear message.
#pragma warning disable CA1416
            return new LinuxSecretToolCredentialStore();
#pragma warning restore CA1416
        }

        return new UnsupportedCredentialStore($"Unrecognised OS platform: {RuntimeInformation.OSDescription}");
    }
}
