using NUnit.Framework;
using UnityEngine;

public class PlayerProfileTests
{
    [Test]
    public void SlotKeyRoundTripsIncludingShift()
    {
        PlayerProfile profile = new PlayerProfile();
        profile.SetSlotKey(2, new KeyBindingOption(KeyCode.Alpha3, requiresShift: true));

        KeyBindingOption? key = profile.GetSlotKey(2);
        Assert.IsTrue(key.HasValue);
        Assert.AreEqual(KeyCode.Alpha3, key.Value.Key);
        Assert.IsTrue(key.Value.RequiresShift);

        profile.SetSlotKey(2, null);
        Assert.IsFalse(profile.GetSlotKey(2).HasValue);
    }

    [Test]
    public void NormalizeRepairsMalformedArraysAndScale()
    {
        PlayerProfile profile = new PlayerProfile
        {
            SlotKeys = new KeyCode[2],
            SlotAbilityIds = new string[20],
            GearIds = null,
            MovementKeys = new KeyCode[1],
            UiScale = 0f,
        };

        profile.Normalize();

        Assert.AreEqual(PlayerProfile.AbilitySlots, profile.SlotKeys.Length);
        Assert.AreEqual(PlayerProfile.AbilitySlots, profile.SlotAbilityIds.Length);
        Assert.AreEqual(PlayerProfile.GearSlotCount, profile.GearIds.Length);
        CollectionAssert.AreEqual(MovementInput.Defaults, profile.MovementKeys);
        Assert.AreEqual(1f, profile.UiScale);
    }

    [Test]
    public void NormalizeKeepsOldBindingsWhenActionsAreAdded()
    {
        PlayerProfile profile = new PlayerProfile { MovementKeys = new[] { KeyCode.UpArrow, KeyCode.DownArrow } };
        profile.Normalize();

        Assert.AreEqual(PlayerProfile.MovementActionCount, profile.MovementKeys.Length);
        Assert.AreEqual(KeyCode.UpArrow, profile.MovementKeys[(int)MovementAction.Forward]);
        Assert.AreEqual(KeyCode.DownArrow, profile.MovementKeys[(int)MovementAction.Backward]);
        Assert.AreEqual(MovementInput.Defaults[(int)MovementAction.AutoAttack], profile.MovementKeys[(int)MovementAction.AutoAttack]);
    }

    [Test]
    public void JsonRoundTripPreservesChoices()
    {
        PlayerProfile profile = new PlayerProfile();
        profile.SlotAbilityIds[0] = "firebolt";
        profile.SetSlotKey(0, new KeyBindingOption(KeyCode.Q, false));
        profile.GearIds[(int)GearSlot.OffHand] = "shield";
        profile.MovementKeys[(int)MovementAction.Forward] = KeyCode.UpArrow;
        profile.UiScale = 1.5f;

        PlayerProfile loaded = new PlayerProfile();
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(profile), loaded);
        loaded.Normalize();

