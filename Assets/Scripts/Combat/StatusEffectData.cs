using System.Collections.Generic;
using UnityEngine;

// A buff/debuff authored as data. Keyed by asset at runtime: reapplying
// the same effect refreshes it (see StatusEffectTracker), different
// effects stack independently even if they touch the same stat.
[CreateAssetMenu(menuName = "Encounter/Status Effect", fileName = "NewStatusEffect")]
public class StatusEffectData : ScriptableObject
{
    // Stable identity used over the network (synced effect list) and in
    // saved data. Never change once content ships.
    public string Id;
    public string DisplayName = "New Effect";
    public float Duration = 3f;

    // Periodic damage and/or periodic healing; 0 = none, both can be set.
    // Ticks on its own schedule from first application - a refresh extends
    // the effect but never resets ticks.
    public float TickDamage = 0f;
    public float TickHeal = 0f;
    public float TickInterval = 1f;

    // Stat modifiers held for as long as the effect is active.
    public List<StatBonus> Modifiers = new List<StatBonus>();

    // If > 0, while this effect is active this fraction of damage the
    // wearer takes is dealt directly to whoever applied the effect
    // instead (see CharacterStats.DealDamage) - e.g. One For All. 0 (the
    // default) means no redirect.
    public float DamageRedirectPercent = 0f;
}
