using System.Threading.Tasks;

namespace Oahu.Decrypt.FrameFilters.Video;

internal class VideoPassthrough : FrameFinalBase<FrameEntry>
{
  protected override int InputBufferSize => 1;

  public override Task AddInputAsync(FrameEntry input)
  {
    return Task.CompletedTask;
  }

  protected override Task FlushAsync() => Task.CompletedTask;

  protected override Task PerformFilteringAsync(FrameEntry input) => Task.CompletedTask;
}
