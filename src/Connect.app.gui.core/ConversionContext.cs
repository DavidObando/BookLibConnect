using System;
using System.Threading;
using Oahu.Core;
using Oahu.Common.Util;

namespace Oahu.App.Gui {
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
