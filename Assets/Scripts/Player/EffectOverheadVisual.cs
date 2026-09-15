using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Shows a VFX above a character's head for as long as a specific status
// effect is currently active on them (per CharacterStats.ActiveEffects,
// already synced to everyone) - one persistent instance per active effect,
// not re-triggered per tick. Supports several (effect, VFX, height) triples
// from one component so multiple buffs can each get their own overhead
// indicator at its own height - e.g. Vitality Ward + Buff_Light, Everliving
// Touch + Light Dots. Purely visual, reacts the same way on every client,
// not just the affected character's owner. Polls ActiveEffects every frame
// rather than
// subscribing to NetworkList.OnListChanged - the same proven approach
// PlayerHUD.DescribeEffects already uses for this exact list, instead of
// an event subscription pattern untested elsewhere in this codebase.
[RequireComponent(typeof(CharacterStats))]
public class EffectOverheadVisual : NetworkBehaviour
{
    [Serializable]
    private struct Mapping
    {
        public StatusEffectData Effect;
        public GameObject VfxPrefab;
        public float HeadHeight;
    }

    [SerializeField] private List<Mapping> mappings = new List<Mapping>();

    private CharacterStats stats;
    private readonly List<GameObject> activeVfx = new List<GameObject>();

    private void Awake()
    {
        stats = GetComponent<CharacterStats>();
        while (activeVfx.Count < mappings.Count) activeVfx.Add(null);
    }

    public override void OnNetworkDespawn()
    {
        foreach (GameObject vfx in activeVfx)
        {
            if (vfx != null) Destroy(vfx);
        }
    }

    private void Update()
    {
        if (!IsClient) return;

        for (int i = 0; i < mappings.Count; i++)
        {
            UpdateMapping(i);
        }
    }

    private void UpdateMapping(int index)
    {
        Mapping mapping = mappings[index];
        if (mapping.Effect == null || mapping.VfxPrefab == null) return;

        bool active = false;
        foreach (ActiveEffectNet entry in stats.ActiveEffects)
        {
            if (entry.EffectId.ToString() == mapping.Effect.Id)
            {
                active = true;
                break;
            }
        }

        if (!active)
        {
            if (activeVfx[index] != null)
            {
                Destroy(activeVfx[index]);
                activeVfx[index] = null;
            }
            return;
        }

        if (activeVfx[index] == null) activeVfx[index] = Instantiate(mapping.VfxPrefab);

        Vector3 position = transform.position;
        activeVfx[index].transform.position = new Vector3(position.x, position.y + mapping.HeadHeight, position.z);
    }
}
