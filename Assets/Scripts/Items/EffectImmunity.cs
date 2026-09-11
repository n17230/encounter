using System;

// "This gear makes you immune to that status effect" - optionally only
// when it would come from a ground patch (ice cleats: immune to being
// slowed by ice on the ground, but a bolt to the face still slows you).
[Serializable]
public struct EffectImmunity
{
    public StatusEffectData Effect;
    public bool GroundOnly;

    public bool Blocks(StatusEffectData effect, HitSource source)
    {
        if (Effect == null || effect != Effect) return false;
        return !GroundOnly || source == HitSource.GroundPatch;
    }
}
