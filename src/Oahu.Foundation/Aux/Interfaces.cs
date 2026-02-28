using System.Diagnostics;

namespace Oahu.Aux {
  public interface IProcessList {
    bool Add (Process process);
    bool Remove (Process process);
  }

  public interface IUserSettings {
  }

  public interface IInitSettings {
    void Init ();
  }
}
