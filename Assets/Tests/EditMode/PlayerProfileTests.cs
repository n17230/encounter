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
}
