using NUnit.Framework;

public class ArmorMitigationTests
{
    private const float Tolerance = 1e-5f;

    [Test]
    public void ZeroOrNegativeArmorGivesNoReduction()
    {
        Assert.AreEqual(0f, ArmorMitigation.Reduction(0f));
        Assert.AreEqual(0f, ArmorMitigation.Reduction(-5f));
        Assert.AreEqual(0f, ArmorMitigation.Reduction(-1e6f));
    }

    [TestCase(5f, 0.10448f)]
    [TestCase(10f, 0.18919f)]
    [TestCase(20f, 0.31818f)]
    [TestCase(35f, 0.44954f)]
    [TestCase(45f, 0.51220f)]
    [TestCase(100f, 0.70000f)]
    public void KnownArmorValuesMatchTheCurve(float armor, float expected)
    {
        Assert.AreEqual(expected, ArmorMitigation.Reduction(armor), Tolerance);
    }

    [Test]
    public void ReferenceArmorGivesReferenceReduction()
    {
        Assert.AreEqual(0.7f, ArmorMitigation.Reduction(100f), Tolerance);
    }

    [Test]
    public void ArmorForHalfReductionGivesFiftyPercent()
    {
        Assert.AreEqual(42.857143f, ArmorMitigation.ArmorForHalfReduction, 1e-4f);
        Assert.AreEqual(0.5f, ArmorMitigation.Reduction(ArmorMitigation.ArmorForHalfReduction), Tolerance);
    }

    [Test]
    public void MoreArmorAlwaysReducesMore()
    {
        foreach (float a in new[] { 0f, 10f, 45f, 100f, 1000f })
        {
            Assert.Greater(ArmorMitigation.Reduction(a + 1f), ArmorMitigation.Reduction(a), $"armor {a}");
        }
    }

    [Test]
    public void NeverReachesFullReduction()
    {
        float huge = ArmorMitigation.Reduction(1e6f);
        Assert.Less(huge, 1f);
        Assert.Greater(huge, 0.9999f);
    }
}
