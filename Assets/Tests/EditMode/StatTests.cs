using NUnit.Framework;

public class StatTests
{
    [Test]
    public void BaseValueWithNoModifiers()
    {
        Assert.AreEqual(10f, new Stat(10f).Value);
    }

    [Test]
    public void FlatIsAddedBeforePercentIsApplied()
    {
        Stat stat = new Stat(100f);
        stat.AddModifier(new StatModifier(20f, StatModifierType.Flat));
        stat.AddModifier(new StatModifier(0.5f, StatModifierType.PercentAdditive));
        Assert.AreEqual(180f, stat.Value, 0.001f);
    }

    [Test]
    public void PercentModifiersAddRatherThanCompound()
    {
        Stat stat = new Stat(100f);
        stat.AddModifier(new StatModifier(0.5f, StatModifierType.PercentAdditive));
        stat.AddModifier(new StatModifier(0.5f, StatModifierType.PercentAdditive));
        Assert.AreEqual(200f, stat.Value, 0.001f);
    }

    [Test]
    public void RemoveAllFromSourceStripsOnlyThatSource()
    {
        object shield = new object();
        object slow = new object();
        Stat stat = new Stat(100f);
        stat.AddModifier(new StatModifier(20f, StatModifierType.Flat, shield));
        stat.AddModifier(new StatModifier(5f, StatModifierType.Flat, shield));
        stat.AddModifier(new StatModifier(-0.5f, StatModifierType.PercentAdditive, slow));

        Assert.AreEqual(2, stat.RemoveAllModifiersFromSource(shield));
        Assert.AreEqual(50f, stat.Value, 0.001f);
        Assert.AreEqual(0, stat.RemoveAllModifiersFromSource(shield));
    }

    [Test]
    public void ValueRecalculatesAfterModifierChanges()
    {
        Stat stat = new Stat(10f);
        Assert.AreEqual(10f, stat.Value);
        StatModifier modifier = new StatModifier(5f, StatModifierType.Flat);
        stat.AddModifier(modifier);
        Assert.AreEqual(15f, stat.Value);
        Assert.IsTrue(stat.RemoveModifier(modifier));
        Assert.AreEqual(10f, stat.Value);
    }
}
