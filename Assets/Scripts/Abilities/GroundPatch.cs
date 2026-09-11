using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// A lingering hazard: anyone standing in it has the patch's status effect
// reapplied every refreshInterval (which, by the tracker's extend-only
// rule, keeps the effect alive without ever shortening it). The patch's
// own lifetime is separate from the effect's duration on a victim.
[RequireComponent(typeof(SphereCollider))]
public class GroundPatch : NetworkBehaviour
{
    private const float DefaultMeshRadius = 0.5f;

    [SerializeField] private float refreshInterval = 1f;

    private StatusEffectData effect;
    private float despawnTime;
    private ulong casterClientId;

    private readonly Dictionary<ulong, CharacterStats> occupants = new Dictionary<ulong, CharacterStats>();
    private readonly Dictionary<ulong, float> nextRefreshTime = new Dictionary<ulong, float>();

    public void Initialize(StatusEffectData effectData, float patchDuration, float radius, ulong casterId)
    {
        effect = effectData;
        despawnTime = Time.time + patchDuration;
        casterClientId = casterId;

        // Visual scale and trigger radius both derive from the same value so
        // they can never disagree, regardless of the prefab's authored scale.
        float horizontalScale = radius / DefaultMeshRadius;
        Vector3 scale = transform.localScale;
        transform.localScale = new Vector3(horizontalScale, scale.y, horizontalScale);

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

        if (effect == null) return;

        List<ulong> occupantIds = new List<ulong>(occupants.Keys);
        foreach (ulong id in occupantIds)
        {
            if (Time.time < nextRefreshTime[id]) continue;
            nextRefreshTime[id] = Time.time + refreshInterval;
            occupants[id].ReceiveHit(new HitInfo { AttackerClientId = casterClientId, Effect = effect });
        }
    }
}
