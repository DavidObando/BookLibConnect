using System;
using System.Diagnostics;
using System.IO;
using Oahu.Cli.App.Paths;

namespace Oahu.Cli.Server.Hosting;

/// <summary>
/// Cooperative file lock under <c>&lt;SharedUserDataDir&gt;/server.lock</c>. Held for the
/// lifetime of the running server. v1 enforces "one CLI server at a time"; full
/// cooperation with the GUI is documented as a known gap (the GUI doesn't take a
/// matching lock yet).
///
/// We open the lock file with <see cref="FileShare.Read"/> so a contending process
/// can read the recorded PID for a friendlier error message, but cannot acquire its
/// own write lock.
/// </summary>
public sealed class UserDataLock : IDisposable
{
    private FileStream? stream;

    public string Path { get; }

    public UserDataLock(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(CliPaths.SharedUserDataDir, "server.lock");
    }

    /// <summary>Acquires the lock or throws an <see cref="InvalidOperationException"/> with the holder's PID (if known).</summary>
    public void Acquire()
    {
        if (stream is not null)
        {
            return;
        }
        var dir = System.IO.Path.GetDirectoryName(Path)!;
        Directory.CreateDirectory(dir);

        try
        {
            stream = new FileStream(
                Path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.WriteThrough | FileOptions.DeleteOnClose);
        }
        catch (IOException ex)
        {
            var holder = TryReadHolder();
            var suffix = holder is null ? string.Empty : $" (held by PID {holder})";
            throw new InvalidOperationException(
                $"oahu-cli serve: another server is already running{suffix}. " +
                $"Lock file: {Path}", ex);
        }

        // Record our PID so a contending process can report it.
        stream.SetLength(0);
        var pid = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var bytes = System.Text.Encoding.UTF8.GetBytes(pid + "\n");
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    public int? TryReadHolder()
    {
        try
        {
            using var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var line = sr.ReadLine();
            if (int.TryParse(line, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var pid) && IsRunning(pid))
            {
                return pid;
            }
        }
        catch
        {
            // file may not exist or may be locked exclusively — fall through.
        }
        return null;
    }

    public void Dispose()
    {
        var s = stream;
        stream = null;
        if (s is null)
        {
            return;
        }
        try
        {
            // FileOptions.DeleteOnClose handles unlink atomically when this stream closes,
            // so there's no race window where another process could acquire the lock
            // pointing at our about-to-be-deleted file.
            s.Close();
        }
        catch
        {
            // best-effort cleanup.
        }
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch
        {
            return false;
        }
    }
}
