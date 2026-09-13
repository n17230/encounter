using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Testing-lobby feature only: lets players spawn extra mobs to fight. Not
// part of the real game (real content spawns mobs via encounter design,
// not player choice). The UI lives on MainMenu's Escape-menu Summon page;
// this component owns the mob list and the server-side spawning.
public class PlayerSummon : NetworkBehaviour
{
    [SerializeField] private GameObject[] summonableMobs;
    [SerializeField] private float summonRadius = 100f;
    [SerializeField] private int maxSummonCount = 20;

    // Skeleton Tactician escort - a single button spawns the whole group at
    // once (see BOSS_DESIGN.md), rather than one mob type at a time like
    // the rest of this menu.
    [SerializeField] private GameObject skeletonWarriorPrefab;
    [SerializeField] private GameObject skeletonArcherPrefab;
    [SerializeField] private GameObject skeletonHealerPrefab;
    [SerializeField] private GameObject skeletonMagePrefab;
    [SerializeField] private GameObject skeletonTacticianPrefab;
    [SerializeField] private float encounterFormationSpacing = 3f;

    public IReadOnlyList<GameObject> SummonableMobs => summonableMobs;
    public int MaxSummonCount => maxSummonCount;

    public static string MobLabel(GameObject prefab)
    {
        Targetable targetable = prefab.GetComponent<Targetable>();
        return targetable != null ? targetable.DisplayName : prefab.name;
    }

    public void RequestSummon(int mobIndex, int count)
    {
        if (!IsOwner) return;
        RequestSummonServerRpc(mobIndex, count);
    }

    [ServerRpc]
    private void RequestSummonServerRpc(int mobIndex, int count)
    {
        if (mobIndex < 0 || mobIndex >= summonableMobs.Length) return;

        count = Mathf.Clamp(count, 1, maxSummonCount);
        GameObject prefab = summonableMobs[mobIndex];
        // Spawned on a ring around the summoning player's own position, not
        // the map's center/edge - so it works the same wherever the player
        // currently is.
        Vector3 center = transform.position;

        for (int i = 0; i < count; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float x = center.x + Mathf.Cos(angle) * summonRadius;
            float z = center.z + Mathf.Sin(angle) * summonRadius;
            Vector3 spawnPosition = new Vector3(x, GetSpawnHeight(x, z), z);

            GameObject instance = Instantiate(prefab, spawnPosition, Quaternion.identity);
            instance.GetComponent<NetworkObject>().Spawn();
        }
    }

    public void RequestSummonSkeletonEncounter()
    {
        if (!IsOwner) return;
        RequestSummonSkeletonEncounterServerRpc();
    }

    [ServerRpc]
    private void RequestSummonSkeletonEncounterServerRpc()
    {
        float angle = Random.Range(0f, Mathf.PI * 2f);
        Vector3 anchor = transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * summonRadius;

        // A tight two-row cluster so the whole escort spawns next to each
        // other rather than scattered - Warriors/Archer up front, Healer/
        // Mage/Tactician just behind. Spacing-only formation, no facing.
        float s = encounterFormationSpacing;
        (GameObject prefab, Vector3 offset)[] group =
        {
            (skeletonWarriorPrefab, new Vector3(-s, 0f, 0f)),
            (skeletonWarriorPrefab, new Vector3(s, 0f, 0f)),
            (skeletonArcherPrefab, new Vector3(0f, 0f, 0f)),
            (skeletonHealerPrefab, new Vector3(-s, 0f, -s)),
            (skeletonMagePrefab, new Vector3(s, 0f, -s)),
            (skeletonTacticianPrefab, new Vector3(0f, 0f, -s)),
        };

        foreach ((GameObject prefab, Vector3 offset) in group)
        {
            if (prefab == null) continue;
            float x = anchor.x + offset.x;
            float z = anchor.z + offset.z;
            Vector3 spawnPosition = new Vector3(x, GetSpawnHeight(x, z), z);

            GameObject instance = Instantiate(prefab, spawnPosition, Quaternion.identity);
            instance.GetComponent<NetworkObject>().Spawn();
        }
    }

    // Terrain height varies across the map (and keeps changing as it's
    // sculpted), so mobs spawn a safe margin above the actual terrain
    // surface at their landing spot rather than a hardcoded Y - each mob's
    // own EnemyAI gravity handling settles it onto the ground correctly
    // from there regardless of its own height/scale.
    private float GetSpawnHeight(float x, float z)
    {
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null) return 1f;

        return terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y + 2f;
    }
}
