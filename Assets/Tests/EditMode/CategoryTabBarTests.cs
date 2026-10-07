using NUnit.Framework;

public class CategoryTabBarTests
{
    [Test]
    public void AValidSelectionSurvivesARebuild()
    {
        Assert.AreEqual(3, CategoryTabBar.ClampSelection(3, 5));
        Assert.AreEqual(0, CategoryTabBar.ClampSelection(0, 5));
        Assert.AreEqual(4, CategoryTabBar.ClampSelection(4, 5));
    }

    [Test]
    public void ASelectionPastTheEndFallsBackToTheFirstTab()
    {
        Assert.AreEqual(0, CategoryTabBar.ClampSelection(5, 5));
        Assert.AreEqual(0, CategoryTabBar.ClampSelection(7, 2));
    }

    [Test]
    public void ANegativeOrEmptySelectionFallsBackToTheFirstTab()
    {
        Assert.AreEqual(0, CategoryTabBar.ClampSelection(-1, 5));
        Assert.AreEqual(0, CategoryTabBar.ClampSelection(0, 0));
    }
}
