using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Like GroundPatch (a lingering hazard that reapplies its effect to
// whoever's standing in it), except the trigger volume tracks a target's
// position every tick instead of staying where it was spawned - e.g.
// Arctic Winds' dome. Never affects the caster themselves, even if they
// end up standing inside it. Despawns itself once its own duration runs
// out, regardless of what happens to the target it was following.
[RequireComponent(typeof(SphereCollider))]
public class FollowingZone : NetworkBehaviour
{
    [SerializeField] private float refreshInterval = 1f;

    private Transform followTarget;
    private StatusEffectData effect;
    private float despawnTime;
    private ulong casterClientId;

    // The caster's own NetworkObjectId (not clientId - occupants are keyed
    // by NetworkObjectId) - never affected even if they end up standing
    // inside their own zone. E.g. Arctic Winds doesn't slow its caster.
    private ulong casterNetworkObjectId;

    private readonly Dictionary<ulong, CharacterStats> occupants = new Dictionary<ulong, CharacterStats>();
    private readonly Dictionary<ulong, float> nextRefreshTime = new Dictionary<ulong, float>();

    public void Initialize(Transform target, StatusEffectData effectData, float duration, float radius, ulong casterId, ulong casterNetworkId)
    {
        followTarget = target;
        effect = effectData;
        despawnTime = Time.time + duration;
        casterClientId = casterId;
        casterNetworkObjectId = casterNetworkId;

        SphereCollider trigger = GetComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = radius;
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

        ulong id = otherNetworkObject.NetworkObjectId;
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

        List<ulong> occupantIds = new List<ulong>(occupants.Keys);
        foreach (ulong id in occupantIds)
        {
            if (id == casterNetworkObjectId) continue; // never affects the caster
            if (Time.time < nextRefreshTime[id]) continue;
            nextRefreshTime[id] = Time.time + refreshInterval;
            occupants[id].ReceiveHit(new HitInfo { AttackerClientId = casterClientId, Source = HitSource.GroundPatch, Effect = effect });
        }
    }
}
