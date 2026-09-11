using Unity.Netcode;
using UnityEngine;

public class Projectile : NetworkBehaviour
{
    // Baked onto the prefab itself (FireBolt/IceBolt each have their own),
    // not AbilityData - a raw prefab reference can't cross an RPC, and this
    // way no data needs to travel at all beyond "impact happened here".
    [SerializeField] private GameObject impactVfxPrefab;

    private ulong targetNetworkObjectId;
    private AbilityData ability;
    private ulong casterClientId;
    private bool initialized;

    public void Initialize(ulong targetId, AbilityData abilityData, ulong casterId)
    {
        targetNetworkObjectId = targetId;
        ability = abilityData;
        casterClientId = casterId;
        initialized = true;
    }

    private void FixedUpdate()
    {
        if (!IsServer || !initialized) return;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObject))
        {
            NetworkObject.Despawn();
            return;
        }

        Vector3 targetPosition = targetObject.transform.position + Vector3.up;
        Vector3 toTarget = targetPosition - transform.position;
        float distance = toTarget.magnitude;
        float step = ability.MissileSpeed * Time.fixedDeltaTime;

        if (distance <= step)
        {
            Impact(targetObject);
            return;
        }

        Vector3 direction = toTarget.normalized;
        transform.rotation = Quaternion.LookRotation(direction);
        transform.position += direction * step;
    }

    private void Impact(NetworkObject targetObject)
    {
        Targetable target = targetObject.GetComponent<Targetable>();
        if (target != null && target.Stats != null)
        {
            target.Stats.ApplyDamage(ability.Damage, casterClientId);

            float directHitDuration = ability.DirectHitDebuffDuration > 0f ? ability.DirectHitDebuffDuration : ability.DebuffDuration;
            target.Stats.ApplyDebuff(ability.Debuff, ability.DebuffMagnitude, ability.DebuffTickInterval, directHitDuration, casterClientId);

            if (ability.ThreatValue > 0f) target.Stats.AddThreat(ability.ThreatValue, casterClientId);
        }

        PlayImpactVfxClientRpc(transform.position);
        SpawnPatches(targetObject.transform.position.x, targetObject.transform.position.z);
        NetworkObject.Despawn();
    }

    [ClientRpc]
    private void PlayImpactVfxClientRpc(Vector3 position)
    {
        if (impactVfxPrefab == null) return;
        GameObject vfxInstance = Instantiate(impactVfxPrefab, position, Quaternion.identity);
        Destroy(vfxInstance, 5f);
    }

    // Terrain height varies across the map (and keeps changing as it's
    // sculpted), so each patch samples the actual ground height at its own
    // scattered X/Z rather than assuming a flat floor - a patch scattered
    // onto a slope needs its own height, not just the impact point's.
    private float GetGroundHeight(float x, float z)
    {
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null) return 0f;

        return terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
    }

    private void SpawnPatches(float centerX, float centerZ)
    {
        if (ability.GroundPatchPrefab == null || ability.MaxPatchCount <= 0) return;

        int count = Random.Range(ability.MinPatchCount, ability.MaxPatchCount + 1);
        for (int i = 0; i < count; i++)
        {
            Vector2 offset = Random.insideUnitCircle * ability.PatchScatterRadius;
            float x = centerX + offset.x;
            float z = centerZ + offset.y;
            Vector3 spawnPosition = new Vector3(x, GetGroundHeight(x, z), z);

            GameObject patchInstance = Instantiate(ability.GroundPatchPrefab, spawnPosition, Quaternion.identity);
            patchInstance.GetComponent<NetworkObject>().Spawn();
            patchInstance.GetComponent<GroundPatch>().Initialize(
                ability.Debuff, ability.DebuffMagnitude, ability.DebuffTickInterval, ability.DebuffDuration, ability.PatchDuration, ability.PatchRadius, casterClientId);
        }
    }
}
