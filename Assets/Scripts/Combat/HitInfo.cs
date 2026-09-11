// Everything one hostile interaction can carry, so every source of harm
// (projectile impact, instant cast, melee swing, ground patch, DoT tick)
// goes through the single CharacterStats.ReceiveHit entry point.
public struct HitInfo
{
    public float Damage;
    public float ExtraThreat;
    public ulong AttackerClientId;
    public StatusEffectData Effect;
    // 0 or less = use Effect.Duration.
    public float EffectDuration;

    public static HitInfo None => new HitInfo { AttackerClientId = CharacterStats.NoAttacker };
}
