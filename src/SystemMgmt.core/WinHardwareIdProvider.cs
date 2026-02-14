using BookLibConnect.CommonTypes;

namespace BookLibConnect.SystemManagement {
  public class WinHardwareIdProvider : IHardwareIdProvider {
    public string GetCpuId () => HardwareId.GetCpuId ();
    public string GetMotherboardId () => HardwareId.GetMotherboardId ();
    public string GetMotherboardPnpDeviceId () => MotherboardInfo.PNPDeviceID;
    public string GetDiskId () => HardwareId.GetDiskId ();
  }
}
