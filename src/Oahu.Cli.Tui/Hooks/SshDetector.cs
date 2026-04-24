using System;

namespace Oahu.Cli.Tui.Hooks;

/// <summary>
/// True when this process is being driven over SSH. Widgets use this signal to
/// halve animation cadence (per design §6.6) and to disable nice-to-have
/// effects that incur per-frame escape-sequence overhead.
/// </summary>
public static class SshDetector
{
    public static bool IsSshSession()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSH_TTY")))
        {
            return true;
        }
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSH_CONNECTION")))
        {
            return true;
        }
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSH_CLIENT")))
        {
            return true;
        }
        return false;
    }
}
