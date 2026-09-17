using Unity.Netcode;
using UnityEngine;

// Simple respawn for the open testing lobby only. Real instanced boss/mob
// content uses the wipe/reset model from DESIGN_IDEAS.md, not this.
// The server decides when a respawn happens and restores stats; the owner
// performs the actual teleport because it holds transform authority.
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(CharacterStats))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerAbilities))]
public class PlayerRespawn : NetworkBehaviour
{
    [SerializeField] private Vector3 respawnPoint = new Vector3(0f, 1f, 0f);

    private CharacterController controller;
    private CharacterStats stats;
    private PlayerMovement movement;
    private PlayerAbilities abilities;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        stats = GetComponent<CharacterStats>();
        movement = GetComponent<PlayerMovement>();
        abilities = GetComponent<PlayerAbilities>();
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
        ResetThreatGenerated();
        abilities.ResetCooldowns();
        // Server-initiated, so it can use ServerTeleportTo (same path Recall
        // uses) to pre-arm the movement validator before the owner's
        // teleport lands, rather than needing a grace period like the
        // owner-initiated spawn snap below.
        movement.ServerTeleportTo(OnTerrain(respawnPoint));
    }

    // Wipes this player's own entry from every mob's ThreatTable in the
    // scene (not a full-table clear - other players' threat is
    // untouched), same FindObjectsByType scan CharacterStats
    // .GenerateHealingThreat already uses for the same "every ThreatTable
    // in the scene" reasoning.
    private void ResetThreatGenerated()
    {
        foreach (ThreatTable table in FindObjectsByType<ThreatTable>(FindObjectsSortMode.None))
        {
            table.RemoveThreatFor(OwnerClientId);
        }
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
