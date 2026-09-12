public enum HitSource { Unknown, Melee, Ability, GroundPatch, Aura }

// Everything one hostile interaction can carry, so every source of harm
// (projectile impact, instant cast, melee swing, ground patch) goes
// through the single CharacterStats.ReceiveHit entry point.
public struct HitInfo
{
    public float Damage;
    public float Heal;
    // Replaces any existing shield outright rather than adding to it -
    // see CharacterStats.GrantShield.
    public float ShieldAmount;
    public float ExtraThreat;
    public ulong AttackerClientId;
    // Optional direct reference to the attacker's own CharacterStats, for
    // callers that have no clientId at all (mobs - AttackerClientId is
    // NoAttacker for them). Currently only used so reflect damage
    // (StatType.DamageReflectPercent) has somewhere to land when a mob is
    // the attacker; everything else still resolves the attacker via
    // AttackerClientId as before, which is sufficient for players.
    public CharacterStats Attacker;
    public HitSource Source;
    public StatusEffectData Effect;
    // 0 or less = use Effect.Duration.
    public float EffectDuration;

    public static HitInfo None => new HitInfo { AttackerClientId = CharacterStats.NoAttacker };
}
