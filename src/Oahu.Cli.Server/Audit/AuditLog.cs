using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Oahu.Cli.App.Paths;

namespace Oahu.Cli.Server.Audit;

/// <summary>
/// Append-only audit log at <c>&lt;SharedUserDataDir&gt;/logs/server-audit.jsonl</c>.
///
/// One line per tool invocation; the line shape is:
/// <code>
/// {
///   "ts": "2026-01-…Z",
///   "transport": "stdio" | "http",
///   "principal": "stdio" | "http:&lt;tokenPrefix&gt;",
///   "tool": "library_list",
///   "argsHash": "&lt;sha256-hex of canonicalized args&gt;",
///   "outcome": "ok" | "denied" | "error",
///   "latencyMs": 12
/// }
/// </code>
///
/// <para>
/// Args are <i>hashed</i> (SHA-256 of the JSON-canonical form), never logged in the clear,
/// so book titles / ASINs / config values do not leak into the audit trail.
/// </para>
///
/// <para>
/// Failure to write is best-effort and never propagates: a hung disk must not crash
/// the server.
/// </para>
/// </summary>
public sealed class AuditLog
{
    private static readonly object Sync = new();
    private readonly string path;

    public AuditLog(string? path = null)
    {
        this.path = path ?? System.IO.Path.Combine(CliPaths.SharedUserDataDir, "logs", "server-audit.jsonl");
    }

    public string Path => path;

    public void Write(
        string transport,
        string principal,
        string tool,
        IReadOnlyDictionary<string, object?>? args,
        string outcome,
        long latencyMs)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);

            var entry = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["ts"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ["transport"] = transport,
                ["principal"] = principal,
                ["tool"] = tool,
                ["argsHash"] = HashArgs(args),
                ["outcome"] = outcome,
                ["latencyMs"] = latencyMs,
            };

            var line = JsonSerializer.Serialize(entry);
            lock (Sync)
            {
                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var sw = new StreamWriter(fs, Encoding.UTF8);
                sw.WriteLine(line);
            }
        }
        catch
        {
            // best-effort.
        }
    }

    public static string HashArgs(IReadOnlyDictionary<string, object?>? args)
    {
        if (args is null || args.Count == 0)
        {
            return "sha256:" + Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant();
        }
        // Canonicalize: sort keys, serialize values via JsonSerializer with default options.
        var sorted = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kvp in args)
        {
            sorted[kvp.Key] = kvp.Value;
        }
        var json = JsonSerializer.Serialize(sorted);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }
}
