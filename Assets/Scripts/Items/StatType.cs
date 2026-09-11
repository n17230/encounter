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
    ThreatMultiplier
}
