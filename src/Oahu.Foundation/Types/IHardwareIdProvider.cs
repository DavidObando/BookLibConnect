namespace Oahu.CommonTypes
{
  public interface IHardwareIdProvider
  {
    string GetCpuId();

    string GetMotherboardId();

    string GetMotherboardPnpDeviceId();

    string GetDiskId();
  }
}
