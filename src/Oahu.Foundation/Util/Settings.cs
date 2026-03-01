namespace Oahu.Common.Util
{
  public interface IUpdateSettings
  {
    EOnlineUpdate OnlineUpdate { get; }
  }

  public class UpdateSettings : IUpdateSettings
  {
    public EOnlineUpdate OnlineUpdate { get; set; } = EOnlineUpdate.promptForDownload;
  }
}
