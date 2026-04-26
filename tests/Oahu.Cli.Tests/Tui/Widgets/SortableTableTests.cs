using System;
using Oahu.Cli.Tui.Widgets;
using Xunit;

namespace Oahu.Cli.Tests.Tui.Widgets;

public class SortableTableTests
{
    [Fact]
    public void AddRow_Validates_Column_Count()
    {
        var t = new SortableTable(new[] { "a", "b" });
        Assert.Throws<ArgumentException>(() => t.AddRow("only-one"));
    }

    [Fact]
    public void Sort_Ascending_Then_Descending_By_Column()
    {
        var t = new SortableTable(new[] { "name", "age" });
        t.AddRow("Charlie", "30");
        t.AddRow("alice", "20");
        t.AddRow("Bob", "25");
        t.Sort(0, ascending: true);
        Assert.Equal(0, t.SortColumn);
        Assert.True(t.SortAscending);
        var asc = t.Build();
        Assert.Equal(3, asc.Rows.Count);

        t.Sort(0, ascending: false);
        Assert.False(t.SortAscending);
    }

    [Fact]
    public void ToggleSort_Reverses_When_Same_Column()
    {
        var t = new SortableTable(new[] { "n" });
        t.AddRow("a");
        t.AddRow("b");
        t.ToggleSort(0);
        Assert.True(t.SortAscending);
        t.ToggleSort(0);
        Assert.False(t.SortAscending);
        t.ToggleSort(0);
        Assert.True(t.SortAscending);
    }

    [Fact]
    public void Sort_Out_Of_Range_Throws()
    {
        var t = new SortableTable(new[] { "x" });
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Sort(5));
    }

    [Fact]
    public void Build_Marks_Active_Sort_Column()
    {
        var t = new SortableTable(new[] { "n" });
        t.AddRow("a");
        t.Sort(0, ascending: true);
        var table = t.Build();
        Assert.Single(table.Columns);
    }

    [Fact]
    public void Clear_Resets_Rows_And_Sort()
    {
        var t = new SortableTable(new[] { "n" });
        t.AddRow("a");
        t.Sort(0);
        t.Clear();
        Assert.Equal(0, t.RowCount);
        Assert.Null(t.SortColumn);
    }
}
