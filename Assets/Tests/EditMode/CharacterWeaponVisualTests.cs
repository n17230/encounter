using NUnit.Framework;
using UnityEngine;

public class CharacterWeaponVisualTests
{
    private GameObject socketA;
    private GameObject socketB;

    [SetUp]
    public void SetUp()
    {
        socketA = new GameObject("socketA");
        socketB = new GameObject("socketB");
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(socketA);
        Object.DestroyImmediate(socketB);
    }

    [Test]
    public void NoRebuildWhenItemAndSocketAreUnchanged()
    {
        Assert.IsFalse(CharacterWeaponVisual.NeedsRebuild("broad_sword", socketA.transform, "broad_sword", socketA.transform));
    }

    [Test]
    public void RebuildWhenItemChanges()
    {
        Assert.IsTrue(CharacterWeaponVisual.NeedsRebuild("broad_sword", socketA.transform, "armorbreaker", socketA.transform));
    }

    // The gender-switch case: same item, but the live rig (and therefore
    // the socket transform) is a different one.
    [Test]
    public void RebuildWhenOnlyTheSocketChanges()
    {
        Assert.IsTrue(CharacterWeaponVisual.NeedsRebuild("broad_sword", socketA.transform, "broad_sword", socketB.transform));
    }

    [Test]
    public void NoRebuildWhileNothingIsEquipped()
    {
        Assert.IsFalse(CharacterWeaponVisual.NeedsRebuild("", null, "", null));
    }

    // A fresh component has never applied anything (null), and "nothing
    // equipped" arrives as "" - that first pass must run so the applied
    // state gets recorded.
    [Test]
    public void RebuildOnTheVeryFirstPass()
    {
        Assert.IsTrue(CharacterWeaponVisual.NeedsRebuild(null, null, "", null));
    }

    [Test]
    public void HandFollowsTheSlotUnlessTheProfileOverridesIt()
    {
        WeaponAttachProfile profile = ScriptableObject.CreateInstance<WeaponAttachProfile>();

        Assert.IsTrue(CharacterWeaponVisual.ResolveRightHand(null, slotIsRightHand: true));
        Assert.IsFalse(CharacterWeaponVisual.ResolveRightHand(null, slotIsRightHand: false));

        profile.Hand = AttachHand.SlotDefault;
        Assert.IsTrue(CharacterWeaponVisual.ResolveRightHand(profile, slotIsRightHand: true));

        profile.Hand = AttachHand.Left;
        Assert.IsFalse(CharacterWeaponVisual.ResolveRightHand(profile, slotIsRightHand: true));

        profile.Hand = AttachHand.Right;
        Assert.IsTrue(CharacterWeaponVisual.ResolveRightHand(profile, slotIsRightHand: false));

        Object.DestroyImmediate(profile);
    }

    [Test]
    public void ScaleCompensationCancelsOnlyTheSkeletonsInternalScale()
    {
        GameObject rigRoot = new GameObject("rigRoot");
        GameObject bone = new GameObject("bone");
        bone.transform.SetParent(rigRoot.transform, false);

        rigRoot.transform.localScale = Vector3.one * 2f;   // intentional whole-character scale
        bone.transform.localScale = Vector3.one * 0.5f;    // rig-internal bone scale

        Assert.AreEqual(2f, CharacterWeaponVisual.InternalScaleCompensation(bone.transform, rigRoot.transform), 0.0001f);

        Object.DestroyImmediate(rigRoot);
    }
}
