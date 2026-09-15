using Unity.Netcode;
using UnityEngine;

// Shows vfxPrefab above a character's head for as long as watchedEffect is
// one of their currently active status effects (per CharacterStats
// .ActiveEffects, already synced to everyone) - e.g. Vitality Ward +
// Buff_Light. Purely visual, reacts the same way on every client, not
// just the affected character's owner. Polls ActiveEffects every frame
// rather than subscribing to NetworkList.OnListChanged - the same
// proven approach PlayerHUD.DescribeEffects already uses for this exact
// list, instead of an event subscription pattern untested elsewhere in
// this codebase.
[RequireComponent(typeof(CharacterStats))]
public class EffectOverheadVisual : NetworkBehaviour
{
    [SerializeField] private StatusEffectData watchedEffect;
    [SerializeField] private GameObject vfxPrefab;
    [SerializeField] private float headHeight = 2.2f;

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
        if (!IsClient || watchedEffect == null || vfxPrefab == null) return;

        bool active = false;
        foreach (ActiveEffectNet entry in stats.ActiveEffects)
        {
            if (entry.EffectId.ToString() == watchedEffect.Id)
            {
                active = true;
                break;
            }
        }

        if (!active)
        {
            if (activeVfx != null)
            {
                Destroy(activeVfx);
                activeVfx = null;
            }
            return;
        }

        if (activeVfx == null) activeVfx = Instantiate(vfxPrefab);

        Vector3 position = transform.position;
        activeVfx.transform.position = new Vector3(position.x, position.y + headHeight, position.z);
    }
}
