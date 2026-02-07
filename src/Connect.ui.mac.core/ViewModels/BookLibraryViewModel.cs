using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    public event EventHandler<IEnumerable<BookItemViewModel>> SelectionChanged;

    public void LoadBooks (IEnumerable<Book> books) {
      Books.Clear ();
      foreach (var book in books) {
        Books.Add (new BookItemViewModel (book));
      }
    }

    public IEnumerable<BookItemViewModel> GetSelectedBooks () =>
      Books.Where (b => b.IsSelected);

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
