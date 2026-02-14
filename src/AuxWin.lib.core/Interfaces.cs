using System;
using System.ComponentModel;

namespace BookLibConnect.Aux.Win {
  public interface ISortingEvents {
    event EventHandler BeginSorting;
    event EventHandler EndSorting;
  }

  public interface ISortableBindingList : IBindingList, ISortingEvents {
  }
}
