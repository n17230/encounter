using System;

// A data-authored stat change: used by gear (permanent while equipped)
// and status effects (while active). Applied as a StatModifier whose
// source is the owning asset, so removal is by source.
[Serializable]
public struct StatBonus
{
    public StatType Stat;
    public StatModifierType ModifierType;
    public float Value;
}
