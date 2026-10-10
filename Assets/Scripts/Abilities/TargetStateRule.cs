// Whether a unit target is in the right life state for an ability - the one
// rule shared by all three places PlayerAbilities decides a cast
// (ClientPrecheck, CastAbilityServerRpc, ResolveAbility), so every site
// rejects with the same words. A corpse is a valid target for nothing but a
// ResurrectTarget ability, and that ability wants nothing but a dead player
// (never a mob - mobs despawn, they don't come back). Pure, no Unity
// dependencies, so it can be unit-tested.
public static class TargetStateRule
{
    // Returns null if the cast may proceed, otherwise the rejection notice
    // to show the caster. Checked in this order: a dead target for a normal
    // ability, then (Resurrect only) not-a-player before not-dead - so a
    // mob is "Can only resurrect players" whether it's alive or not.
    public static string Check(bool resurrectAbility, bool targetAlive, bool targetIsPlayer)
    {
        if (!resurrectAbility) return targetAlive ? null : "Target is dead";
        if (!targetIsPlayer) return "Can only resurrect players";
        if (targetAlive) return "Target is not dead";
        return null;
    }
}
