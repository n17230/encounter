using Unity.Netcode;
using UnityEngine;

// Purely visual: shows arcaneShieldVfxPrefab as a child of this character
// for as long as CharacterStats.ShieldAmount is above 0. Named for Aegis of
// Arcane specifically, the only ability that currently grants a shield -
// if a second, differently-themed shield source is ever added, this would
// need to distinguish which one granted the current ShieldAmount (not
// tracked today) rather than showing the Arcane visual for any of them.
// ShieldAmount is already a NetworkVariable synced to everyone; this polls
// its .Value every frame (the same proven approach PlayerHUD already uses
// to read synced NetworkVariables) rather than subscribing to
// OnValueChanged, which isn't otherwise used/proven anywhere in this
// codebase. Skipped entirely on a headless dedicated server, which has
// nothing to render it for.
[RequireComponent(typeof(CharacterStats))]
public class ArcaneShieldVisual : NetworkBehaviour
{
    [SerializeField] private GameObject arcaneShieldVfxPrefab;
    // Parented directly to the player, so this is a LOCAL offset from the
    // CharacterController's center (roughly chest-height at 0) - negative
    // moves it down the body. Live-tuned in the Editor across several
    // rounds of feedback.
    [SerializeField] private float verticalOffset = -0.1f;

    private CharacterStats stats;
    private GameObject activeVfx;

    private void Awake()
    {
        stats = GetComponent<CharacterStats>();
    }

    public override void OnNetworkDespawn()
    {
        if (activeVfx != null) Destroy(activeVfx);
    }

    private void Update()
    {
        if (!IsClient || arcaneShieldVfxPrefab == null) return;

        bool shouldShow = stats.ShieldAmount.Value > 0f;
        if (shouldShow == (activeVfx != null)) return;

        if (shouldShow)
        {
            activeVfx = Instantiate(arcaneShieldVfxPrefab, transform);
            activeVfx.transform.localPosition = Vector3.up * verticalOffset;
        }
        else
        {
            Destroy(activeVfx);
            activeVfx = null;
        }
    }
}
