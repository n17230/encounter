// Which CharacterStats stat a StatBonus applies to.
public enum StatType
{
    MaxHealth,
    HealthRegenRate,
    MaxMana,
    ManaRegenRate,
    RunSpeed,
    Armor,
    // Multiplier on every ability's mana cost (base 1.0; -10% = PercentAdditive -0.1).
    ManaCostMultiplier,
    // Multiplier on all threat this character generates (base 1.0).
    ThreatMultiplier,
    // Multiplier on all damage this character deals (base 1.0).
    DamageMultiplier,
    // Multiplier on all healing this character deals, instant or over
    // time (base 1.0).
    HealingMultiplier,
    // Multiplier on all damage this character TAKES, applied after armor
    // mitigation (base 1.0; e.g. Team Up, Barbarian's Mantle).
    DamageTakenMultiplier,
    // Flat bonus added to this character's current weapon's Damage (base
    // 0) - read wherever weapon damage is read: PlayerAutoAttack's basic
    // swing and PlayerAbilities.ResolveWeaponDamage (weapon-scaling
    // abilities like Reaper's Wheel, Cleave, Crippling Blow, Seismic Slam).
    WeaponDamageBonus
}
