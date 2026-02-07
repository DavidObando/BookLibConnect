using System.Threading;
using core.audiamus.connect;

namespace core.audiamus.connect.app.mac {
  /// <summary>
  /// Minimal implementation of <see cref="ICancellation"/> for use with
  /// <see cref="DownloadDecryptJob{T}"/>.
  /// </summary>
  public class SimpleCancellation : ICancellation {
    public CancellationToken CancellationToken { get; }

    public SimpleCancellation (CancellationToken token) {
      CancellationToken = token;
    }
  }
}
