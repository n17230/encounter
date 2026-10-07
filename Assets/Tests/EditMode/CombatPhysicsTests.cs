using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// Built far away from the origin so nothing in whatever scene happens to
// be open in the Editor can sit on the test's sight line.
public class CombatPhysicsTests
{
    private static readonly Vector3 From = new Vector3(5000f, 5000f, 5000f);
    private static readonly Vector3 To = From + Vector3.forward * 20f;

    private readonly List<GameObject> spawned = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in spawned) Object.DestroyImmediate(go);
        spawned.Clear();
    }

    // A 2x4x2 box centered on the sight line, distanceAlong units from From.
    private GameObject Obstacle(float distanceAlong, bool isTrigger = false, bool isCreature = false)
    {
        GameObject go = new GameObject("obstacle");
        go.transform.position = From + Vector3.forward * distanceAlong + Vector3.up * CombatPhysics.SightHeight;
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(2f, 4f, 2f);
        box.isTrigger = isTrigger;
        if (isCreature) go.AddComponent<Targetable>();
        spawned.Add(go);
        Physics.SyncTransforms();
        return go;
    }

    [Test]
    public void ClearLineHasSight()
    {
        Assert.IsTrue(CombatPhysics.HasLineOfSight(From, To));
    }

    [Test]
    public void AWallBlocksSight()
    {
        Obstacle(10f);
        Assert.IsFalse(CombatPhysics.HasLineOfSight(From, To));
    }

    [Test]
    public void ACreatureInTheWayDoesNotBlockSight()
    {
        Obstacle(10f, isCreature: true);
        Assert.IsTrue(CombatPhysics.HasLineOfSight(From, To));
    }

    // The old implementation only looked at the NEAREST hit, so a mob
    // standing in front of a wall made the wall invisible.
    [Test]
    public void AWallBehindACreatureStillBlocksSight()
    {
        Obstacle(5f, isCreature: true);
        Obstacle(12f);
        Assert.IsFalse(CombatPhysics.HasLineOfSight(From, To));
    }

    // Ground patches, following zones and pickups are trigger volumes,
    // not walls.
    [Test]
    public void ATriggerVolumeDoesNotBlockSight()
    {
        Obstacle(10f, isTrigger: true);
        Assert.IsTrue(CombatPhysics.HasLineOfSight(From, To));
    }

    [Test]
    public void AWallBeyondTheTargetDoesNotBlockSight()
    {
        Obstacle(30f);
        Assert.IsTrue(CombatPhysics.HasLineOfSight(From, To));
    }

    // The line runs at SightHeight, not along the ground between two
    // characters' feet - a knee-high rock between them must not block.
    [Test]
    public void ALowObstacleAtFootLevelDoesNotBlockSight()
    {
        GameObject rock = new GameObject("rock");
        rock.transform.position = From + Vector3.forward * 10f + Vector3.up * 0.25f;
        rock.AddComponent<BoxCollider>().size = new Vector3(2f, 0.5f, 2f);
        spawned.Add(rock);
        Physics.SyncTransforms();

        Assert.IsTrue(CombatPhysics.HasLineOfSight(From, To));
    }

    // Self-targeted casts: zero-length line.
    [Test]
    public void ZeroDistanceAlwaysHasSight()
    {
        Assert.IsTrue(CombatPhysics.HasLineOfSight(From, From));
    }

    // Real characters carry their collider on the root or on a child of
    // the object holding Targetable - a creature's child collider must
    // count as that creature, not as a wall.
    [Test]
    public void ACreaturesChildColliderDoesNotBlockSight()
    {
        GameObject creature = new GameObject("creature");
        creature.transform.position = From + Vector3.forward * 10f;
        creature.AddComponent<Targetable>();
        spawned.Add(creature);

        GameObject body = new GameObject("body");
        body.transform.SetParent(creature.transform, false);
        body.transform.localPosition = Vector3.up * CombatPhysics.SightHeight;
        body.AddComponent<BoxCollider>().size = new Vector3(2f, 4f, 2f);
        Physics.SyncTransforms();

        Assert.IsTrue(CombatPhysics.HasLineOfSight(From, To));
    }
}
