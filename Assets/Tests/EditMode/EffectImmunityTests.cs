using NUnit.Framework;
using UnityEngine;

public class EffectImmunityTests
{
    private StatusEffectData slow;
    private StatusEffectData burn;

    [SetUp]
    public void CreateEffects()
    {
        slow = ScriptableObject.CreateInstance<StatusEffectData>();
        burn = ScriptableObject.CreateInstance<StatusEffectData>();
    }

    [TearDown]
    public void DestroyEffects()
    {
        Object.DestroyImmediate(slow);
        Object.DestroyImmediate(burn);
    }

    [Test]
    public void GroundOnlyImmunityBlocksPatchesButNotDirectHits()
    {
        EffectImmunity cleats = new EffectImmunity { Effect = slow, GroundOnly = true };

        Assert.IsTrue(cleats.Blocks(slow, HitSource.GroundPatch));
        Assert.IsFalse(cleats.Blocks(slow, HitSource.Ability));
        Assert.IsFalse(cleats.Blocks(slow, HitSource.Melee));
    }

    [Test]
    public void FullImmunityBlocksEverySource()
    {
        EffectImmunity ward = new EffectImmunity { Effect = burn, GroundOnly = false };

        Assert.IsTrue(ward.Blocks(burn, HitSource.GroundPatch));
        Assert.IsTrue(ward.Blocks(burn, HitSource.Ability));
        Assert.IsTrue(ward.Blocks(burn, HitSource.Melee));
    }

    [Test]
    public void OnlyTheNamedEffectIsBlocked()
    {
        EffectImmunity cleats = new EffectImmunity { Effect = slow, GroundOnly = true };

        Assert.IsFalse(cleats.Blocks(burn, HitSource.GroundPatch));
        Assert.IsFalse(new EffectImmunity().Blocks(slow, HitSource.GroundPatch));
    }
}
