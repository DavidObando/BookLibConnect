using System;
using System.Diagnostics;
using System.IO;
using Oahu.CommonTypes;

namespace Oahu.SystemManagement
{
  public class LinuxHardwareIdProvider : IHardwareIdProvider
  {
    private string cachedCpuId;
    private string cachedMotherboardId;
    private string cachedDiskId;

    public string GetCpuId()
    {
      if (cachedCpuId is not null)
      {
        return cachedCpuId;
      }

      try
      {
        // Try lscpu for CPU model name
        string output = RunCommand("lscpu", "");
        cachedCpuId = ExtractLscpuValue(output, "Model name") ?? string.Empty;
        return cachedCpuId;
      }
      catch (Exception)
      {
        return string.Empty;
      }
    }

    public string GetMotherboardId()
    {
      if (cachedMotherboardId is not null)
      {
        return cachedMotherboardId;
      }

      try
      {
        // Use /etc/machine-id as primary stable identifier (systemd-based distros)
        string machineIdPath = "/etc/machine-id";
        if (File.Exists(machineIdPath))
        {
          cachedMotherboardId = File.ReadAllText(machineIdPath).Trim();
          return cachedMotherboardId;
        }

        // Fallback to DMI board serial
        string dmiPath = "/sys/class/dmi/id/board_serial";
        if (File.Exists(dmiPath))
        {
          cachedMotherboardId = File.ReadAllText(dmiPath).Trim();
          return cachedMotherboardId;
        }

        cachedMotherboardId = string.Empty;
        return cachedMotherboardId;
      }
      catch (Exception)
      {
        return string.Empty;
      }
    }

    public string GetMotherboardPnpDeviceId()
    {
      try
      {
        // Use DMI product name as a secondary identifier
        string dmiPath = "/sys/class/dmi/id/product_name";
        if (File.Exists(dmiPath))
        {
          return File.ReadAllText(dmiPath).Trim();
        }

        return string.Empty;
      }
      catch (Exception)
      {
        return string.Empty;
      }
    }

    public string GetDiskId()
    {
      if (cachedDiskId is not null)
      {
        return cachedDiskId;
      }

      try
      {
        // Use lsblk to get the serial of the root disk
        string output = RunCommand("lsblk", "-ndo SERIAL /dev/sda");
        cachedDiskId = output?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(cachedDiskId))
        {
          // Fallback: try nvme drive
          output = RunCommand("lsblk", "-ndo SERIAL /dev/nvme0n1");
          cachedDiskId = output?.Trim() ?? string.Empty;
        }

        return cachedDiskId;
      }
      catch (Exception)
      {
        return string.Empty;
      }
    }

    private static string RunCommand(string command, string arguments)
    {
      try
      {
        var psi = new ProcessStartInfo
        {
          FileName = command,
          Arguments = arguments,
          RedirectStandardOutput = true,
          UseShellExecute = false,
          CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        string output = process?.StandardOutput.ReadToEnd();
        process?.WaitForExit();
        return output;
      }
      catch (Exception)
      {
        return null;
      }
    }

    private static string ExtractLscpuValue(string lscpuOutput, string key)
    {
      if (string.IsNullOrEmpty(lscpuOutput))
      {
        return null;
      }

      foreach (string line in lscpuOutput.Split('\n'))
      {
        if (line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
        {
          int colonIdx = line.IndexOf(':');
          if (colonIdx >= 0)
          {
            return line.Substring(colonIdx + 1).Trim();
          }
        }
      }

      return null;
    }
  }
}
