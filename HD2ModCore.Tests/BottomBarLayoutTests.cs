using HD2ModManager.ViewModels;

namespace HD2ModCore.Tests;

public sealed class BottomBarLayoutTests
{
    [Fact]
    public void DefaultRegistrationsFillRowsFromBottomToTop()
    {
        var store = new BottomBarRegistrationStore();

        store.Register(Request("first"));
        store.Register(Request("second"));

        Assert.Collection(store.Snapshot.Rows,
            row => Assert.Equal(("first:main", 1), (row.Key, row.RowNumber)),
            row => Assert.Equal(("second:main", 2), (row.Key, row.RowNumber)));
    }

    [Fact]
    public void ForcedRowIsAnInsertionHintAndLaterRowsCompact()
    {
        var store = new BottomBarRegistrationStore();
        store.Register(Request("first"));
        store.Register(Request("third", requestedRow: 4));
        store.Register(Request("bottom", requestedRow: 1));
        store.Remove("first");

        Assert.Collection(store.Snapshot.Rows,
            row => Assert.Equal(("bottom:main", 1), (row.Key, row.RowNumber)),
            row => Assert.Equal(("third:main", 2), (row.Key, row.RowNumber)));
    }

    [Fact]
    public void ZeroAndLargeForcedRowsInsertAtTopAndMultiRowBlocksStayContiguous()
    {
        var store = new BottomBarRegistrationStore();
        store.Register(Request("base"));
        store.Register(Request("top-zero", requestedRow: 0));
        store.Register(new BottomBarRegistrationRequest("block",
            [new BottomBarRowDefinition("a", "A"), new BottomBarRowDefinition("b", "B")],
            InsertAtRow: 1));
        store.Register(Request("top-large", requestedRow: 999));

        Assert.Equal(
            ["block:a", "block:b", "base:main", "top-zero:main", "top-large:main"],
            store.Snapshot.Rows.Select(row => row.Key));
    }

    [Fact]
    public void ForcedRowCountsActualRowsRatherThanSources()
    {
        var store = new BottomBarRegistrationStore();
        store.Register(new BottomBarRegistrationRequest("block",
            [new BottomBarRowDefinition("a", "A"), new BottomBarRowDefinition("b", "B")]));
        store.Register(Request("top"));
        store.Register(Request("insert", requestedRow: 3));

        Assert.Equal(
            ["block:a", "block:b", "insert:main", "top:main"],
            store.Snapshot.Rows.Select(row => row.Key));
    }

    private static BottomBarRegistrationRequest Request(string sourceId, int? requestedRow = null)
        => new(sourceId, [new BottomBarRowDefinition("main", sourceId)], requestedRow);
}
