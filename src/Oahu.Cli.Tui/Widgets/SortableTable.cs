using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;

namespace Oahu.Cli.Tui.Widgets;

/// <summary>
/// Lightweight Spectre <see cref="Table"/> wrapper that supports re-sorting in
/// place by a column index. The widget owns no rendering state — callers grab
/// <see cref="Build"/> when they need a fresh <see cref="Table"/> to render.
/// </summary>
/// <remarks>
/// Sort is stable: equal-keyed rows preserve their original input order.
/// Comparison is ordinal/case-insensitive on the string projection of the cell;
/// callers that need numeric sort should pre-pad with leading zeros or supply a
/// custom <see cref="Comparison{T}"/> via <see cref="Sort(int, bool, Comparison{string}?)"/>.
/// </remarks>
public sealed class SortableTable
{
    private readonly List<string> headers;
    private readonly List<string[]> rows;
    private int sortColumn = -1;
    private bool sortAscending = true;

    public SortableTable(IEnumerable<string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        this.headers = headers.ToList();
        this.rows = new List<string[]>();
    }

    public int RowCount => rows.Count;

    public int ColumnCount => headers.Count;

    public int? SortColumn => sortColumn < 0 ? null : sortColumn;

    public bool SortAscending => sortAscending;

    public void AddRow(params string[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.Length != headers.Count)
        {
            throw new ArgumentException(
                $"Row has {cells.Length} cells but the table expects {headers.Count}.",
                nameof(cells));
        }
        rows.Add(cells);
    }

    public void Clear()
    {
        rows.Clear();
        sortColumn = -1;
        sortAscending = true;
    }

    /// <summary>Sort rows by <paramref name="columnIndex"/> in place.</summary>
    public void Sort(int columnIndex, bool ascending = true, Comparison<string>? comparer = null)
    {
        if (columnIndex < 0 || columnIndex >= headers.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(columnIndex));
        }
        var cmp = comparer ?? ((a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
        rows.Sort((l, r) => ascending ? cmp(l[columnIndex], r[columnIndex]) : cmp(r[columnIndex], l[columnIndex]));
        sortColumn = columnIndex;
        sortAscending = ascending;
    }

    /// <summary>
    /// Toggle the sort direction if <paramref name="columnIndex"/> is already
    /// the active sort column; otherwise sort ascending by that column.
    /// </summary>
    public void ToggleSort(int columnIndex)
    {
        if (columnIndex == sortColumn)
        {
            Sort(columnIndex, !sortAscending);
        }
        else
        {
            Sort(columnIndex, ascending: true);
        }
    }

    /// <summary>Build a fresh Spectre <see cref="Table"/> with the current header + row state.</summary>
    public Table Build()
    {
        var t = new Table();
        for (var i = 0; i < headers.Count; i++)
        {
            var marker = i == sortColumn ? (sortAscending ? " ▲" : " ▼") : string.Empty;
            t.AddColumn(headers[i] + marker);
        }
        foreach (var row in rows)
        {
            t.AddRow(row);
        }
        return t;
    }
}
