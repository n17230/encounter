using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Ground-level VFX at a player's feet for as long as they have Aura of
// Replenishment (Aura_Arcane) or Aura of Regeneration (Aura_Light)
// slotted - both can be active on the same player at once. Reacts to
// CharacterEquipment's HasReplenishmentAura/HasRegenerationAura
// NetworkVariables (already synced to everyone), so every client sees
// this on whichever players actually have the aura, not just the owner.
[RequireComponent(typeof(CharacterEquipment))]
[RequireComponent(typeof(CharacterStats))]
[RequireComponent(typeof(CharacterController))]
public class AuraGroundVisual : NetworkBehaviour
{
    [SerializeField] private GameObject replenishmentVfxPrefab; // Aura_Arcane
    [SerializeField] private GameObject regenerationVfxPrefab;  // Aura_Light
    [SerializeField] private float groundOffset = 0.05f;
    // Applied once at instantiation, not animated - unlike the old
    // scale-pulse approach, a static scale set before any particles spawn
    // is safe (1x = the imported prefab's own natural size).
    [SerializeField] private float sizeMultiplier = 0.67f;

    // Pulse timing: a repeating 6-tick cycle (tick = CharacterEquipment's
    // own AuraPulseInterval, 1s) where each connected player's pulse
    // starts on a different tick, staggered by their rank among connected
    // players (1-6, by OwnerClientId - i.e. join order), so multiple
    // players' pulses don't all flash in sync. Within its own 3-tick
    // window the pulse fades in, peaks at the middle tick, and fades back
    // out (a half sine curve), then stays hidden for the remaining 3
    // ticks of the cycle. Implemented as a per-renderer material alpha
    // fade, NOT a transform scale pulse - a particle's size is baked in
    // at the moment it's emitted, so scaling the transform after the
    // fact silently fails to fade particles that spawned near scale 0
    // (confirmed: a plain unmodified instance renders fully in-scene,
    // so animating scale was the actual bug, not the shader/material).
    private const float TickSeconds = 1f;
    private const int CycleTicks = 6;
    private const float VisibleTicks = 3f;

    private CharacterEquipment equipment;
    private CharacterStats stats;
    private CharacterController controller;
    private readonly AuraInstance replenishment = new AuraInstance();
    private readonly AuraInstance regeneration = new AuraInstance();

    private int cachedPlayerNumber = 1;
    private float nextRankRefresh;

    // One live VFX instance plus the original (pre-fade) alpha of every
    // renderer under it, captured once from the shared material so each
    // frame's fade can be computed as a fraction of the real design
    // alpha instead of clobbering it to 1.
    private class AuraInstance
    {
        public GameObject Instance;
        public readonly List<ParticleSystemRenderer> Renderers = new List<ParticleSystemRenderer>();
        public readonly List<float> BaseAlpha = new List<float>();
    }

    private void Awake()
    {
        equipment = GetComponent<CharacterEquipment>();
        stats = GetComponent<CharacterStats>();
        controller = GetComponent<CharacterController>();
    }

    // The character's own feet world Y - transform.position sits at the
    // CharacterController's center, not the ground, so this has to
    // account for its center/height the same way PlayerMovement
    // .ValidateReplicatedMovement computes feetY.
    private float FeetY => transform.position.y + controller.center.y - controller.height * 0.5f;

    public override void OnNetworkDespawn()
    {
        if (replenishment.Instance != null) Destroy(replenishment.Instance);
        if (regeneration.Instance != null) Destroy(regeneration.Instance);
    }

    private void Update()
    {
        if (!IsClient) return;

        if (Time.time >= nextRankRefresh)
        {
            nextRankRefresh = Time.time + TickSeconds;
            cachedPlayerNumber = ComputePlayerNumber();
        }

        float pulseAlpha = ComputePulseAlpha(cachedPlayerNumber);
        UpdateAura(replenishment, replenishmentVfxPrefab, equipment.HasReplenishmentAura.Value, pulseAlpha);
        UpdateAura(regeneration, regenerationVfxPrefab, equipment.HasRegenerationAura.Value, pulseAlpha);
    }

    private void UpdateAura(AuraInstance aura, GameObject prefab, bool active, float pulseAlpha)
    {
        if (!active || prefab == null)
        {
            if (aura.Instance != null)
            {
                Destroy(aura.Instance);
                aura.Instance = null;
                aura.Renderers.Clear();
                aura.BaseAlpha.Clear();
            }
            return;
        }

        if (aura.Instance == null)
        {
            aura.Instance = Instantiate(prefab);
            aura.Instance.transform.localScale = Vector3.one * sizeMultiplier;
            aura.Renderers.Clear();
            aura.BaseAlpha.Clear();
            foreach (ParticleSystemRenderer renderer in aura.Instance.GetComponentsInChildren<ParticleSystemRenderer>())
            {
                aura.Renderers.Add(renderer);
                aura.BaseAlpha.Add(renderer.sharedMaterial != null ? renderer.sharedMaterial.color.a : 1f);
            }
        }

        Vector3 position = transform.position;
        aura.Instance.transform.position = new Vector3(position.x, FeetY + groundOffset, position.z);

        for (int i = 0; i < aura.Renderers.Count; i++)
        {
            ParticleSystemRenderer renderer = aura.Renderers[i];
            if (renderer == null) continue;
            Color color = renderer.material.color;
            color.a = aura.BaseAlpha[i] * pulseAlpha;
            renderer.material.color = color;
        }
    }

    // 0 at the start/end of the visible window, 1 at its exact middle
    // (tick 2), 0 for the rest of the 6-tick cycle.
    private static float ComputePulseAlpha(int playerNumber)
    {
        double now = NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;
        double offset = (playerNumber - 1) * TickSeconds;
        double cycleLength = CycleTicks * TickSeconds;
        double cyclePos = ((now - offset) % cycleLength + cycleLength) % cycleLength;

        double visibleWindow = VisibleTicks * TickSeconds;
        if (cyclePos >= visibleWindow) return 0f;

        float t = (float)(cyclePos / visibleWindow); // 0..1 across the visible window
        return Mathf.Sin(t * Mathf.PI); // 0 -> 1 (middle) -> 0
    }

    // This player's rank (1-based) among all currently connected players,
    // sorted by OwnerClientId - the same "join order" identity
    // PartyFrames.PartyNumber already uses, computed the same
    // scan-the-scene way (NetworkManager.ConnectedClientsList is
    // server-only, so this can't just read that directly on a client).
    private int ComputePlayerNumber()
    {
        List<ulong> ids = new List<ulong>();
        foreach (CharacterStats candidate in FindObjectsByType<CharacterStats>(FindObjectsSortMode.None))
        {
            if (candidate.GetComponent<PlayerMovement>() == null) continue; // players only
            ids.Add(candidate.OwnerClientId);
        }
        ids.Sort();

        int rank = ids.IndexOf(stats.OwnerClientId) + 1;
        return rank > 0 ? rank : 1;
    }
}
