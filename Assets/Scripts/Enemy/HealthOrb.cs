using Unity.Netcode;
using UnityEngine;

// A mob death drop: any player who touches it gets a flat percentage of
// their own max health restored and it's consumed. Spawned by
// EnemyAI.HandleDeath via Resources.Load (see TrySpawn), same pattern as
// ManaOrb.
[RequireComponent(typeof(SphereCollider))]
public class HealthOrb : NetworkBehaviour
{
    private const float HealPercent = 0.3f;

    // Loaded once per drop rather than cached, since Resources.Load is cheap
    // and this keeps TrySpawn a single self-contained entry point.
    public static void TrySpawn(Vector3 position)
    {
        GameObject prefab = Resources.Load<GameObject>("Prefabs/HealthOrb");
        if (prefab == null)
        {
            Debug.LogWarning("[HealthOrb] Resources/Prefabs/HealthOrb not configured yet - drop skipped.");
            return;
        }

        GameObject instance = Instantiate(prefab, position, Quaternion.identity);
        instance.GetComponent<NetworkObject>().Spawn();
    }

    // Two players can touch it within the same physics step - only the
    // first gets the heal, and Despawn() is only ever called once.
    private bool consumed;

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer || consumed) return;

        CharacterStats stats = other.GetComponentInParent<CharacterStats>();
        PlayerMovement player = other.GetComponentInParent<PlayerMovement>();
        if (stats == null || player == null) return;

        consumed = true;
        stats.RestoreHealthPercent(HealPercent);
        NetworkObject.Despawn();
    }
}
