using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
public class GroundPatch : NetworkBehaviour
{
    private const float DefaultMeshRadius = 0.5f;

    private DebuffType effectType;
    private float magnitude;
    private float refreshInterval;
    private float debuffDuration;
    private float despawnTime;
    private ulong casterClientId;

    private readonly Dictionary<ulong, CharacterStats> occupants = new Dictionary<ulong, CharacterStats>();
    private readonly Dictionary<ulong, float> nextRefreshTime = new Dictionary<ulong, float>();

    public void Initialize(DebuffType type, float effectMagnitude, float effectRefreshInterval, float effectDebuffDuration, float patchDuration, float radius, ulong casterId)
    {
        effectType = type;
        magnitude = effectMagnitude;
        refreshInterval = effectRefreshInterval;
        debuffDuration = effectDebuffDuration;
        despawnTime = Time.time + patchDuration;
        casterClientId = casterId;

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

        List<ulong> occupantIds = new List<ulong>(occupants.Keys);
        foreach (ulong id in occupantIds)
        {
            if (Time.time < nextRefreshTime[id]) continue;
            nextRefreshTime[id] = Time.time + refreshInterval;
            occupants[id].ApplyDebuff(effectType, magnitude, refreshInterval, debuffDuration, casterClientId);
        }
    }
}
