using Unity.Netcode;
using UnityEngine;

// A mob death drop: any player who touches it gets a flat mana refund and
// it's consumed. Spawned by EnemyAI.HandleDeath via Resources.Load (see
// TrySpawn) rather than a per-mob-prefab field, so every mob prefab (present
// and future) behaves the same with no Inspector wiring.
[RequireComponent(typeof(SphereCollider))]
public class ManaOrb : NetworkBehaviour
{
    private const float ManaAmount = 250f;

    // Loaded once per drop rather than cached, since Resources.Load is cheap
    // and this keeps TrySpawn a single self-contained entry point.
    public static void TrySpawn(Vector3 position)
    {
        GameObject prefab = Resources.Load<GameObject>("Prefabs/ManaOrb");
        if (prefab == null)
        {
            Debug.LogWarning("[ManaOrb] Resources/Prefabs/ManaOrb not configured yet - drop skipped.");
            return;
        }

        GameObject instance = Instantiate(prefab, position, Quaternion.identity);
        instance.GetComponent<NetworkObject>().Spawn();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        CharacterStats stats = other.GetComponentInParent<CharacterStats>();
        PlayerMovement player = other.GetComponentInParent<PlayerMovement>();
        if (stats == null || player == null) return;

        stats.RestoreMana(ManaAmount);
        NetworkObject.Despawn();
    }
}
