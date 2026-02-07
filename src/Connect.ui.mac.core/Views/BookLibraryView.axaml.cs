using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using core.audiamus.connect.ui.mac.ViewModels;

namespace core.audiamus.connect.ui.mac.Views {
  public partial class BookLibraryView : UserControl {
    public BookLibraryView () {
      InitializeComponent ();
    }

    protected override void OnLoaded (RoutedEventArgs e) {
      base.OnLoaded (e);

      if (booksGrid is null)
        return;

      // Save sort state whenever the user clicks a column header.
      // The Sorting event fires before the sort is applied, so we compute the
      // next direction using the same toggle logic the DataGrid uses internally:
      // null → Ascending → Descending → Ascending → …
      booksGrid.Sorting += (s, args) => {
        if (DataContext is BookLibraryViewModel vm && args.Column is not null) {
          int colIdx = booksGrid.Columns.IndexOf (args.Column);
          ListSortDirection next;
          if (vm.SortColumnIndex == colIdx && vm.SortDirection == ListSortDirection.Ascending)
            next = ListSortDirection.Descending;
          else
            next = ListSortDirection.Ascending;

          vm.SortColumnIndex = colIdx;
          vm.SortDirection = next;
        }
      };

      // Restore previously saved sort state
      restoreSortState ();
    }

    private void restoreSortState () {
      if (DataContext is not BookLibraryViewModel vm)
        return;
      if (vm.SortColumnIndex is null || vm.SortDirection is null)
        return;
      int idx = vm.SortColumnIndex.Value;
      if (idx < 0 || idx >= booksGrid.Columns.Count)
        return;

      var col = booksGrid.Columns[idx];

      // Clear any existing sort indicators
      foreach (var c in booksGrid.Columns)
        c.ClearSort ();

      col.Sort (vm.SortDirection.Value);
    }
  }
}
