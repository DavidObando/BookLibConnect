using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BookLibConnect.Common.Util {
  public interface IUpdateSettings {
    EOnlineUpdate OnlineUpdate { get; }
  }

  public class UpdateSettings : IUpdateSettings {
    public EOnlineUpdate OnlineUpdate { get; set; } = EOnlineUpdate.promptForDownload;
  }
}
