using UnityEngine;

// Shared by every place that needs to resize an imported multi-layer
// particle VFX at instantiation time (AbilityData.CastVfxScale/
// TargetVfxScale, EffectOverheadVisual.Mapping.Scale). These VFX packs
// use Particle System Scaling Mode: Local (deliberately, in at least one
// case - see PlayerAbilities - since a shared prefab is also nested
// inside a differently-scaled parent elsewhere, and Local mode is what
// lets that nested use ignore the parent's scale and stay full-size).
// Scaling only the instance's own root transform does nothing under
// Local mode; this reaches every particle system's own scale instead,
// which Local mode does respect.
public static class VfxScale
{
    public static void Apply(GameObject instance, float scale)
    {
        if (scale == 1f) return;
        foreach (ParticleSystem ps in instance.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.transform.localScale *= scale;
        }
    }

    // For a VFX instance that's meant to stay ON a moving character (an
    // aura at their feet, a buff above their head): these packs author
    // their systems in World simulation space, where a particle stays
    // wherever it was emitted - so moving the instance only moves where
    // NEW particles appear, and a long-lived one (an aura's ground ring)
    // is simply left behind at the spot the effect started. Local space
    // makes every particle ride along with the instance instead. Same
    // per-instance approach as Apply - never touches the shared prefab.
    public static void FollowInstance(GameObject instance)
    {
        foreach (ParticleSystem ps in instance.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
        }
    }
}
