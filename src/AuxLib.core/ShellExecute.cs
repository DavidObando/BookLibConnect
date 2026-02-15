using System;
using System.Diagnostics;

namespace BookLibConnect.Aux {
  public static class ShellExecute {
    public static void Url (Uri uri) => File (uri.OriginalString);

    public static void File (string url) {
      Process.Start (new ProcessStartInfo () {
        UseShellExecute = true,
        CreateNoWindow = true,
        FileName = url,
      });

    }
  }
}
