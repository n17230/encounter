using Unity.Netcode;
using UnityEngine;

// A persistent, server-spawned obstacle with a real (non-trigger)
// collider - e.g. Earthen Bastion's wall. Unlike GroundPatch (a timed
// hazard trigger that never blocks movement), this physically stops
// CharacterController.Move like any other Default-layer prop, and has no
// lifetime of its own - it only ever despawns when ServerDespawn is
// called (see PlayerAbilities.ResolvePersistentStructure - recasting the
// ability that placed it removes the old one first).
[RequireComponent(typeof(BoxCollider))]
public class PlacedStructure : NetworkBehaviour
{
    // Scales the whole object so a BoxCollider's default (1,1,1) size
    // always matches the visual exactly, regardless of the prefab's
    // authored scale - same reasoning as GroundPatch's radius handling,
    // just simpler since a Cube's default extents are already 1 unit.
    public void Initialize(float width, float height, float thickness)
    {
        transform.localScale = new Vector3(width, height, thickness);
    }

    public void ServerDespawn()
    {
        if (!IsServer) return;
        if (NetworkObject != null && NetworkObject.IsSpawned) NetworkObject.Despawn();
    }
}
