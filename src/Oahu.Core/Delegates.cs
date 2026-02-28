using System;
using Oahu.BooksDatabase;

namespace Oahu.Core {
  public delegate void ConvertDelegate<T> (Book book, T context, Action<Conversion> onNewStateCallback) where T : ICancellation;

  delegate ConfigurationTokenResult ConfigTokenDelegate (bool enforce = false);
}
