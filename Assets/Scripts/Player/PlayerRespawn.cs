using Unity.Netcode;
using UnityEngine;

// Simple respawn for the open testing lobby only. Real instanced boss/mob
// content uses the wipe/reset model from DESIGN_IDEAS.md, not this.
// The server decides when a respawn happens and restores stats; the owner
// performs the actual teleport because it holds transform authority.
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(CharacterStats))]
[RequireComponent(typeof(PlayerMovement))]
public class PlayerRespawn : NetworkBehaviour
{
    [SerializeField] private Vector3 respawnPoint = new Vector3(0f, 1f, 0f);

    private CharacterController controller;
    private CharacterStats stats;
    private PlayerMovement movement;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        stats = GetComponent<CharacterStats>();
        movement = GetComponent<PlayerMovement>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer) stats.OnDeath += HandleDeath;

        // The prefab's baked spawn position only has the right X/Z - its Y
        // assumed the old flat floor. Snap to the terrain's current height.
        if (IsOwner) movement.TeleportTo(OnTerrain(transform.position));
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer) stats.OnDeath -= HandleDeath;
    }

    private void HandleDeath()
    {
        stats.RestoreFull();
        RespawnClientRpc();
    }

    [ClientRpc]
    private void RespawnClientRpc()
    {
        if (!IsOwner) return;
        movement.TeleportTo(OnTerrain(respawnPoint));
    }

    // Terrain height changes as the world gets sculpted, so spawn/respawn
    // always samples the CURRENT terrain surface at the target X/Z rather
    // than trusting a hardcoded Y.
    private Vector3 OnTerrain(Vector3 targetXZ)
    {
        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null) return targetXZ;

        float groundY = terrain.SampleHeight(targetXZ) + terrain.transform.position.y;
        float pivotY = groundY - controller.center.y + controller.height * 0.5f;
        return new Vector3(targetXZ.x, pivotY, targetXZ.z);
    }
}
