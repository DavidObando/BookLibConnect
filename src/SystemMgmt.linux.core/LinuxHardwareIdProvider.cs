using System;
using System.Diagnostics;
using System.IO;
using BookLibConnect.CommonTypes;

namespace BookLibConnect.SystemManagement.Linux {
  public class LinuxHardwareIdProvider : IHardwareIdProvider {

    private string _cachedCpuId;
    private string _cachedMotherboardId;
    private string _cachedDiskId;

    public string GetCpuId () {
      if (_cachedCpuId is not null)
        return _cachedCpuId;
      try {
        // Try lscpu for CPU model name
        string output = runCommand ("lscpu", "");
        _cachedCpuId = extractLscpuValue (output, "Model name") ?? string.Empty;
        return _cachedCpuId;
      } catch (Exception) {
        return string.Empty;
      }
    }

    public string GetMotherboardId () {
      if (_cachedMotherboardId is not null)
        return _cachedMotherboardId;
      try {
        // Use /etc/machine-id as primary stable identifier (systemd-based distros)
        string machineIdPath = "/etc/machine-id";
        if (File.Exists (machineIdPath)) {
          _cachedMotherboardId = File.ReadAllText (machineIdPath).Trim ();
          return _cachedMotherboardId;
        }
        // Fallback to DMI board serial
        string dmiPath = "/sys/class/dmi/id/board_serial";
        if (File.Exists (dmiPath)) {
          _cachedMotherboardId = File.ReadAllText (dmiPath).Trim ();
          return _cachedMotherboardId;
        }
        _cachedMotherboardId = string.Empty;
        return _cachedMotherboardId;
      } catch (Exception) {
        return string.Empty;
      }
    }

    public string GetMotherboardPnpDeviceId () {
      try {
        // Use DMI product name as a secondary identifier
        string dmiPath = "/sys/class/dmi/id/product_name";
        if (File.Exists (dmiPath))
          return File.ReadAllText (dmiPath).Trim ();
        return string.Empty;
      } catch (Exception) {
        return string.Empty;
      }
    }

    public string GetDiskId () {
      if (_cachedDiskId is not null)
        return _cachedDiskId;
      try {
        // Use lsblk to get the serial of the root disk
        string output = runCommand ("lsblk", "-ndo SERIAL /dev/sda");
        _cachedDiskId = output?.Trim () ?? string.Empty;
        if (string.IsNullOrEmpty (_cachedDiskId)) {
          // Fallback: try nvme drive
          output = runCommand ("lsblk", "-ndo SERIAL /dev/nvme0n1");
          _cachedDiskId = output?.Trim () ?? string.Empty;
        }
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

    private static string extractLscpuValue (string lscpuOutput, string key) {
      if (string.IsNullOrEmpty (lscpuOutput))
        return null;
      foreach (string line in lscpuOutput.Split ('\n')) {
        if (line.StartsWith (key, StringComparison.OrdinalIgnoreCase)) {
          int colonIdx = line.IndexOf (':');
          if (colonIdx >= 0)
            return line.Substring (colonIdx + 1).Trim ();
        }
      }
      return null;
    }
  }
}
