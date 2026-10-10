using Unity.Netcode;
using UnityEngine;

// Death and respawn for the open testing lobby only. Real instanced boss/mob
// content uses the wipe/reset model from DESIGN_IDEAS.md, not this.
// Dying leaves the player dead where they fell (HandleDeath); the two ways
// back are a Resurrect cast by someone else (PlayerAbilities ->
// CharacterStats.Resurrect, in place) and the Escape menu's Respawn button
// (RequestRespawn -> ServerRespawn, at the respawn point). The server
// restores stats; the owner performs the actual teleport because it holds
// transform authority.
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

    // Server-only, from CharacterStats.OnDeath. The body stays put at 0
    // health (no restore, no teleport) - only the fight's leftovers end:
    // effects/shield, the threat this player had built, any cast in
    // flight, and the redirect bonds this player was soaking damage
    // through for others. Mana and cooldowns (own and global) are left
    // as they were - a resurrected player picks their cooldowns back up
    // mid-count.
    private void HandleDeath()
    {
        stats.EnterDeadState();
        ResetThreatGenerated();
        abilities.CancelCast();
        CharacterStats.RemoveRedirectBondsFrom(OwnerClientId);
    }

    // Owner-callable (the Escape menu's Respawn button): asks the server
    // for a full respawn at the respawn point. Works dead or alive - a
    // living player gets the same outcome (full restore, threat wiped,
    // cooldowns reset, back at the respawn point).
    public void RequestRespawn()
    {
        if (!IsOwner) return;
        RequestRespawnServerRpc();
    }

    [ServerRpc]
    private void RequestRespawnServerRpc()
    {
        ServerRespawn();
    }

    private void ServerRespawn()
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

    // Wipes this player's own entry from every mob's ThreatTable (not a
    // full-table clear - other players' threat is untouched).
    private void ResetThreatGenerated()
    {
        foreach (ThreatTable table in ThreatTable.All)
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
