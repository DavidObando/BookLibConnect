using System.Threading;
using Oahu.Core;

namespace Oahu.App.Avalonia
{
  /// <summary>
  /// Minimal implementation of <see cref="ICancellation"/> for use with
  /// <see cref="DownloadDecryptJob{T}"/>.
  /// </summary>
  public class SimpleCancellation : ICancellation
  {
    public CancellationToken CancellationToken { get; }

    public SimpleCancellation(CancellationToken token)
    {
      CancellationToken = token;
    }
  }
}
