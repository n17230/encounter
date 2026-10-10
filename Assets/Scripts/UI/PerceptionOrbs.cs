using System.Collections.Generic;
using UnityEngine;

// Owner-local orbs above every mob's head, coloured by the mob's base
// TargetingMode - what the Perception aura (AbilityData.AuraShowsPerceptionOrbs)
// shows. Plain C# owned by PlayerHUD (the one already-!IsOwner-gated per-frame
// hook, so this never runs on a dedicated server or for another player's
// HUD), not a MonoBehaviour and not a NetworkObject: each orb is a local
// Instantiate of a Resources prefab, same approach ManaOrb's load uses, and
// other clients see nothing. Prefabs are used exactly as authored - nothing
// on the instance but position and scale is touched.
public class PerceptionOrbs
{
    // Authored sphere prefab is a 1-unit sphere; this is the orb's diameter.
    public const float OrbScale = 0.6f;
    // How far the orb's bottom edge floats above the capsule top, so it
    // never clips into the mob. The centre height is derived from it so the
    // two can't drift apart if either is retuned.
    public const float BottomClearance = 0.3f;
    public const float OrbCenterHeight = BottomClearance + OrbScale * 0.5f;
    private const string PrefabFolder = "Prefabs/Indicators/";

    private readonly Dictionary<EnemyAI, GameObject> orbs = new Dictionary<EnemyAI, GameObject>();
    // Loaded lazily once per mode; a null entry means "tried, missing" so a
    // missing prefab logs once rather than every frame.
    private readonly Dictionary<TargetingMode, GameObject> prefabs = new Dictionary<TargetingMode, GameObject>();
    // Scratch for the sweep - a Dictionary can't be mutated mid-iteration.
    private readonly List<EnemyAI> gone = new List<EnemyAI>();

    // Prefab file name per mode. Two don't match the enum name: the authored
    // prefabs are called FurthestPlayer and ClosestProximity.
    public static string PrefabNameFor(TargetingMode mode)
    {
        switch (mode)
        {
            case TargetingMode.Proximity: return "ClosestProximity";
            case TargetingMode.HighestThreat: return "HighestThreat";
            case TargetingMode.LowestThreat: return "LowestThreat";
            case TargetingMode.FarthestPlayer: return "FurthestPlayer";
            case TargetingMode.MostHealing: return "MostHealing";
            default: return mode.ToString();
        }
    }

    public static Vector3 OrbCenter(Vector3 headPosition)
    {
        return headPosition + Vector3.up * OrbCenterHeight;
    }

    // Call every frame from the owner's Update. Inactive = no orbs at all.
    public void Update(bool active)
    {
        if (!active)
        {
            Clear();
            return;
        }

        foreach (EnemyAI mob in EnemyAI.All)
        {
            if (mob == null) continue;
            if (!orbs.TryGetValue(mob, out GameObject orb))
            {
                orb = CreateOrb(mob.BaseTargetingMode);
                orbs[mob] = orb;
            }
            // Null here = the prefab for this mode is missing; keep the
            // entry so the load isn't retried per frame.
            if (orb != null) orb.transform.position = OrbCenter(mob.HeadPosition());
        }

        // A mob stays in EnemyAI.All through its death until it despawns
        // (OnDisable), so an orb lingers over a corpse until then - and a
        // despawned/destroyed mob is removed here.
        gone.Clear();
        foreach (KeyValuePair<EnemyAI, GameObject> entry in orbs)
        {
            if (entry.Key == null || !entry.Key.isActiveAndEnabled) gone.Add(entry.Key);
        }
        foreach (EnemyAI mob in gone)
        {
            if (orbs.TryGetValue(mob, out GameObject orb) && orb != null) Object.Destroy(orb);
            orbs.Remove(mob);
        }
    }

    public void Clear()
    {
        foreach (GameObject orb in orbs.Values)
        {
            if (orb != null) Object.Destroy(orb);
        }
        orbs.Clear();
    }

    private GameObject CreateOrb(TargetingMode mode)
    {
        if (!prefabs.TryGetValue(mode, out GameObject prefab))
        {
            prefab = Resources.Load<GameObject>(PrefabFolder + PrefabNameFor(mode));
            if (prefab == null) Debug.LogWarning($"[PerceptionOrbs] Resources/{PrefabFolder}{PrefabNameFor(mode)} not found - no orb for {mode} mobs.");
            prefabs[mode] = prefab;
        }
        if (prefab == null) return null;

        // Unparented: the mob's root may be scaled (2x Ogre), which would
        // scale a child orb with it; positioned per frame instead.
        GameObject instance = Object.Instantiate(prefab);
        instance.transform.localScale = Vector3.one * OrbScale;
        return instance;
    }
}
