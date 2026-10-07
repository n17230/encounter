using NUnit.Framework;
using UnityEngine;

public class WeaponDataTests
{
    private WeaponData weapon;

    [SetUp]
    public void SetUp() => weapon = ScriptableObject.CreateInstance<WeaponData>();

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(weapon);

    // A weapon sitting exactly at melee reach is still melee - only
    // something that reaches PAST it is ranged.
    [Test]
    public void MeleeReachIsNotRanged()
    {
        weapon.Range = WeaponData.BasicAttackRange;
        Assert.IsFalse(weapon.IsRanged);
        Assert.AreEqual(HitSource.Melee, weapon.BasicAttackSource);
    }

    [Test]
    public void JustPastMeleeReachIsAlreadyRanged()
    {
        weapon.Range = WeaponData.BasicAttackRange + 0.01f;
        Assert.IsTrue(weapon.IsRanged);
    }

    // "Chance to block melee attacks" must not block arrows: a ranged
    // weapon's basic attack lands as Ranged, never Melee.
    [Test]
    public void AnythingPastMeleeReachIsRanged()
    {
        weapon.Range = 50f;
        Assert.IsTrue(weapon.IsRanged);
        Assert.AreEqual(HitSource.Ranged, weapon.BasicAttackSource);
    }
}
