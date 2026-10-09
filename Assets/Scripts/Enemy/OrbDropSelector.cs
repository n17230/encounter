// A single roll decides at most one outcome: a roll below manaChance is
// Mana, otherwise a roll below manaChance + healthChance is Health, else
// None - so mana and health drops are mutually exclusive by construction,
// never both from the same roll. Pure + testable, same shape as
// MobPathing/TargetSelector.
public enum OrbDropResult
{
    None,
    Mana,
    Health
}

public static class OrbDropSelector
{
    public static OrbDropResult Select(float roll, float manaChance, float healthChance)
    {
        if (roll < manaChance) return OrbDropResult.Mana;
        if (roll < manaChance + healthChance) return OrbDropResult.Health;
        return OrbDropResult.None;
    }
}
