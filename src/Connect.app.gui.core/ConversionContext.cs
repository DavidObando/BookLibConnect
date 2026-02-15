using System;
using System.Threading;
using BookLibConnect.Core;
using BookLibConnect.Common.Util;

namespace BookLibConnect.App.Gui {
  class ConversionContext : ICancellation {
    public IProgress<ProgressMessage> Progress { get; }
    public CancellationToken CancellationToken { get; }

    public ConversionContext (IProgress<ProgressMessage> progress, CancellationToken token) {
      Progress = progress;
      CancellationToken = token;
    }

    public void Init (int nItems) {
      Progress?.Report (new (nItems, null, null, null));
    }
  }
}
