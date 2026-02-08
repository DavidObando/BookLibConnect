using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using core.audiamus.booksdb;

namespace core.audiamus.connect.ui.mac.ViewModels {
  public partial class BookLibraryViewModel : ObservableObject {

    [ObservableProperty]
    private ObservableCollection<BookItemViewModel> _books = new ();

    [ObservableProperty]
    private BookItemViewModel _selectedBook;

    [ObservableProperty]
    private string _filterText;

    [ObservableProperty]
    private bool _downloadSelectEnabled;

    [ObservableProperty]
    private int _selectedCount;

    // Sort state remembered within the session
    public int? SortColumnIndex { get; set; }
    public ListSortDirection? SortDirection { get; set; }

    public event EventHandler<IEnumerable<BookItemViewModel>> DownloadRequested;

    public void LoadBooks (IEnumerable<Book> books) {
      Books.Clear ();
      foreach (var book in books) {
        var vm = new BookItemViewModel (book);
        vm.PropertyChanged += (s, e) => {
          if (e.PropertyName == nameof (BookItemViewModel.IsSelected))
            UpdateSelectedCount ();
        };
        Books.Add (vm);
      }
      UpdateSelectedCount ();
    }

    public IEnumerable<BookItemViewModel> GetSelectedBooks () =>
      Books.Where (b => b.IsSelected);

    public void UpdateSelectedCount () =>
      SelectedCount = Books.Count (b => b.IsSelected);

    [RelayCommand]
    private void SelectAll () {
      foreach (var book in Books)
        book.IsSelected = true;
    }

    [RelayCommand]
    private void DeselectAll () {
      foreach (var book in Books)
        book.IsSelected = false;
    }

    [RelayCommand]
    private void DownloadSelected () {
      var selected = GetSelectedBooks ().ToList ();
      if (selected.Count > 0)
        DownloadRequested?.Invoke (this, selected);
    }
  }

  public partial class BookItemViewModel : ObservableObject {
    private readonly Book _book;

    public BookItemViewModel (Book book) {
      _book = book;
    }

    public Book Book => _book;
    public string Asin => _book.Asin;
    public string Title => _book.Title;
    public string Author => _book.Author;
    public string Narrator => _book.Narrator;
    public DateTime? PurchaseDate => _book.PurchaseDate;
    public DateTime? ReleaseDate => _book.ReleaseDate;
    public int? RunTimeLengthSeconds => _book.RunTimeLengthSeconds;
    public string CoverImageFile => _book.CoverImageFile;
    public EConversionState ConversionState => _book.Conversion?.State ?? EConversionState.unknown;

    [ObservableProperty]
    private bool _isSelected;

    public string Duration {
      get {
        if (RunTimeLengthSeconds is null)
          return null;
        var ts = TimeSpan.FromSeconds (RunTimeLengthSeconds.Value);
        return ts.TotalHours >= 1
          ? $"{(int)ts.TotalHours}h {ts.Minutes:D2}m"
          : $"{ts.Minutes}m";
      }
    }
  }
}
