using System;
using System.Runtime.InteropServices;

namespace Oahu.Cli.Tui.Hooks;

/// <summary>
/// Best-effort screen-reader detection. Phase 2 honours environment overrides
/// universally and queries <c>SystemParametersInfo(SPI_GETSCREENREADER)</c> on
/// Windows; macOS / Linux native probes are reserved for Phase 9.
///
/// Always assume a false-negative rate: a user who needs the accessibility
/// path can opt in explicitly with <c>OAHU_SCREEN_READER=1</c>.
/// </summary>
public static class ScreenReaderProbe
{
    private const uint SpiGetscreenreader = 0x0046;

    public static bool IsActive() => CheckEnv() || CheckWin32();

    private static bool CheckEnv()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("OAHU_NO_TUI"), "1", StringComparison.Ordinal))
        {
            return true;
        }
        if (string.Equals(Environment.GetEnvironmentVariable("OAHU_SCREEN_READER"), "1", StringComparison.Ordinal))
        {
            return true;
        }
        return false;
    }

    private static bool CheckWin32()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }
        try
        {
            var active = false;
            if (NativeMethods.SystemParametersInfo(SpiGetscreenreader, 0, ref active, 0))
            {
                return active;
            }
        }
        catch
        {
            // Not all Windows hosts (Nano Server, sandboxes) expose the SPI; treat as not-active.
        }
        return false;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref bool pvParam, uint fWinIni);
    }
}
