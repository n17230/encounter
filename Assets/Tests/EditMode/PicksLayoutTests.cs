using System.Collections.Generic;
using NUnit.Framework;

public class PicksLayoutTests
{
    private struct Row
    {
        public ulong ClientId;
        public bool IsLocal;
    }

    [Test]
    public void RowsSortByClientIdAscendingWithTheLocalPlayerInPlace()
    {
        List<Row> rows = new List<Row>
        {
            new Row { ClientId = 3 },
            new Row { ClientId = 1, IsLocal = true },
            new Row { ClientId = 0 },
            new Row { ClientId = 2 },
        };

        PicksLayout.SortByClientId(rows, row => row.ClientId);

        Assert.AreEqual(0ul, rows[0].ClientId);
        Assert.AreEqual(1ul, rows[1].ClientId);
        Assert.IsTrue(rows[1].IsLocal);
        Assert.AreEqual(2ul, rows[2].ClientId);
        Assert.AreEqual(3ul, rows[3].ClientId);
    }

    [Test]
    public void EmptyPicksRenderNoIcons()
    {
        Assert.AreEqual(0, PicksLayout.ResolveIds("", _ => true).Count);
        Assert.AreEqual(0, PicksLayout.ResolveIds(null, _ => true).Count);
        Assert.AreEqual(0, PicksLayout.ResolveIds(";;;;", _ => true).Count);
    }

    [Test]
    public void UnknownIdsAreSkippedAndKnownOnesKeepTheirOrder()
    {
        HashSet<string> known = new HashSet<string> { "firebolt", "icebolt" };

        List<string> ids = PicksLayout.ResolveIds("firebolt;;removed_spell;icebolt", known.Contains);

        CollectionAssert.AreEqual(new[] { "firebolt", "icebolt" }, ids);
    }
}
