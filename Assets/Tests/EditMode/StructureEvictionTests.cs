using System.Collections.Generic;
using NUnit.Framework;

public class StructureEvictionTests
{
    // A, B, C are plain strings; "dead" ones are those starting with "x".
    private static bool IsDead(string s) => s.StartsWith("x");

    [Test]
    public void RoomRemainingEvictsNothing()
    {
        List<string> empty = new List<string>();
        List<string> one = new List<string> { "A" };

        Assert.AreEqual(0, StructureEviction.MakeRoom(empty, 2, IsDead).Count);
        Assert.AreEqual(0, StructureEviction.MakeRoom(one, 2, IsDead).Count);
        Assert.AreEqual(new[] { "A" }, one);
    }

    [Test]
    public void FullListEvictsTheOldest()
    {
        List<string> active = new List<string> { "A", "B" };

        List<string> evicted = StructureEviction.MakeRoom(active, 2, IsDead);

        Assert.AreEqual(new[] { "A" }, evicted);
        Assert.AreEqual(new[] { "B" }, active);
    }

    [Test]
    public void CyclesOldestFirstAcrossCasts()
    {
        List<string> active = new List<string>();

        Assert.AreEqual(0, StructureEviction.MakeRoom(active, 2, IsDead).Count);
        active.Add("A");
        Assert.AreEqual(0, StructureEviction.MakeRoom(active, 2, IsDead).Count);
        active.Add("B");
        Assert.AreEqual(new[] { "A" }, StructureEviction.MakeRoom(active, 2, IsDead));
        active.Add("C");
        Assert.AreEqual(new[] { "B" }, StructureEviction.MakeRoom(active, 2, IsDead));
        active.Add("D");

        Assert.AreEqual(new[] { "C", "D" }, active);
    }

    [Test]
    public void DeadEntryFreesItsSlotWithoutEvictingALiveOne()
    {
        List<string> active = new List<string> { "A", "xB" };

        List<string> evicted = StructureEviction.MakeRoom(active, 2, IsDead);

        Assert.AreEqual(0, evicted.Count);
        Assert.AreEqual(new[] { "A" }, active);
    }

    [Test]
    public void OldestDeadEntryIsDroppedNotReturned()
    {
        List<string> active = new List<string> { "xA", "B", "C" };

        List<string> evicted = StructureEviction.MakeRoom(active, 2, IsDead);

        Assert.AreEqual(new[] { "B" }, evicted);
        Assert.AreEqual(new[] { "C" }, active);
    }

    [Test]
    public void MaxOneReplacesTheOnlyEntry()
    {
        List<string> active = new List<string> { "A" };

        Assert.AreEqual(new[] { "A" }, StructureEviction.MakeRoom(active, 1, IsDead));
        Assert.AreEqual(0, active.Count);
    }

    [TestCase(0)]
    [TestCase(-3)]
    public void MaxBelowOneBehavesAsOne(int max)
    {
        List<string> active = new List<string> { "A" };

        Assert.AreEqual(new[] { "A" }, StructureEviction.MakeRoom(active, max, IsDead));
    }

    [Test]
    public void OverFullListEvictsEnoughToMakeRoom()
    {
        List<string> active = new List<string> { "A", "B", "C" };

        List<string> evicted = StructureEviction.MakeRoom(active, 2, IsDead);

        Assert.AreEqual(new[] { "A", "B" }, evicted);
        Assert.AreEqual(new[] { "C" }, active);
    }

    [Test]
    public void NullPredicateLeavesEntriesAloneButStillEvictsTheOldest()
    {
        List<string> active = new List<string> { "A", "xB" };

        List<string> evicted = StructureEviction.MakeRoom(active, 2, null);

        Assert.AreEqual(new[] { "A" }, evicted);
        Assert.AreEqual(new[] { "xB" }, active);
    }

    [Test]
    public void CapLargerThanTheListEvictsNothing()
    {
        List<string> active = new List<string> { "A", "B" };

        Assert.AreEqual(0, StructureEviction.MakeRoom(active, 5, IsDead).Count);
        Assert.AreEqual(new[] { "A", "B" }, active);
    }

    [Test]
    public void InterleavedDeadEntriesAreAllPrunedBeforeCounting()
    {
        List<string> active = new List<string> { "A", "xB", "C", "xD" };

        // Two live entries left; a cap of 3 still has room for one more.
        Assert.AreEqual(0, StructureEviction.MakeRoom(active, 3, IsDead).Count);
        Assert.AreEqual(new[] { "A", "C" }, active);

        // A cap of 2 is full with them, so only the oldest live one goes.
        List<string> full = new List<string> { "A", "xB", "C", "xD" };
        Assert.AreEqual(new[] { "A" }, StructureEviction.MakeRoom(full, 2, IsDead));
        Assert.AreEqual(new[] { "C" }, full);
    }

    private class Wall
    {
        public bool Dead;
    }

    [Test]
    public void EvictedItemsAreTheSameInstances()
    {
        Wall first = new Wall();
        Wall second = new Wall();
        Wall third = new Wall { Dead = true };
        List<Wall> active = new List<Wall> { first, third, second };

        List<Wall> evicted = StructureEviction.MakeRoom(active, 2, wall => wall.Dead);

        Assert.AreEqual(1, evicted.Count);
        Assert.AreSame(first, evicted[0]);
        Assert.AreEqual(1, active.Count);
        Assert.AreSame(second, active[0]);
    }

    [Test]
    public void NullListIsEmpty()
    {
        Assert.AreEqual(0, StructureEviction.MakeRoom<string>(null, 2, IsDead).Count);
    }
}
