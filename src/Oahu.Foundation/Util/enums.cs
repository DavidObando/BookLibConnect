using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Oahu.Common.Util {
  public enum EOnlineUpdate {
    no,
    promptForDownload,
    promptForInstall
  }

  public enum EUpdateInteract {
    newVersAvail,
    installNow,
    installLater,
  }
}
