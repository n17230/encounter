using System.Collections.Generic;
using UnityEngine;

// How reapplying this effect (from any caster) interacts with an
// already-active instance of it - see StatusEffectTracker for the actual
// rules:
//  - RefreshExtendOnly (default): one shared instance regardless of who
//    applied it; a reapplication only ever extends the remaining
//    duration, never shortens it (a weaker/shorter reapplication never
//    dilutes a stronger one already running).
//  - Override: also one shared instance regardless of caster, but a
//    reapplication always wins outright - for buffs that represent a
//    single exclusive bond, where only one caster's version should ever
//    be active on a given player at a time (e.g. One For All).
//  - StackPerCaster: each caster gets their own independent instance,
//    ticking/expiring separately - for effects that SHOULD stack when
//    multiple different players apply them (e.g. two healers each
//    running their own heal-over-time on the same target).
//  - StackUpToLimit: one shared instance (like RefreshExtendOnly), but
//    each reapplication up to MaxStacks also adds another copy of
//    Modifiers, so the magnitude grows with stack count instead of
//    just the duration resetting - e.g. an on-hit armor-reduction debuff
//    that gets worse the more you're hit, capped at some number of
//    stacks. The whole stack shares one timer (reapplying always resets
//    it), not per-stack independent timers.
public enum EffectStackingMode
{
    RefreshExtendOnly,
    Override,
    StackPerCaster,
    StackUpToLimit,
}

// A buff/debuff authored as data. Keyed by asset at runtime: reapplying
// the same effect refreshes/stacks/overrides it per StackingMode below;
// different effects always stack independently even if they touch the
// same stat.
[CreateAssetMenu(menuName = "Encounter/Status Effect", fileName = "NewStatusEffect")]
public class StatusEffectData : ScriptableObject
{
    // Stable identity used over the network (synced effect list) and in
    // saved data. Never change once content ships.
    public string Id;
    public string DisplayName = "New Effect";
    public float Duration = 3f;
    public EffectStackingMode StackingMode = EffectStackingMode.RefreshExtendOnly;

    // Periodic damage and/or periodic healing; 0 = none, both can be set.
    // Ticks on its own schedule from first application - a refresh extends
    // the effect but never resets ticks.
    public float TickDamage = 0f;
    public float TickHeal = 0f;
    public float TickInterval = 1f;

    // Stat modifiers held for as long as the effect is active - see
    // StackUpToLimit above, one copy of these is added per stack.
    public List<StatBonus> Modifiers = new List<StatBonus>();

    // Only meaningful when StackingMode is StackUpToLimit - the most
    // copies of Modifiers that can be active at once. 1 (the default) is
    // meaningless for any other mode.
    public int MaxStacks = 1;

    // If > 0, while this effect is active this fraction of damage the
    // wearer takes is dealt directly to whoever applied the effect
    // instead (see CharacterStats.DealDamage) - e.g. One For All. 0 (the
    // default) means no redirect.
    public float DamageRedirectPercent = 0f;

    // While active, the holder can't move, cast, or auto-attack - see
    // CharacterStats.IsStunned and its consumers (currently just EnemyAI;
    // no ability stuns a player yet).
    public bool IsStun = false;

    // A harmful effect, eligible to be stripped by a dispel ability (e.g.
    // Cleanse) - see CharacterStats.RemoveOneNegativeEffect. False (the
    // default) for buffs/auras, which a dispel should never touch.
    public bool IsNegative = false;
}
