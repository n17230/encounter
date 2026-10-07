using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// A lingering effect zone: anyone standing in it has its status effect
// reapplied every refreshInterval (which, by the tracker's extend-only
// rule, keeps the effect alive without ever shortening it). The zone's own
// lifetime is separate from the effect's duration on a victim.
//
// Two shapes share this one class, differing only in what Initialize is
// given:
//  - a fixed ground patch (Firebolt/Icebolt's scattered fire/ice): stays
//    where it spawned, affects everyone including its caster;
//  - a following zone (Arctic Winds' dome): re-centers on a target every
//    tick, and never affects one excluded character (its caster), even if
//    they end up standing inside it.
[RequireComponent(typeof(SphereCollider))]
public class GroundPatch : NetworkBehaviour
{
    private const float DefaultMeshRadius = 0.5f;
    public const ulong NoExclusion = ulong.MaxValue;

    [SerializeField] private float refreshInterval = 1f;

    private StatusEffectData effect;
    private float despawnTime;
    private ulong casterClientId;
    private Transform followTarget;
    private ulong excludedNetworkObjectId = NoExclusion;

    private readonly Dictionary<ulong, CharacterStats> occupants = new Dictionary<ulong, CharacterStats>();
    private readonly Dictionary<ulong, float> nextRefreshTime = new Dictionary<ulong, float>();
    private readonly List<ulong> occupantScratch = new List<ulong>();

    // follow == null: a fixed patch. excludedId is a NetworkObjectId (not a
    // clientId - occupants are keyed by NetworkObjectId).
    public void Initialize(StatusEffectData effectData, float duration, float radius, ulong casterId,
        Transform follow = null, ulong excludedId = NoExclusion)
    {
        effect = effectData;
        despawnTime = Time.time + duration;
        casterClientId = casterId;
        followTarget = follow;
        excludedNetworkObjectId = excludedId;

        // Visual scale and trigger radius both derive from the same value so
        // they can never disagree, regardless of the prefab's authored
        // scale. A fixed patch is a flat disc (its height is the prefab's
        // own); a following zone is a dome, so it scales uniformly.
        float scale = radius / DefaultMeshRadius;
        transform.localScale = follow != null
            ? Vector3.one * scale
            : new Vector3(scale, transform.localScale.y, scale);

        SphereCollider trigger = GetComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = DefaultMeshRadius;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        CharacterStats otherStats = other.GetComponentInParent<CharacterStats>();
        NetworkObject otherNetworkObject = other.GetComponentInParent<NetworkObject>();
        if (otherStats == null || otherNetworkObject == null) return;

        ulong id = otherNetworkObject.NetworkObjectId;
        if (occupants.ContainsKey(id)) return;

        occupants[id] = otherStats;
        nextRefreshTime[id] = Time.time;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsServer) return;

        NetworkObject otherNetworkObject = other.GetComponentInParent<NetworkObject>();
        if (otherNetworkObject == null) return;

        RemoveOccupant(otherNetworkObject.NetworkObjectId);
    }

    private void RemoveOccupant(ulong id)
    {
        occupants.Remove(id);
        nextRefreshTime.Remove(id);
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        if (Time.time >= despawnTime)
        {
            NetworkObject.Despawn();
            return;
        }

        if (followTarget != null) transform.position = followTarget.position;

        if (effect == null) return;

        occupantScratch.Clear();
        occupantScratch.AddRange(occupants.Keys);
        foreach (ulong id in occupantScratch)
        {
            // An occupant that despawned while still inside (a mob that
            // burned to death, a player who disconnected) never fires
            // OnTriggerExit - drop it here instead of calling into a
            // destroyed object every refresh for the rest of the zone's life.
            if (occupants[id] == null)
            {
                RemoveOccupant(id);
                continue;
            }

            if (id == excludedNetworkObjectId) continue;
            if (Time.time < nextRefreshTime[id]) continue;
            nextRefreshTime[id] = Time.time + refreshInterval;
            occupants[id].ReceiveHit(new HitInfo { AttackerClientId = casterClientId, Source = HitSource.GroundPatch, Effect = effect });
        }
    }
}