        Assert.AreEqual("firebolt", loaded.SlotAbilityIds[0]);
        Assert.AreEqual(KeyCode.Q, loaded.GetSlotKey(0).Value.Key);
        Assert.AreEqual("shield", loaded.GearIds[(int)GearSlot.OffHand]);
        Assert.AreEqual(KeyCode.UpArrow, loaded.MovementKeys[(int)MovementAction.Forward]);
        Assert.AreEqual(1.5f, loaded.UiScale);
    }

    [Test]
    public void OnlyRing1AndRing2AreRingSlots()
    {
        Assert.IsTrue(GearSlot.Ring1.IsRing());
        Assert.IsTrue(GearSlot.Ring2.IsRing());
        Assert.IsFalse(GearSlot.Trinket.IsRing());
        Assert.IsFalse(GearSlot.MainHand.IsRing());
    }

    [Test]
    public void NormalizeDefaultsTopAndBottomToAMatchingGenderOption()
    {
        PlayerProfile profile = new PlayerProfile();
        profile.Normalize();

        Assert.IsFalse(string.IsNullOrEmpty(profile.AppearanceTopId));
        Assert.IsFalse(string.IsNullOrEmpty(profile.AppearanceBottomId));
        Assert.AreEqual(AppearanceGender.Male, profile.GetAppearanceTop().Gender);
        Assert.AreEqual(AppearanceGender.Male, profile.GetAppearanceBottom().Gender);
        Assert.AreEqual(AppearanceSlot.Top, profile.GetAppearanceTop().Slot);
        Assert.AreEqual(AppearanceSlot.Bottom, profile.GetAppearanceBottom().Slot);
    }

    // Eyebrows/Eyes/Mouth all go through the same NormalizeRequiredSlot
    // helper as Top/Bottom - one representative slot (Eyebrows) covers the
    // shared logic; the others are exercised by DrawAppearancePanel's tabs
    // reading/writing the same catalog, not re-tested per-slot here.
    [Test]
    public void NormalizeDefaultsEyebrowsToAMatchingGenderOption()
    {
        PlayerProfile profile = new PlayerProfile();
        profile.Normalize();

        Assert.IsFalse(string.IsNullOrEmpty(profile.AppearanceEyebrowsId));
        Assert.AreEqual(AppearanceGender.Male, profile.GetAppearanceEyebrows().Gender);
        Assert.AreEqual(AppearanceSlot.Eyebrows, profile.GetAppearanceEyebrows().Slot);
    }

    [Test]
    public void NormalizeClearsAndRedefaultsEyebrowsWhenGenderSwitches()
    {
        PlayerProfile profile = new PlayerProfile { AppearanceIsFemale = false, AppearanceEyebrowsId = "eyebrows_m_3" };
        profile.Normalize();
        Assert.AreEqual("eyebrows_m_3", profile.AppearanceEyebrowsId);

        profile.AppearanceIsFemale = true;
        profile.Normalize();

        Assert.AreEqual(AppearanceGender.Female, profile.GetAppearanceEyebrows().Gender);
    }

    // Hair and FacialHair both use NormalizeOptionalSlot instead - "none" is
    // a valid choice (e.g. hair tucked away under a full helmet, or no
    // facial hair at all) and must survive Normalize() rather than being
    // redefaulted to some other style.
    [Test]
    public void NormalizeLeavesHairEmptyRatherThanDefaultingIt()
    {
        PlayerProfile profile = new PlayerProfile();
        profile.Normalize();

        Assert.IsTrue(string.IsNullOrEmpty(profile.AppearanceHairId));
    }

    [Test]
    public void NormalizeClearsHairThatNoLongerMatchesGender()
    {
        PlayerProfile profile = new PlayerProfile { AppearanceIsFemale = false, AppearanceHairId = "hair_m_5" };
        profile.Normalize();
        Assert.AreEqual("hair_m_5", profile.AppearanceHairId);

        profile.AppearanceIsFemale = true;
        profile.Normalize();

        Assert.IsTrue(string.IsNullOrEmpty(profile.AppearanceHairId));
    }

    [Test]
    public void NormalizeLeavesFacialHairEmptyRatherThanDefaultingIt()
    {
        PlayerProfile profile = new PlayerProfile();
        profile.Normalize();

        Assert.IsTrue(string.IsNullOrEmpty(profile.AppearanceFacialHairId));
    }

    [Test]
    public void NormalizeClearsFacialHairThatNoLongerMatchesGender()
    {
        PlayerProfile profile = new PlayerProfile { AppearanceIsFemale = false, AppearanceFacialHairId = "facialhair_m_1" };
        profile.Normalize();
        Assert.AreEqual("facialhair_m_1", profile.AppearanceFacialHairId);

        profile.AppearanceIsFemale = true;
        profile.Normalize();

        Assert.IsTrue(string.IsNullOrEmpty(profile.AppearanceFacialHairId));
    }

    [Test]
    public void NormalizeDropsAccessoriesThatNoLongerMatchGender()
    {
        PlayerProfile profile = new PlayerProfile
        {
            AppearanceIsFemale = false,
            AppearanceAccessoryIds = "accessory_m_knight_pauldrons;accessory_m_knight_greathelm",
        };
        profile.Normalize();
        Assert.IsTrue(profile.HasAccessory("accessory_m_knight_pauldrons"));
        Assert.IsTrue(profile.HasAccessory("accessory_m_knight_greathelm"));

        profile.AppearanceIsFemale = true;
        profile.Normalize();

        Assert.IsFalse(profile.HasAccessory("accessory_m_knight_pauldrons"));
        Assert.IsFalse(profile.HasAccessory("accessory_m_knight_greathelm"));
    }

    [Test]
    public void ToggleAccessoryAddsAndRemovesIndependently()
    {
        PlayerProfile profile = new PlayerProfile();
        profile.ToggleAccessory("accessory_m_knight_pauldrons", true);
        profile.ToggleAccessory("accessory_m_knight_greathelm", true);

        Assert.IsTrue(profile.HasAccessory("accessory_m_knight_pauldrons"));
        Assert.IsTrue(profile.HasAccessory("accessory_m_knight_greathelm"));

        profile.ToggleAccessory("accessory_m_knight_pauldrons", false);

        Assert.IsFalse(profile.HasAccessory("accessory_m_knight_pauldrons"));
        Assert.IsTrue(profile.HasAccessory("accessory_m_knight_greathelm"));
    }

    [Test]
    public void NormalizeKeepsAUnisexHeadwearRegardlessOfGender()
    {
        PlayerProfile profile = new PlayerProfile { AppearanceHeadwearId = "headwear_beret" };
        profile.Normalize();
        Assert.AreEqual("headwear_beret", profile.AppearanceHeadwearId);

        profile.AppearanceIsFemale = true;
        profile.Normalize();
        Assert.AreEqual("headwear_beret", profile.AppearanceHeadwearId);
    }

    [Test]
    public void NormalizeClearsAndRedefaultsTopBottomHeadwearWhenGenderSwitches()
    {
        PlayerProfile profile = new PlayerProfile
        {
            AppearanceIsFemale = false,
            AppearanceTopId = "top_m_knight",
            AppearanceBottomId = "bottom_m_knight",
            AppearanceHeadwearId = "headwear_kettlehat_m",
        };
        profile.Normalize();
        Assert.AreEqual("top_m_knight", profile.AppearanceTopId);

        profile.AppearanceIsFemale = true;
        profile.Normalize();

        Assert.AreEqual(AppearanceGender.Female, profile.GetAppearanceTop().Gender);
        Assert.AreEqual(AppearanceGender.Female, profile.GetAppearanceBottom().Gender);
        // A gender-specific headwear that no longer matches is cleared
        // outright, not re-defaulted (unlike Top/Bottom, "no headwear" is a
        // valid, common choice).
        Assert.IsTrue(string.IsNullOrEmpty(profile.AppearanceHeadwearId));
    }

    [Test]
    public void NormalizeClearsAStaleHeadwearIdThatNoLongerResolves()
    {
        PlayerProfile profile = new PlayerProfile { AppearanceHeadwearId = "headwear_does_not_exist" };
        profile.Normalize();

        Assert.IsTrue(string.IsNullOrEmpty(profile.AppearanceHeadwearId));
    }

    [Test]
    public void NormalizeClampsAppearanceColorIndicesToThePaletteRange()
    {
        PlayerProfile profile = new PlayerProfile
        {
            AppearanceBodyColorIndex = 999,
            AppearanceObjectColorIndex = -5,
        };
        profile.Normalize();

        AppearanceColorPalette palette = GameDatabase.Palette;
        Assert.IsNotNull(palette);
        Assert.GreaterOrEqual(profile.AppearanceBodyColorIndex, 0);
        Assert.Less(profile.AppearanceBodyColorIndex, palette.BodyColors.Length);
        Assert.GreaterOrEqual(profile.AppearanceObjectColorIndex, 0);
        Assert.Less(profile.AppearanceObjectColorIndex, palette.ObjectColors.Length);
    }
}
