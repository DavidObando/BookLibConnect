using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BookLibConnect.BooksDatabase;

namespace BookLibConnect.Core.UI.Mac.ViewModels {
  public partial class BookLibraryViewModel : ObservableObject {

    [ObservableProperty]
    private ObservableCollection<BookItemViewModel> _books = new ();

    [ObservableProperty]
    private BookItemViewModel _selectedBook;

    [ObservableProperty]
    private bool _hasSelectedBook;

    partial void OnSelectedBookChanged (BookItemViewModel value) {
      HasSelectedBook = value is not null;
    }

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

      if (Books.Count > 0)
        SelectedBook = Books[0];
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

    // Detail properties
    public string Publisher => _book.PublisherName;
    public string Language => _book.Language;
    public string Unabridged => _book.Unabridged switch {
      true => "Yes",
      false => "No",
      _ => null
    };
    public string Series => _book.Series?.Count > 0
      ? string.Join (", ", _book.Series.Select (s => s.ToString ()))
      : null;
    public string ConversionStateText => ConversionState.ToString ();
    public int? Parts => _book.Components?.Count > 0 ? _book.Components.Count : (int?)null;

    public string Description {
      get {
        var html = _book.PublisherSummary;
        if (string.IsNullOrWhiteSpace (html))
          return null;
        // Strip HTML tags and decode common entities
        var text = Regex.Replace (html, "<[^>]+>", " ");
        text = text.Replace ("&amp;", "&")
                   .Replace ("&lt;", "<")
                   .Replace ("&gt;", ">")
                   .Replace ("&quot;", "\"")
                   .Replace ("&#39;", "'")
                   .Replace ("&nbsp;", " ");
        // Collapse whitespace
        text = Regex.Replace (text, @"\s+", " ").Trim ();
        return text;
      }
    }

    public string CoverImagePath {
      get {
        var path = _book.CoverImageFile;
        if (!string.IsNullOrEmpty (path) && File.Exists (path))
          return path;
        return null;
      }
    }

    public bool HasDetails => true;
  }
}
