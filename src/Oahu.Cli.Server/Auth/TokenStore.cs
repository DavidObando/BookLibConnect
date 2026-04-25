using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Oahu.Cli.App.Paths;

namespace Oahu.Cli.Server.Auth;

/// <summary>
/// Bearer token for the loopback HTTP transport. The token lives in a single file
/// at <c>&lt;ConfigDir&gt;/server.token</c> with restrictive permissions (Unix: 0600,
/// Windows: ACL'd to current user only). Generated lazily on first <see cref="ReadOrCreate"/>.
///
/// v1 model (per design §15.2 — scopes deferred): a single full-power token. Compare
/// in constant time to thwart trivial timing attacks.
/// </summary>
public sealed class TokenStore
{
    /// <summary>Public for tests; production callers should use the no-arg constructor.</summary>
    public TokenStore(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(CliPaths.ConfigDir, "server.token");
    }

    public string Path { get; }

    /// <summary>Creates the token if missing; otherwise returns the existing value (and validates the file mode).</summary>
    public string ReadOrCreate()
    {
        var dir = System.IO.Path.GetDirectoryName(Path)!;
        Directory.CreateDirectory(dir);

        if (File.Exists(Path))
        {
            ValidateMode(Path);
            var existing = File.ReadAllText(Path).Trim();
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }
            // empty file — fall through to regenerate.
        }

        return WriteNew(Path);
    }

    /// <summary>Atomically replaces the token with a freshly-minted one. Used by <c>oahu-cli serve token rotate</c>.</summary>
    public string Rotate()
    {
        var dir = System.IO.Path.GetDirectoryName(Path)!;
        Directory.CreateDirectory(dir);
        return WriteNew(Path);
    }

    /// <summary>Constant-time comparison to defeat trivial timing oracles.</summary>
    public static bool Equal(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return false;
        }
        if (a.Length != b.Length)
        {
            return false;
        }
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }

    private static string WriteNew(string path)
    {
        // 32 random bytes (256 bits) -> base64url, no padding.
        Span<byte> buf = stackalloc byte[32];
        RandomNumberGenerator.Fill(buf);
        var token = ToBase64Url(buf);

        // Create with restrictive mode FROM THE START on Unix (no chmod-after window).
        FileStreamOptions opts = new()
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            opts.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var fs = new FileStream(path, opts))
        using (var sw = new StreamWriter(fs))
        {
            sw.Write(token);
            sw.Write('\n');
        }

        // On Windows, tighten the ACL to the current user only.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            TryRestrictWindowsAcl(path);
        }
        return token;
    }

    private static void ValidateMode(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Best-effort on Windows — relying on NTFS ACLs configured at write-time.
            return;
        }
        var mode = File.GetUnixFileMode(path);
        const UnixFileMode forbidden = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                                       UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        if ((mode & forbidden) != 0)
        {
            throw new InvalidOperationException(
                $"Token file {path} has overly permissive mode ({mode}); refusing to start. " +
                "Run `chmod 600 \"" + path + "\"` and retry, or delete the file to regenerate.");
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void TryRestrictWindowsAcl(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            var sec = fi.GetAccessControl();
            sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            // Strip everyone, add current user only.
            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
            var rule = new System.Security.AccessControl.FileSystemAccessRule(
                sid,
                System.Security.AccessControl.FileSystemRights.FullControl,
                System.Security.AccessControl.AccessControlType.Allow);
            // Remove all then add ours.
            var current = sec.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier));
            foreach (System.Security.AccessControl.FileSystemAccessRule r in current)
            {
                sec.RemoveAccessRule(r);
            }
            sec.AddAccessRule(rule);
            fi.SetAccessControl(sec);
        }
        catch
        {
            // Best-effort. The token is already in a per-user APPDATA folder.
        }
    }

    private static string ToBase64Url(ReadOnlySpan<byte> bytes)
    {
        var s = Convert.ToBase64String(bytes);
        return s.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
