using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Oahu.CommonTypes;

namespace Oahu.SystemManagement.Mac {
  public class MacHardwareIdProvider : IHardwareIdProvider {

    private string _cachedCpuId;
    private string _cachedMotherboardId;
    private string _cachedDiskId;

    public string GetCpuId () {
      if (_cachedCpuId is not null)
        return _cachedCpuId;
      try {
        // Use sysctl to get CPU brand string as a stable identifier
        _cachedCpuId = runCommand ("sysctl", "-n machdep.cpu.brand_string")?.Trim () ?? string.Empty;
        return _cachedCpuId;
      } catch (Exception) {
        return string.Empty;
      }
    }

    public string GetMotherboardId () {
      if (_cachedMotherboardId is not null)
        return _cachedMotherboardId;
      try {
        // Hardware UUID is the macOS equivalent of a motherboard serial
        _cachedMotherboardId = runCommand ("ioreg", "-rd1 -c IOPlatformExpertDevice")
          ?.ExtractIoregValue ("IOPlatformUUID") ?? string.Empty;
        return _cachedMotherboardId;
      } catch (Exception) {
        return string.Empty;
      }
    }

    public string GetMotherboardPnpDeviceId () {
      try {
        // Serial number serves as a secondary identifier on macOS
        return runCommand ("ioreg", "-rd1 -c IOPlatformExpertDevice")
          ?.ExtractIoregValue ("IOPlatformSerialNumber") ?? string.Empty;
      } catch (Exception) {
        return string.Empty;
      }
    }

    public string GetDiskId () {
      if (_cachedDiskId is not null)
        return _cachedDiskId;
      try {
        // Get the volume UUID of the root filesystem
        _cachedDiskId = runCommand ("diskutil", "info -plist /")
          ?.ExtractPlistValue ("VolumeUUID") ?? string.Empty;
        return _cachedDiskId;
      } catch (Exception) {
        return string.Empty;
      }
    }

    private static string runCommand (string command, string arguments) {
      try {
        var psi = new ProcessStartInfo {
          FileName = command,
          Arguments = arguments,
          RedirectStandardOutput = true,
          UseShellExecute = false,
          CreateNoWindow = true
        };
        using var process = Process.Start (psi);
        string output = process?.StandardOutput.ReadToEnd ();
        process?.WaitForExit ();
        return output;
      } catch (Exception) {
        return null;
      }
    }
  }

  internal static class MacStringExtensions {
    internal static string ExtractIoregValue (this string ioregOutput, string key) {
      if (string.IsNullOrEmpty (ioregOutput))
        return null;
      // ioreg output format: "key" = "value"
      string search = $"\"{key}\" = \"";
      int idx = ioregOutput.IndexOf (search, StringComparison.Ordinal);
      if (idx < 0)
        return null;
      idx += search.Length;
      int endIdx = ioregOutput.IndexOf ('"', idx);
      if (endIdx < 0)
        return null;
      return ioregOutput.Substring (idx, endIdx - idx);
    }

    internal static string ExtractPlistValue (this string plistOutput, string key) {
      if (string.IsNullOrEmpty (plistOutput))
        return null;
      // Simple plist XML extraction: <key>Key</key>\n<string>Value</string>
      string keyTag = $"<key>{key}</key>";
      int idx = plistOutput.IndexOf (keyTag, StringComparison.Ordinal);
      if (idx < 0)
        return null;
      idx += keyTag.Length;
      string remaining = plistOutput.Substring (idx);
      string startTag = "<string>";
      string endTag = "</string>";
      int startIdx = remaining.IndexOf (startTag, StringComparison.Ordinal);
      if (startIdx < 0)
        return null;
      startIdx += startTag.Length;
      int endIdx = remaining.IndexOf (endTag, startIdx, StringComparison.Ordinal);
      if (endIdx < 0)
        return null;
      return remaining.Substring (startIdx, endIdx - startIdx);
    }
  }
}
