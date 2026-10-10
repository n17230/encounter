// Cast pushback: how much a cast-time cast is delayed by each damaging hit
// the caster takes while casting. One instance per cast (see
// PlayerAbilities.ServerCast) - a fresh instance IS the per-cast reset, so
// there's no Begin/Reset path to forget. Plain C#, no Unity references, so
// the per-hit sequence is unit-testable (Assets/Tests/EditMode).
//
// The delay shrinks with each hit of the same cast: 40%, then 20%, then 10%
// of the cast's total cast time, then nothing from the 4th hit on. What
// counts as a hit is decided by the caller (CharacterStats.DamageTaken -
// only a hit that actually lowered the caster's own health).
public sealed class CastPushbackTracker
{
    // Fraction of the cast time added by the 1st, 2nd and 3rd hit. Any hit
    // past the end of this table adds nothing. The one place these numbers
    // live.
    private static readonly float[] Fractions = { 0.4f, 0.2f, 0.1f };

    private readonly float castTime;

    // How many hits have been registered against this cast, including ones
    // that added no delay.
    public int Hits { get; private set; }

    public CastPushbackTracker(float castTime)
    {
        this.castTime = castTime;
    }

    // Registers one hit and returns the delay (seconds) it adds to the cast.
    // A cast with no cast time can't be pushed back, but the hit is still
    // counted.
    public float RegisterHit()
    {
        int index = Hits;
        Hits++;

        if (castTime <= 0f || index >= Fractions.Length) return 0f;
        return Fractions[index] * castTime;
    }
}
