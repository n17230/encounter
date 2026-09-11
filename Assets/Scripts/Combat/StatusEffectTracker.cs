using System;
using System.Collections.Generic;

// Server-side bookkeeping for the status effects on one character. Pure
// C# with time passed in, so the refresh/tick/expiry rules are testable
// without a scene or a network session.
public class StatusEffectTracker
{
    public class ActiveEffect
    {
        public StatusEffectData Data;
        public float ExpireTime;
        public float NextTickTime;
        public ulong AttackerClientId;
    }

    // What actually identifies "one active instance" in the dictionary.
    // For StackPerCaster effects the caster is part of the identity, so
    // different casters land in different slots; for every other mode the
    // caster is fixed at 0, so every application (regardless of who cast
    // it) collides on the same single slot - that's what makes
    // Override/RefreshExtendOnly "one shared instance" in the first place.
    private readonly struct Key : IEquatable<Key>
    {
        private readonly StatusEffectData data;
        private readonly ulong casterId;

        public Key(StatusEffectData data, ulong casterId)
        {
            this.data = data;
            this.casterId = casterId;
        }

        public bool Equals(Key other) => data == other.data && casterId == other.casterId;
        public override bool Equals(object obj) => obj is Key other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(data, casterId);
    }

    private readonly Dictionary<Key, ActiveEffect> active = new Dictionary<Key, ActiveEffect>();
    private readonly List<ActiveEffect> tickScratch = new List<ActiveEffect>();
    private readonly List<ActiveEffect> expiredScratch = new List<ActiveEffect>();

    public event Action<ActiveEffect> Applied;
    public event Action<ActiveEffect> Refreshed;
    public event Action<ActiveEffect> Expired;

    public int Count => active.Count;
    public IEnumerable<ActiveEffect> All => active.Values;

    // Only meaningful for non-StackPerCaster effects (there's exactly one
    // possible instance for those); a StackPerCaster effect may have
    // several simultaneous instances, so this returns whichever the
    // dictionary happens to find at the shared (caster 0) slot - which,
    // for a StackPerCaster effect, is generally none.
    public bool TryGet(StatusEffectData data, out ActiveEffect effect) => active.TryGetValue(SharedKey(data), out effect);

    private static Key SharedKey(StatusEffectData data) => new Key(data, 0);

    private static Key KeyOf(StatusEffectData data, ulong attackerClientId) =>
        data != null && data.StackingMode == EffectStackingMode.StackPerCaster ? new Key(data, attackerClientId) : SharedKey(data);

    private static Key KeyOf(ActiveEffect effect) => KeyOf(effect.Data, effect.AttackerClientId);

    // RefreshExtendOnly (default): a reapplication only ever extends the
    // remaining duration, never shortens it, and never touches the tick
    // schedule - a due tick still fires even if a refresh lands at the
    // same moment. Override: a reapplication always wins outright (new
    // duration, new caster attribution) regardless of what was left on
    // the old one - for buffs where only one caster's version can be
    // active at a time (e.g. One For All). StackPerCaster: each caster's
    // application is a wholly separate instance (see Key above), so two
    // different casters both have their own copy ticking independently;
    // a recast by the SAME caster still just extends their own instance.
    public void Apply(StatusEffectData data, float duration, ulong attackerClientId, float now)
    {
        if (data == null) return;

        Key key = KeyOf(data, attackerClientId);
        float expireTime = now + duration;

        if (active.TryGetValue(key, out ActiveEffect existing))
        {
            if (data.StackingMode == EffectStackingMode.Override)
            {
                existing.ExpireTime = expireTime;
                existing.AttackerClientId = attackerClientId;
                Refreshed?.Invoke(existing);
                return;
            }

            if (expireTime <= existing.ExpireTime) return;
            existing.ExpireTime = expireTime;
            existing.AttackerClientId = attackerClientId;
            Refreshed?.Invoke(existing);
            return;
        }

        ActiveEffect effect = new ActiveEffect
        {
            Data = data,
            ExpireTime = expireTime,
            NextTickTime = now,
            AttackerClientId = attackerClientId,
        };
        active[key] = effect;
        Applied?.Invoke(effect);
    }

    // Fires every due tick (catching up after a stall rather than dropping
    // ticks), then expires. Callbacks run outside the dictionary walk, and
    // an effect removed by a callback (a tick killing the target, say) is
    // skipped for the rest of the pass.
    public void Tick(float now, Action<ActiveEffect> onTick)
    {
        if (active.Count == 0) return;

        foreach (ActiveEffect effect in active.Values)
        {
            bool ticks = (effect.Data.TickDamage > 0f || effect.Data.TickHeal > 0f) && effect.Data.TickInterval > 0f;
            if (ticks && now >= effect.NextTickTime)
            {
                tickScratch.Add(effect);
            }
            if (now >= effect.ExpireTime) expiredScratch.Add(effect);
        }

        foreach (ActiveEffect effect in tickScratch)
        {
            Key key = KeyOf(effect);
            while (now >= effect.NextTickTime && active.ContainsKey(key))
            {
                effect.NextTickTime += effect.Data.TickInterval;
                onTick?.Invoke(effect);
            }
        }
        tickScratch.Clear();

        foreach (ActiveEffect effect in expiredScratch)
        {
            if (active.Remove(KeyOf(effect))) Expired?.Invoke(effect);
        }
        expiredScratch.Clear();
    }

    // Ends one specific effect early (e.g. an Override-mode buff moving to
    // a new target) rather than waiting for it to expire on its own.
    // No-op if it isn't active - safe to call on a stale reference. Only
    // finds the shared (non-StackPerCaster) instance; there's no single
    // "the" instance to remove for a StackPerCaster effect, since several
    // casters' copies could be active at once.
    public bool Remove(StatusEffectData data)
    {
        Key key = SharedKey(data);
        if (data == null || !active.TryGetValue(key, out ActiveEffect effect)) return false;
        active.Remove(key);
        Expired?.Invoke(effect);
        return true;
    }

    public void ClearAll()
    {
        if (active.Count == 0) return;
        expiredScratch.AddRange(active.Values);
        active.Clear();
        foreach (ActiveEffect effect in expiredScratch) Expired?.Invoke(effect);
        expiredScratch.Clear();
    }
}
