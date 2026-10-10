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

    // Distinct fractions, so a swapped health/mana would be caught.
    [Test]
    public void ResurrectDescribesItsHealthAndManaFractionsAsPercents()
    {
        AbilityData ability = ScriptableObject.CreateInstance<AbilityData>();
        try
        {
            ability.ResurrectTarget = true;
            ability.ResurrectHealthPercent = 0.25f;
            ability.ResurrectManaPercent = 0.1f;

            StringAssert.Contains("Resurrects a dead player with 25% health and 10% mana.", TooltipText.BuildAbilityBody(ability));
        }
        finally
        {
            Object.DestroyImmediate(ability);
        }
    }

    // Pins the "0" format: a non-round fraction renders as a whole percent,
    // not "33.3%" or "33.300003%".
    [Test]
    public void ResurrectRoundsNonRoundFractionsToWholePercents()
    {
        AbilityData ability = ScriptableObject.CreateInstance<AbilityData>();
        try
        {
            ability.ResurrectTarget = true;
            ability.ResurrectHealthPercent = 0.333f;
            ability.ResurrectManaPercent = 0.333f;

            StringAssert.Contains("33% health and 33% mana.", TooltipText.BuildAbilityBody(ability));
        }
        finally
        {
            Object.DestroyImmediate(ability);
        }
    }

    private static string BuildAuraBody(bool showsPerceptionOrbs, MinimapReveal reveals)
    {
        AbilityData ability = ScriptableObject.CreateInstance<AbilityData>();
        try
        {
            ability.IsAuraSpell = true;
            ability.AuraShowsPerceptionOrbs = showsPerceptionOrbs;
            ability.AuraReveals = reveals;
            return TooltipText.BuildAbilityBody(ability);
        }
        finally
        {
            Object.DestroyImmediate(ability);
        }
    }

    [Test]
    public void PerceptionAuraDescribesItsOrbs()
    {
        StringAssert.Contains("Shows an orb above every mob", BuildAuraBody(true, MinimapReveal.None));
    }

    [Test]
    public void RevealOnlyAuraDoesNotMentionOrbs()
    {
        StringAssert.DoesNotContain("Shows an orb above every mob", BuildAuraBody(false, MinimapReveal.Mobs));
    }

    private static string BuildGroundBody(bool isStructure, float structureWidth)
    {
        AbilityData ability = ScriptableObject.CreateInstance<AbilityData>();
        try
        {
            ability.IsGroundTargeted = true;
            ability.GroundEffectRadius = 10f;
            ability.IsPersistentStructure = isStructure;
            ability.StructureWidth = structureWidth;
            return TooltipText.BuildAbilityBody(ability);
        }
        finally
        {
            Object.DestroyImmediate(ability);
        }
    }

    [Test]
    public void StructureShowsItsWallWidthNotADiameter()
    {
        string body = BuildGroundBody(true, 30f);

        StringAssert.Contains("Raises a 30-wide wall", body);
        StringAssert.DoesNotContain("diameter", body);
    }

    [Test]
    public void StructureWidthShowsTheClampedWholeSegmentWidth()
    {
        StringAssert.Contains("32-wide", BuildGroundBody(true, 100f));
        StringAssert.Contains("20-wide", BuildGroundBody(true, 20.4f));
        StringAssert.Contains("21-wide", BuildGroundBody(true, 20.6f));
    }

    [Test]
    public void NonStructureGroundAbilityStillShowsItsDiameter()
    {
        StringAssert.Contains("Ground-targeted: 20 diameter area.", BuildGroundBody(false, 30f));
    }
}
