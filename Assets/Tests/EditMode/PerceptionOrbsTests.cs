using NUnit.Framework;
using UnityEngine;

public class PerceptionOrbsTests
{
    [Test]
    public void PrefabNamesMatchTheAuthoredIndicatorPrefabs()
    {
        Assert.AreEqual("ClosestProximity", PerceptionOrbs.PrefabNameFor(TargetingMode.Proximity));
        Assert.AreEqual("HighestThreat", PerceptionOrbs.PrefabNameFor(TargetingMode.HighestThreat));
        Assert.AreEqual("LowestThreat", PerceptionOrbs.PrefabNameFor(TargetingMode.LowestThreat));
        Assert.AreEqual("FurthestPlayer", PerceptionOrbs.PrefabNameFor(TargetingMode.FarthestPlayer));
        Assert.AreEqual("MostHealing", PerceptionOrbs.PrefabNameFor(TargetingMode.MostHealing));
    }

    [Test]
    public void OrbCenterFloatsAboveTheHead()
    {
        Vector3 center = PerceptionOrbs.OrbCenter(new Vector3(3f, 10f, -2f));
        Assert.AreEqual(3f, center.x, 1e-5f);
        Assert.AreEqual(10.6f, center.y, 1e-5f);
        Assert.AreEqual(-2f, center.z, 1e-5f);
    }

    // The geometry invariant behind the 0.6: the orb's bottom edge (centre
    // minus its radius) sits exactly BottomClearance above the head.
    [Test]
    public void OrbBottomEdgeClearsTheHeadByBottomClearance()
    {
        float centerHeight = PerceptionOrbs.OrbCenter(Vector3.zero).y;
        Assert.AreEqual(PerceptionOrbs.BottomClearance, centerHeight - PerceptionOrbs.OrbScale * 0.5f, 1e-5f);
        Assert.AreEqual(0.3f, PerceptionOrbs.BottomClearance, 1e-5f);
    }

    // Catches the Indicators folder landing outside Resources/ (the move
    // was a plain filesystem one) or a future TargetingMode with no prefab.
    [Test]
    public void EveryTargetingModeHasALoadableIndicatorPrefab()
    {
        foreach (TargetingMode mode in System.Enum.GetValues(typeof(TargetingMode)))
        {
            string path = "Prefabs/Indicators/" + PerceptionOrbs.PrefabNameFor(mode);
            Assert.IsNotNull(Resources.Load<GameObject>(path), $"No prefab at Resources/{path} for {mode}");
        }
    }
}
