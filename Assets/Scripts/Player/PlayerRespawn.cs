using Unity.Netcode;
using UnityEngine;

// Simple respawn for the open testing lobby only. Real instanced boss/mob
// content uses the wipe/reset model from DESIGN_IDEAS.md, not this.
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(CharacterStats))]
public class PlayerRespawn : NetworkBehaviour
{
    [SerializeField] private Vector3 respawnPoint = new Vector3(0f, 1f, 0f);

    private CharacterController controller;
    private CharacterStats stats;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        stats = GetComponent<CharacterStats>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        stats.OnDeath += HandleDeath;

        // The prefab's own baked spawn position only has the right X/Z -
        // its Y assumed the old flat floor. Snap to whatever the terrain's
        // current height actually is at that spot instead.
        controller.enabled = false;
        SnapToTerrain(transform.position);
        controller.enabled = true;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer) stats.OnDeath -= HandleDeath;
    }

    private void HandleDeath()
    {
        controller.enabled = false;
        SnapToTerrain(respawnPoint);
        controller.enabled = true;
        stats.RestoreFull();
    }

    // Terrain height changes as the world gets sculpted, so spawn/respawn
    // always samples the CURRENT terrain surface at the target X/Z rather
    // than trusting a hardcoded Y - avoids spawning under or above ground
    // every time the terrain is reshaped near a spawn point.
    private void SnapToTerrain(Vector3 targetXZ)
    {
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null)
        {
            transform.position = targetXZ;
            return;
        }

        float groundY = terrain.SampleHeight(targetXZ) + terrain.transform.position.y;
        float pivotY = groundY - controller.center.y + controller.height * 0.5f;
        transform.position = new Vector3(targetXZ.x, pivotY, targetXZ.z);
        Debug.Log($"[PlayerRespawn] terrain={terrain.name} at target ({targetXZ.x},{targetXZ.z}): groundY={groundY} -> spawned at {transform.position}");
    }
}
