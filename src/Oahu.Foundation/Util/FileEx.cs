using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Oahu.Common.Util {
  public interface IFileCopyCallout {
    void ProcessBuffer (byte[] buffer, int size, long globalOffset);
  }

  public static class FileEx {

    private const int BUFSIZ = 10 * 1000 * 1000; // 10 MB
    private const int IVL_MS = 50;


    public static bool Copy (string sourceFileName, string destFileName, bool overwrite, 
        Action<ProgressMessage> report = null, Func<bool> cancel = null) =>
     Copy (sourceFileName, destFileName, overwrite, null, report, cancel);

    public static bool Copy (string sourceFileName, string destFileName, bool overwrite, IFileCopyCallout callout, 
      Action<ProgressMessage> report = null, Func<bool> cancel = null
    ) {
      if (RuntimeInformation.IsOSPlatform (OSPlatform.Windows))
        return copyWin32 (sourceFileName, destFileName, overwrite, callout, report, cancel);
      else
        return copyPortable (sourceFileName, destFileName, overwrite, callout, report, cancel);
    }

    private static bool copyWin32 (string sourceFileName, string destFileName, bool overwrite, IFileCopyCallout callout, 
      Action<ProgressMessage> report, Func<bool> cancel
    ) {
      byte[] buf = new byte[BUFSIZ];

      DateTime dt0 = DateTime.Now;
      long ivlcnt = 0; 

      long total = new FileInfo (sourceFileName).Length;
      long count = 0;

      using (var threadProgress = new ThreadProgressPerMille (report)) { 
        using (var wfioRd = new Oahu.Aux.Win32.WinFileIO (buf)) 
        using (var wfioWr = new Oahu.Aux.Win32.WinFileIO (buf)) {
          wfioRd.OpenForReading (sourceFileName);
          wfioWr.OpenForWriting (destFileName, overwrite);

          int read = 0;
          while (true) {

            if (cancel?.Invoke () ?? false)
              return false;

            read = wfioRd.ReadBlocks (BUFSIZ);
            if (read <= 0)
              break;

            callout?.ProcessBuffer (buf, read, count);

            wfioWr.Write (read);
            
            count += read;
            DateTime dt = DateTime.Now;
            long tot_ms = (int)(dt - dt0).TotalMilliseconds;
            long q = tot_ms / IVL_MS;
            if (q <= ivlcnt)
              continue;

            ivlcnt = q;
            threadProgress.Report ((double)count / total);
          };
        }
      }
      return true;
    }

    private static bool copyPortable (string sourceFileName, string destFileName, bool overwrite, IFileCopyCallout callout, 
      Action<ProgressMessage> report, Func<bool> cancel
    ) {
      byte[] buf = new byte[BUFSIZ];

      DateTime dt0 = DateTime.Now;
      long ivlcnt = 0; 

      long total = new FileInfo (sourceFileName).Length;
      long count = 0;

      var mode = overwrite ? FileMode.Create : FileMode.CreateNew;

      using (var threadProgress = new ThreadProgressPerMille (report)) { 
        using (var fsRd = new FileStream (sourceFileName, FileMode.Open, FileAccess.Read, FileShare.Read, BUFSIZ))
        using (var fsWr = new FileStream (destFileName, mode, FileAccess.Write, FileShare.None, BUFSIZ)) {

          int read = 0;
          while (true) {

            if (cancel?.Invoke () ?? false)
              return false;

            read = fsRd.Read (buf, 0, BUFSIZ);
            if (read <= 0)
              break;

            callout?.ProcessBuffer (buf, read, count);

            fsWr.Write (buf, 0, read);
            
            count += read;
            DateTime dt = DateTime.Now;
            long tot_ms = (int)(dt - dt0).TotalMilliseconds;
            long q = tot_ms / IVL_MS;
            if (q <= ivlcnt)
              continue;

            ivlcnt = q;
            threadProgress.Report ((double)count / total);
          };
        }
      }
      return true;
    }
  }
}
