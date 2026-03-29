using System;
using System.Diagnostics;
using System.IO;

namespace Oahu.Aux
{
  public static class ShellExecute
  {
    public static void Url(Uri uri) => File(uri.OriginalString);

    public static void File(string url)
    {
      Process.Start(new ProcessStartInfo()
      {
        UseShellExecute = true,
        CreateNoWindow = true,
        FileName = url,
      });
    }

    public static void Directory(string path)
    {
      if (string.IsNullOrWhiteSpace(path))
      {
        throw new ArgumentException("Directory path is required.", nameof(path));
      }

      System.IO.Directory.CreateDirectory(path);

      ProcessStartInfo startInfo;
      if (OperatingSystem.IsWindows())
      {
        startInfo = new ProcessStartInfo
        {
          FileName = "explorer.exe",
          ArgumentList = { path },
          UseShellExecute = false,
          CreateNoWindow = true,
        };
      }
      else if (OperatingSystem.IsMacOS())
      {
        startInfo = new ProcessStartInfo
        {
          FileName = "open",
          ArgumentList = { path },
          UseShellExecute = false,
          CreateNoWindow = true,
        };
      }
      else if (OperatingSystem.IsLinux())
      {
        startInfo = new ProcessStartInfo
        {
          FileName = "xdg-open",
          ArgumentList = { path },
          UseShellExecute = false,
          CreateNoWindow = true,
        };
      }
      else
      {
        throw new PlatformNotSupportedException("Opening directories is not supported on this platform.");
      }

      Process.Start(startInfo);
    }
  }
}
