namespace core.audiamus.common {
  public interface IHardwareIdProvider {
    string GetCpuId ();
    string GetMotherboardId ();
    string GetMotherboardPnpDeviceId ();
    string GetDiskId ();
  }
}
