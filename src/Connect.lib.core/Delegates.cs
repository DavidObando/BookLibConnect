using System;
using BookLibConnect.BooksDatabase;

namespace BookLibConnect.Core {
  public delegate void ConvertDelegate<T> (Book book, T context, Action<Conversion> onNewStateCallback) where T : ICancellation;

  delegate ConfigurationTokenResult ConfigTokenDelegate (bool enforce = false);
}
