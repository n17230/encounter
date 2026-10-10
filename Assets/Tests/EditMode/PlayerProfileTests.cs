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
            EquipmentIds = null,
            MovementKeys = new KeyCode[1],
            UiScale = 0f,
        };

        profile.Normalize();

        Assert.AreEqual(PlayerProfile.AbilitySlots, profile.SlotKeys.Length);
        Assert.AreEqual(PlayerProfile.AbilitySlots, profile.SlotAbilityIds.Length);
        Assert.AreEqual(PlayerProfile.EquipmentSlotCount, profile.EquipmentIds.Length);
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
        profile.EquipmentIds[(int)EquipmentSlot.OffHand] = "shield";
        profile.MovementKeys[(int)MovementAction.Forward] = KeyCode.UpArrow;
        profile.UiScale = 1.5f;

        PlayerProfile loaded = new PlayerProfile();
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(profile), loaded);
        loaded.Normalize();

        Assert.AreEqual("firebolt", loaded.SlotAbilityIds[0]);
        Assert.AreEqual(KeyCode.Q, loaded.GetSlotKey(0).Value.Key);
        Assert.AreEqual("shield", loaded.EquipmentIds[(int)EquipmentSlot.OffHand]);
        Assert.AreEqual(KeyCode.UpArrow, loaded.MovementKeys[(int)MovementAction.Forward]);
        Assert.AreEqual(1.5f, loaded.UiScale);
    }

    [Test]
    public void NormalizeTurnsAMissingCharacterNameIntoEmpty()
    {
        PlayerProfile profile = new PlayerProfile { CharacterName = null };
        profile.Normalize();

        Assert.AreEqual("", profile.CharacterName);
    }

    [Test]
    public void ApplyValidatedLoadoutKeepsAcceptedSlotsAndClearsDroppedOnesWithTheirKeys()
    {
        PlayerProfile profile = new PlayerProfile();
        profile.SlotAbilityIds[0] = "a";
        profile.SlotAbilityIds[1] = "b";
        profile.SlotAbilityIds[2] = "c";
        profile.SetSlotKey(0, new KeyBindingOption(KeyCode.Q, false));
        profile.SetSlotKey(1, new KeyBindingOption(KeyCode.E, true));
        profile.SetSlotKey(2, new KeyBindingOption(KeyCode.R, false));

        profile.ApplyValidatedLoadout(new[] { "a", "", "c" });

        Assert.AreEqual("a", profile.SlotAbilityIds[0]);
        Assert.IsNull(profile.SlotAbilityIds[1]);
        Assert.AreEqual("c", profile.SlotAbilityIds[2]);
        Assert.AreEqual(KeyCode.Q, profile.SlotKeys[0]);
        Assert.AreEqual(KeyCode.None, profile.SlotKeys[1]);
        Assert.IsFalse(profile.SlotKeyShift[1]);
        Assert.AreEqual(KeyCode.R, profile.SlotKeys[2]);
    }

    [Test]
    public void ApplyValidatedLoadoutIgnoresADuplicateIdAfterTheFirst()
    {
        PlayerProfile profile = new PlayerProfile();

        profile.ApplyValidatedLoadout(new[] { "a", "a", "b" });

        Assert.AreEqual("a", profile.SlotAbilityIds[0]);
        Assert.IsNull(profile.SlotAbilityIds[1]);
        Assert.AreEqual("b", profile.SlotAbilityIds[2]);
    }

    [Test]
    public void ApplyValidatedEquipmentOverwritesEveryPhysicalSlot()
    {
        PlayerProfile profile = new PlayerProfile();
        profile.EquipmentIds[(int)EquipmentSlot.Helmet] = "old_helmet";
        string[] validated = new string[PlayerProfile.EquipmentSlotCount];
        validated[(int)EquipmentSlot.MainHand] = "sword";

        profile.ApplyValidatedEquipment(validated);

        Assert.IsNull(profile.EquipmentIds[(int)EquipmentSlot.Helmet]);
        Assert.AreEqual("sword", profile.EquipmentIds[(int)EquipmentSlot.MainHand]);
    }

    // A reply shorter than the slot count (a malformed/truncated joined
    // string) clears the tail rather than throwing, and an empty segment
    // becomes null, the same "empty" every other reader of EquipmentIds expects.
    [Test]
    public void ApplyValidatedEquipmentWithAShortArrayClearsTheTailAndTurnsEmptyIntoNull()
    {
        PlayerProfile profile = new PlayerProfile();
        profile.EquipmentIds[(int)EquipmentSlot.OffHand] = "old_shield";

        profile.ApplyValidatedEquipment(new[] { "helmet", "" });

        Assert.AreEqual("helmet", profile.EquipmentIds[(int)EquipmentSlot.Helmet]);
        Assert.IsNull(profile.EquipmentIds[(int)EquipmentSlot.Necklace]);
        Assert.IsNull(profile.EquipmentIds[(int)EquipmentSlot.OffHand]);
    }

    [Test]
    public void ApplyValidatedLoadoutIgnoresEntriesPastTheSlotCount()
    {
        PlayerProfile profile = new PlayerProfile();

        profile.ApplyValidatedLoadout(new[] { "a", "b", "c", "d", "e", "f", "g" });

        Assert.AreEqual(PlayerProfile.AbilitySlots, profile.SlotAbilityIds.Length);
        Assert.AreEqual("e", profile.SlotAbilityIds[PlayerProfile.AbilitySlots - 1]);
    }

    [Test]
    public void NormalizeKeepsASetCharacterName()
    {
        PlayerProfile profile = new PlayerProfile { CharacterName = "Bob" };
        profile.Normalize();

        Assert.AreEqual("Bob", profile.CharacterName);
    }

    [Test]
    public void OnlyRing1AndRing2AreRingSlots()
    {
        Assert.IsTrue(EquipmentSlot.Ring1.IsRing());
        Assert.IsTrue(EquipmentSlot.Ring2.IsRing());
        Assert.IsFalse(EquipmentSlot.Trinket.IsRing());
        Assert.IsFalse(EquipmentSlot.MainHand.IsRing());
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
