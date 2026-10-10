using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class TooltipTextTests
{
    private ItemData item;

    [SetUp]
    public void CreateItem()
    {
        item = ScriptableObject.CreateInstance<ItemData>();
        item.ItemName = "Test Item";
    }

    [TearDown]
    public void DestroyItem()
    {
        Object.DestroyImmediate(item);
    }

    [Test]
    public void BlockChanceRendersAsPercent()
    {
        item.Bonuses.Add(new StatBonus { Stat = StatType.BlockChancePercent, ModifierType = StatModifierType.Flat, Value = 0.05f });

        string tooltip = TooltipText.BuildItemTooltip(item);

        StringAssert.Contains("Block Chance +5%", tooltip);
    }

    [Test]
    public void HpThresholdEffectsAreDescribed()
    {
        item.HpThresholdEffects.Add(new HpThresholdEffect
        {
            HealthPercentThreshold = 0.5f,
            AboveThresholdBonuses = new List<StatBonus> { new StatBonus { Stat = StatType.DamageTakenMultiplier, ModifierType = StatModifierType.PercentAdditive, Value = -0.15f } },
            BelowThresholdBonuses = new List<StatBonus> { new StatBonus { Stat = StatType.DamageMultiplier, ModifierType = StatModifierType.PercentAdditive, Value = 0.15f } },
        });

        string tooltip = TooltipText.BuildItemTooltip(item);

        StringAssert.Contains("Above 50% health:", tooltip);
        StringAssert.Contains("At or below 50% health:", tooltip);
        // Both sides read as clean English, not the raw enum name (the
        // exact bug class reported for BlockChancePercent) - Barbarian's
        // Mantle is what actually exercises DamageTakenMultiplier/
        // DamageMultiplier through this path.
        StringAssert.Contains("Damage taken -15%", tooltip);
        StringAssert.Contains("Damage dealt +15%", tooltip);
        StringAssert.DoesNotContain("DamageTakenMultiplier", tooltip);
        StringAssert.DoesNotContain("DamageMultiplier", tooltip);
    }

    [Test]
    public void AirHoverIsMentionedOnlyWhenGranted()
    {
        item.GrantsAirHover = false;
        StringAssert.DoesNotContain("hover", TooltipText.BuildItemTooltip(item));

        item.GrantsAirHover = true;
        StringAssert.Contains("hover", TooltipText.BuildItemTooltip(item));
    }

    [Test]
    public void TwoHandedIsMentionedOnlyWhenSet()
    {
        item.TwoHanded = false;
        StringAssert.DoesNotContain("Two-handed", TooltipText.BuildItemTooltip(item));

        item.TwoHanded = true;
        StringAssert.Contains("Two-handed", TooltipText.BuildItemTooltip(item));
    }
}
