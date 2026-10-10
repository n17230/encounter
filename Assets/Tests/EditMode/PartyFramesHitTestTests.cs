using NUnit.Framework;
using UnityEngine;

// Pins PartyFrames' layout constants as a click target: RowWidth 200,
// Margin 12, RowHeight 44 (16 name + 2 x 14 bars), Gap 6 - so with an
// 800-wide GUI the column spans x [588, 788), row 0 spans y [12, 56), the
// gap [56, 62), row 1 [62, 106), the next gap [106, 112), row 2 [112, 156).
public class PartyFramesHitTestTests
{
    private const float GuiWidth = 800f;
    private const float ColumnX = 600f; // comfortably inside the column

    [Test]
    public void PointInFirstRowReturnsRowZero()
    {
        Assert.AreEqual(0, PartyFrames.RowIndexAt(new Vector2(ColumnX, 30f), GuiWidth, rowCount: 3));
    }

    [Test]
    public void PointInThirdRowReturnsRowTwo()
    {
        Assert.AreEqual(2, PartyFrames.RowIndexAt(new Vector2(ColumnX, 130f), GuiWidth, rowCount: 3));
    }

    [Test]
    public void PointInGapBetweenRowsMisses()
    {
        Assert.AreEqual(-1, PartyFrames.RowIndexAt(new Vector2(ColumnX, 58f), GuiWidth, rowCount: 3));
    }

    [Test]
    public void PointLeftOfColumnMisses()
    {
        Assert.AreEqual(-1, PartyFrames.RowIndexAt(new Vector2(500f, 30f), GuiWidth, rowCount: 3));
    }

    [Test]
    public void PointRightOfColumnMisses()
    {
        // Past guiWidth - Margin (788), in the margin itself.
        Assert.AreEqual(-1, PartyFrames.RowIndexAt(new Vector2(795f, 30f), GuiWidth, rowCount: 3));
    }

    [Test]
    public void PointBelowLastRowMisses()
    {
        // Row 2's band, but only two rows are drawn.
        Assert.AreEqual(-1, PartyFrames.RowIndexAt(new Vector2(ColumnX, 130f), GuiWidth, rowCount: 2));
    }

    [Test]
    public void NoRowsMeansNothingIsHit()
    {
        Assert.AreEqual(-1, PartyFrames.RowIndexAt(new Vector2(ColumnX, 30f), GuiWidth, rowCount: 0));
    }

    // Edges follow Rect.Contains: a row's top edge is inside it, its
    // bottom edge is the first point of the gap below.
    [Test]
    public void RowTopEdgeIsInsideThatRow()
    {
        Assert.AreEqual(0, PartyFrames.RowIndexAt(new Vector2(ColumnX, 12f), GuiWidth, rowCount: 3));
        Assert.AreEqual(1, PartyFrames.RowIndexAt(new Vector2(ColumnX, 62f), GuiWidth, rowCount: 3));
    }

    [Test]
    public void RowBottomEdgeIsTheGap()
    {
        Assert.AreEqual(-1, PartyFrames.RowIndexAt(new Vector2(ColumnX, 56f), GuiWidth, rowCount: 3));
    }

    [Test]
    public void LastRowBottomEdgeMissesButJustAboveItHits()
    {
        Assert.AreEqual(-1, PartyFrames.RowIndexAt(new Vector2(ColumnX, 156f), GuiWidth, rowCount: 3));
        Assert.AreEqual(2, PartyFrames.RowIndexAt(new Vector2(ColumnX, 155.9f), GuiWidth, rowCount: 3));
    }

    [Test]
    public void ColumnLeftEdgeHitsAndRightEdgeMisses()
    {
        Assert.AreEqual(0, PartyFrames.RowIndexAt(new Vector2(588f, 30f), GuiWidth, rowCount: 3));
        Assert.AreEqual(-1, PartyFrames.RowIndexAt(new Vector2(788f, 30f), GuiWidth, rowCount: 3));
    }

    // Above the first row, inside the top margin: a negative relative Y
    // truncates toward zero in C#, so without an explicit guard this would
    // wrongly land on row 0.
    [Test]
    public void PointInTopMarginMisses()
    {
        Assert.AreEqual(-1, PartyFrames.RowIndexAt(new Vector2(ColumnX, 5f), GuiWidth, rowCount: 3));
    }

    // Pins the row pitch (RowHeight + Gap = 50) across every row, not just
    // the first few: the middle of row i is at Margin + i * 50 + 22.
    [Test]
    public void EveryRowIsFiftyApart()
    {
        for (int i = 0; i < 6; i++)
        {
            Assert.AreEqual(i, PartyFrames.RowIndexAt(new Vector2(ColumnX, 12f + i * 50f + 22f), GuiWidth, rowCount: 6), $"row {i}");
        }
    }

    [Test]
    public void ScreenToGuiMapsTopLeftCornerToOrigin()
    {
        Vector2 gui = PartyFrames.ScreenToGui(new Vector2(0f, 600f), screenHeight: 600f, scale: 1f);

        Assert.AreEqual(0f, gui.x);
        Assert.AreEqual(0f, gui.y);
    }

    // Flip first, then divide: the bottom-left corner at scale 0.75 lands
    // at screenHeight / 0.75, not (screenHeight / 0.75) - 0 flipped some
    // other way.
    [Test]
    public void ScreenToGuiFlipsBeforeDividing()
    {
        Vector2 gui = PartyFrames.ScreenToGui(new Vector2(0f, 0f), screenHeight: 600f, scale: 0.75f);

        Assert.AreEqual(0f, gui.x);
        Assert.AreEqual(800f, gui.y);
    }

    [Test]
    public void ScreenToGuiFlipsYAtScaleOne()
    {
        Vector2 gui = PartyFrames.ScreenToGui(new Vector2(100f, 50f), screenHeight: 600f, scale: 1f);

        Assert.AreEqual(100f, gui.x);
        Assert.AreEqual(550f, gui.y);
    }

    [Test]
    public void ScreenToGuiDividesByScale()
    {
        Vector2 gui = PartyFrames.ScreenToGui(new Vector2(100f, 50f), screenHeight: 600f, scale: 2f);

        Assert.AreEqual(50f, gui.x);
        Assert.AreEqual(275f, gui.y);
    }
}
