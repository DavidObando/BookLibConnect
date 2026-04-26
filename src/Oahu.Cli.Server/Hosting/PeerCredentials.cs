using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Oahu.Cli.Server.Hosting;

/// <summary>
/// Resolves the peer UID of a Unix-domain-socket client connection. Used by the
/// optional <c>--strict-peer</c> mode in <c>oahu-cli serve</c> to ensure that a
/// connecting client process belongs to the same user as the server. Linux uses
/// <c>SO_PEERCRED</c>; macOS uses <c>LOCAL_PEERCRED</c> via <c>getsockopt</c>.
/// </summary>
/// <remarks>
/// Returns <c>null</c> on platforms where peer-credential lookup is unavailable
/// (Windows, BSD variants we haven't tested) or when the underlying syscall
/// fails. Callers should treat <c>null</c> as "unknown" — the strict-peer
/// caller policy is to ALLOW unknown peers but log a warning, since rejecting
/// would brick the server on platforms we haven't covered yet.
/// </remarks>
[SuppressMessage("StyleCop.CSharp.NamingRules", "SA1310:Field names should not contain underscore", Justification = "Constants mirror libc/syscall identifiers.")]
[SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1202:Elements should be ordered by access", Justification = "Public methods grouped near related private P/Invoke decls for readability.")]
internal static class PeerCredentials
{
    private const int SOL_SOCKET = 1;        // Linux
    private const int SO_PEERCRED = 17;      // Linux
    private const int SOL_LOCAL = 0;         // macOS
    private const int LOCAL_PEERCRED = 0x001;

    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxUcred
    {
        public int Pid;
        public uint Uid;
        public uint Gid;
    }

    [StructLayout(LayoutKind.Sequential)]
    [SuppressMessage("StyleCop.CSharp.NamingRules", "SA1307:Accessible fields should begin with upper-case letter", Justification = "Mirrors xucred(4) struct field names verbatim.")]
    private struct MacXucred
    {
        public uint cr_version;
        public uint cr_uid;
        public uint cr_ngroups;
    }

    [DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static extern int GetsockoptLinux(int sockfd, int level, int optname, ref LinuxUcred optval, ref uint optlen);

    [DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
    private static extern int GetsockoptMac(int sockfd, int level, int optname, ref MacXucred optval, ref uint optlen);

    /// <summary>
    /// Returns the peer UID for the given socket, or <c>null</c> if the
    /// platform does not support peer-credential lookup or the syscall fails.
    /// </summary>
    public static uint? TryGetPeerUid(Socket socket)
    {
        if (socket is null)
        {
            return null;
        }

        // Get the OS file descriptor. .NET 8+ exposes Handle as a SafeSocketHandle/IntPtr.
        var handle = (int)socket.Handle.ToInt64();
        if (handle <= 0)
        {
            return null;
        }

        if (OperatingSystem.IsLinux())
        {
            var cred = default(LinuxUcred);
            uint len = (uint)Marshal.SizeOf<LinuxUcred>();
            if (GetsockoptLinux(handle, SOL_SOCKET, SO_PEERCRED, ref cred, ref len) == 0)
            {
                return cred.Uid;
            }
            return null;
        }

        if (OperatingSystem.IsMacOS())
        {
            var cred = default(MacXucred);
            uint len = (uint)Marshal.SizeOf<MacXucred>();
            if (GetsockoptMac(handle, SOL_LOCAL, LOCAL_PEERCRED, ref cred, ref len) == 0)
            {
                return cred.cr_uid;
            }
            return null;
        }

        return null;
    }

    /// <summary>The current process's effective UID (Linux/macOS), or <c>null</c> on Windows.</summary>
    public static uint? GetCurrentUid()
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }
        try
        {
            return Geteuid();
        }
        catch (DllNotFoundException)
        {
            return null;
        }
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint Geteuid();
}
