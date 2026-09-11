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

    private readonly Dictionary<StatusEffectData, ActiveEffect> active = new Dictionary<StatusEffectData, ActiveEffect>();
    private readonly List<ActiveEffect> tickScratch = new List<ActiveEffect>();
    private readonly List<ActiveEffect> expiredScratch = new List<ActiveEffect>();

    public event Action<ActiveEffect> Applied;
    public event Action<ActiveEffect> Refreshed;
    public event Action<ActiveEffect> Expired;

    public int Count => active.Count;
    public IEnumerable<ActiveEffect> All => active.Values;
    public bool TryGet(StatusEffectData data, out ActiveEffect effect) => active.TryGetValue(data, out effect);

    // Reapplying an active effect only ever extends it: a shorter/weaker
    // reapplication (e.g. a patch tick landing on someone already hit by
    // the bolt) never cuts the existing one short. The tick schedule is
    // never touched on refresh, so a tick that was due still fires.
    public void Apply(StatusEffectData data, float duration, ulong attackerClientId, float now)
    {
        if (data == null) return;

        float expireTime = now + duration;
        if (active.TryGetValue(data, out ActiveEffect existing))
        {
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
        active[data] = effect;
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
            while (now >= effect.NextTickTime && active.ContainsKey(effect.Data))
            {
                effect.NextTickTime += effect.Data.TickInterval;
                onTick?.Invoke(effect);
            }
        }
        tickScratch.Clear();

        foreach (ActiveEffect effect in expiredScratch)
        {
            if (active.Remove(effect.Data)) Expired?.Invoke(effect);
        }
        expiredScratch.Clear();
    }

    // Ends one specific effect early (e.g. One For All moving to a new
    // target) rather than waiting for it to expire on its own. No-op if
    // it isn't active - safe to call on a stale reference.
    public bool Remove(StatusEffectData data)
    {
        if (data == null || !active.TryGetValue(data, out ActiveEffect effect)) return false;
        active.Remove(data);
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
