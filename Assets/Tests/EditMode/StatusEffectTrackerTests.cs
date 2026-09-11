using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class StatusEffectTrackerTests
{
    private readonly List<StatusEffectData> created = new List<StatusEffectData>();

    private StatusEffectData Effect(string id, float duration = 3f, float tickDamage = 0f, float tickInterval = 1f)
    {
        StatusEffectData effect = ScriptableObject.CreateInstance<StatusEffectData>();
        effect.Id = id;
        effect.Duration = duration;
        effect.TickDamage = tickDamage;
        effect.TickInterval = tickInterval;
        created.Add(effect);
        return effect;
    }

    [TearDown]
    public void DestroyEffects()
    {
        foreach (StatusEffectData effect in created) Object.DestroyImmediate(effect);
        created.Clear();
    }

    [Test]
    public void ApplyNewEffectRaisesApplied()
    {
        StatusEffectTracker tracker = new StatusEffectTracker();
        StatusEffectTracker.ActiveEffect applied = null;
        tracker.Applied += e => applied = e;

        StatusEffectData burn = Effect("burn");
        tracker.Apply(burn, 3f, 7, now: 10f);

        Assert.AreSame(burn, applied.Data);
        Assert.AreEqual(13f, applied.ExpireTime);
        Assert.AreEqual(7UL, applied.AttackerClientId);
        Assert.AreEqual(1, tracker.Count);
    }

    [Test]
    public void ShorterReapplicationNeverShortens()
    {
        StatusEffectTracker tracker = new StatusEffectTracker();
        int refreshes = 0;
        tracker.Refreshed += _ => refreshes++;

        StatusEffectData slow = Effect("slow");
        tracker.Apply(slow, 5f, 1, now: 0f);
        tracker.Apply(slow, 3f, 2, now: 1f); // would expire at 4, existing expires at 5

        tracker.TryGet(slow, out StatusEffectTracker.ActiveEffect active);
        Assert.AreEqual(5f, active.ExpireTime);
        Assert.AreEqual(1UL, active.AttackerClientId);
        Assert.AreEqual(0, refreshes);
    }

    [Test]
    public void LongerReapplicationExtendsAndReattributes()
    {
        StatusEffectTracker tracker = new StatusEffectTracker();
        int refreshes = 0;
        tracker.Refreshed += _ => refreshes++;

        StatusEffectData slow = Effect("slow");
        tracker.Apply(slow, 3f, 1, now: 0f);
        tracker.Apply(slow, 3f, 2, now: 2f);

        tracker.TryGet(slow, out StatusEffectTracker.ActiveEffect active);
        Assert.AreEqual(5f, active.ExpireTime);
        Assert.AreEqual(2UL, active.AttackerClientId);
        Assert.AreEqual(1, refreshes);
        Assert.AreEqual(1, tracker.Count);
    }

    [Test]
    public void RefreshDoesNotResetTickSchedule()
    {
        StatusEffectTracker tracker = new StatusEffectTracker();
        StatusEffectData burn = Effect("burn", duration: 3f, tickDamage: 5f, tickInterval: 1f);
        int ticks = 0;

        tracker.Apply(burn, 3f, 1, now: 0f);
        tracker.Tick(0f, _ => ticks++);
        Assert.AreEqual(1, ticks);

        tracker.Apply(burn, 3f, 1, now: 0.9f); // refresh just before a tick is due
        tracker.Tick(0.95f, _ => ticks++);
        Assert.AreEqual(1, ticks);
        tracker.Tick(1.0f, _ => ticks++);
        Assert.AreEqual(2, ticks);
    }

    [Test]
    public void TickCatchesUpMissedIntervals()
    {
        StatusEffectTracker tracker = new StatusEffectTracker();
        StatusEffectData burn = Effect("burn", duration: 10f, tickDamage: 5f, tickInterval: 1f);
        int ticks = 0;

        tracker.Apply(burn, 10f, 1, now: 0f);
        tracker.Tick(2.5f, _ => ticks++);

        Assert.AreEqual(3, ticks); // due at 0, 1, 2
    }

    [Test]
    public void EffectExpiresAndRaisesExpired()
    {
        StatusEffectTracker tracker = new StatusEffectTracker();
        StatusEffectTracker.ActiveEffect expired = null;
        tracker.Expired += e => expired = e;

        StatusEffectData slow = Effect("slow");
        tracker.Apply(slow, 3f, 1, now: 0f);
        tracker.Tick(2.99f, null);
        Assert.IsNull(expired);
        tracker.Tick(3f, null);

        Assert.AreSame(slow, expired.Data);
        Assert.AreEqual(0, tracker.Count);
    }

    [Test]
    public void ClearingDuringTickCallbackIsSafe()
    {
        StatusEffectTracker tracker = new StatusEffectTracker();
        StatusEffectData burn = Effect("burn", duration: 10f, tickDamage: 5f, tickInterval: 1f);
        StatusEffectData bleed = Effect("bleed", duration: 10f, tickDamage: 5f, tickInterval: 1f);
        int ticks = 0;

        tracker.Apply(burn, 10f, 1, now: 0f);
        tracker.Apply(bleed, 10f, 1, now: 0f);

        // Simulates a tick killing the target, whose death handler wipes
        // every effect mid-pass.
        Assert.DoesNotThrow(() => tracker.Tick(3f, _ =>
        {
            ticks++;
            tracker.ClearAll();
        }));

        Assert.AreEqual(1, ticks);
        Assert.AreEqual(0, tracker.Count);
    }

    [Test]
    public void ClearAllRaisesExpiredForEveryEffect()
    {
        StatusEffectTracker tracker = new StatusEffectTracker();
        int expired = 0;
        tracker.Expired += _ => expired++;

        tracker.Apply(Effect("a"), 3f, 1, now: 0f);
        tracker.Apply(Effect("b"), 3f, 1, now: 0f);
        tracker.ClearAll();

        Assert.AreEqual(2, expired);
        Assert.AreEqual(0, tracker.Count);
    }
}
