using NUnit.Framework;

public class RegistryTests
{
    private class Thing { }

    [TearDown]
    public void TearDown()
    {
        foreach (Thing thing in new System.Collections.Generic.List<Thing>(Registry<Thing>.All)) Registry<Thing>.Remove(thing);
    }

    [Test]
    public void AddThenRemoveRoundTrips()
    {
        Thing a = new Thing();
        Registry<Thing>.Add(a);
        Assert.AreEqual(1, Registry<Thing>.All.Count);

        Registry<Thing>.Remove(a);
        Assert.AreEqual(0, Registry<Thing>.All.Count);
    }

    // OnEnable can fire more than once for the same object (disable/
    // re-enable always pairs with a Remove, but a double Add must still
    // not make one object get hit twice by an area effect).
    [Test]
    public void AddingTheSameItemTwiceKeepsOneEntry()
    {
        Thing a = new Thing();
        Registry<Thing>.Add(a);
        Registry<Thing>.Add(a);
        Assert.AreEqual(1, Registry<Thing>.All.Count);
    }

    [Test]
    public void NullIsNeverRegistered()
    {
        Registry<Thing>.Add(null);
        Assert.AreEqual(0, Registry<Thing>.All.Count);
    }

    [Test]
    public void RemovingOneItemLeavesTheOthers()
    {
        Thing a = new Thing();
        Thing b = new Thing();
        Registry<Thing>.Add(a);
        Registry<Thing>.Add(b);

        Registry<Thing>.Remove(a);

        Assert.AreEqual(1, Registry<Thing>.All.Count);
        Assert.AreSame(b, Registry<Thing>.All[0]);
    }

    // The scratch list is reused - it must be emptied each time, or every
    // later area effect would hit each target once per earlier snapshot.
    [Test]
    public void ASecondSnapshotDoesNotAccumulateTheFirst()
    {
        Thing a = new Thing();
        Registry<Thing>.Add(a);

        Registry<Thing>.Snapshot();
        Assert.AreEqual(1, Registry<Thing>.Snapshot().Count);
    }

    // The whole point of Snapshot(): a hit applied mid-loop can disable
    // (unregister) a target without breaking the loop that's iterating.
    [Test]
    public void SnapshotIsUnaffectedByRemovalDuringIteration()
    {
        Thing a = new Thing();
        Thing b = new Thing();
        Registry<Thing>.Add(a);
        Registry<Thing>.Add(b);

        int visited = 0;
        foreach (Thing thing in Registry<Thing>.Snapshot())
        {
            Registry<Thing>.Remove(a);
            Registry<Thing>.Remove(b);
            visited++;
        }

        Assert.AreEqual(2, visited);
        Assert.AreEqual(0, Registry<Thing>.All.Count);
    }
}
