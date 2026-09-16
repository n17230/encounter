using NUnit.Framework;
using UnityEngine;

// Uses real Ids/node names from the generated appearance catalog
// (Assets/Resources/Data/AppearancePieces), same pattern PlayerProfileTests
// already relies on for ability/gear Ids - these assets are expected to
// keep existing.
public class CharacterAppearanceApplierTests
{
    private Transform maleRoot;
    private Transform femaleRoot;
    private CharacterAppearanceApplier applier;

    [SetUp]
    public void SetUp()
    {
        maleRoot = new GameObject("MaleRig").transform;
        femaleRoot = new GameObject("FemaleRig").transform;

        foreach (string name in new[]
        {
            "M_Knight_Top", "M_Knight_Bottom", "M_Archer_Top", "M_Archer_Bottom", "head_joint",
            "M_hair_1", "M_hair_2", "M_hair_2b",
            "M_Knight_GreatHelm", "M_Knight_NeckScarf", "M_Knight_Pauldrons",
            "M_TopBody", "M_BottomBody",
        })
        {
            new GameObject(name).transform.SetParent(maleRoot);
        }
        foreach (string name in new[] { "F_Witch_Top", "F_Witch_Bottom", "F_Archer_Bottom", "F_BottomBody", "head_joint" })
        {
            new GameObject(name).transform.SetParent(femaleRoot);
        }

        applier = new CharacterAppearanceApplier(maleRoot, femaleRoot);
    }

    [TearDown]
    public void TearDown()
    {
        applier.Cleanup();
        Object.DestroyImmediate(maleRoot.gameObject);
        Object.DestroyImmediate(femaleRoot.gameObject);
    }

    // renderer.sharedMaterial (singular) only ever reads/writes a renderer's
    // FIRST material slot - several real pieces bake a skin-toned region
    // and a clothing-toned region into two submeshes of the same renderer
    // (confirmed via a real in-Editor renderer showing Element 0 "Objects"
    // correctly swapped and Element 1 "Body" left on the broken
    // shader-graph default). This must walk the full material array.
    [Test]
    public void ApplyColorRetintsEveryMatchingMaterialSlotNotJustTheFirst()
    {
        GameObject knightTop = Find(maleRoot, "M_Knight_Top");
        MeshRenderer renderer = knightTop.AddComponent<MeshRenderer>();
        Shader placeholderShader = Shader.Find("Hidden/InternalErrorShader");
        Material objectsSlot = new Material(placeholderShader) { name = "RGBRecolor_Objects" };
        Material bodySlot = new Material(placeholderShader) { name = "RGBRecolor_Body" };
        renderer.sharedMaterials = new[] { objectsSlot, bodySlot };

        AppearanceColorPalette palette = GameDatabase.Palette;
        Assert.IsNotNull(palette, "Test relies on the real AppearanceColorPalette asset existing.");

        applier.Apply(Selection(false, "top_m_knight", "bottom_m_knight"));

        Material[] resultMaterials = renderer.sharedMaterials;
        Assert.AreEqual(palette.ObjectMaterial, resultMaterials[0]);
        Assert.AreEqual(palette.BodyMaterial, resultMaterials[1]);

        // Each slot must get its OWN texture via an indexed property block -
        // an un-indexed block would apply the same (wrong, for one of the
        // two) texture to both slots.
        MaterialPropertyBlock objectsBlock = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(objectsBlock, 0);
        MaterialPropertyBlock bodyBlock = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(bodyBlock, 1);

        Assert.AreEqual(palette.ObjectColors[0], objectsBlock.GetTexture("_BaseMap"));
        Assert.AreEqual(palette.BodyColors[0], bodyBlock.GetTexture("_BaseMap"));
    }

    [Test]
    public void ApplyEnablesOnlyTheSelectedTopAndBottomNodes()
    {
        applier.Apply(Selection(false, "top_m_knight", "bottom_m_knight"));

        Assert.IsTrue(Find(maleRoot, "M_Knight_Top").activeSelf);
        Assert.IsTrue(Find(maleRoot, "M_Knight_Bottom").activeSelf);
        Assert.IsFalse(Find(maleRoot, "M_Archer_Top").activeSelf);
        Assert.IsFalse(Find(maleRoot, "M_Archer_Bottom").activeSelf);
    }

    // The base-skin body ships with M_TopBody/M_BottomBody always active by
    // default; since Top/Bottom are required slots (always resolved once
    // Normalize has run), the matching skin part must be hidden once a real
    // piece is equipped, or it pokes through wherever the clothing mesh
    // doesn't fully enclose it.
    [Test]
    public void ApplyHidesBaseSkinBodyPartsOnceTopAndBottomAreResolved()
    {
        Assert.IsTrue(Find(maleRoot, "M_TopBody").activeSelf);
        Assert.IsTrue(Find(maleRoot, "M_BottomBody").activeSelf);

        applier.Apply(Selection(false, "top_m_knight", "bottom_m_knight"));

        Assert.IsFalse(Find(maleRoot, "M_TopBody").activeSelf);
        Assert.IsFalse(Find(maleRoot, "M_BottomBody").activeSelf);
    }

    // Some Bottom pieces are deliberately short (skirts) and rely on the
    // character's own legs showing below the hem - CoversLegs = false on
    // that piece must keep the base-skin legs visible instead of hiding
    // them the way a full-coverage Bottom (pants, armor) would.
    [Test]
    public void ApplyLeavesBaseSkinVisibleForABottomThatDoesNotCoverLegs()
    {
        applier.Apply(Selection(true, "top_f_witch", "bottom_f_archer"));

        Assert.IsTrue(Find(femaleRoot, "F_BottomBody").activeSelf);
    }

    // An unresolved Top/Bottom (e.g. before Normalize has ever run) should
    // fall back to showing skin rather than hiding it with nothing worn
    // over it.
    [Test]
    public void ApplyLeavesBaseSkinVisibleWhenTopOrBottomDoesNotResolve()
    {
        applier.Apply(Selection(false, "", ""));

        Assert.IsTrue(Find(maleRoot, "M_TopBody").activeSelf);
        Assert.IsTrue(Find(maleRoot, "M_BottomBody").activeSelf);
    }

    [Test]
    public void ApplySwitchesActiveTopNodeWhenSelectionChanges()
    {
        applier.Apply(Selection(false, "top_m_knight", "bottom_m_knight"));
        applier.Apply(Selection(false, "top_m_archer", "bottom_m_knight"));

        Assert.IsFalse(Find(maleRoot, "M_Knight_Top").activeSelf);
        Assert.IsTrue(Find(maleRoot, "M_Archer_Top").activeSelf);
    }

    [Test]
    public void ApplyTogglesRigRootActiveStateByGender()
    {
        applier.Apply(Selection(false, "top_m_knight", "bottom_m_knight"));
        Assert.IsTrue(maleRoot.gameObject.activeSelf);
        Assert.IsFalse(femaleRoot.gameObject.activeSelf);

        applier.Apply(Selection(true, "top_f_witch", "bottom_f_witch"));
        Assert.IsFalse(maleRoot.gameObject.activeSelf);
        Assert.IsTrue(femaleRoot.gameObject.activeSelf);
    }

    [Test]
    public void ApplyWithEmptyHeadwearIdCreatesNoInstanceUnderHeadBone()
    {
        applier.Apply(Selection(false, "top_m_knight", "bottom_m_knight"));

        Assert.AreEqual(0, Find(maleRoot, "head_joint").transform.childCount);
    }

    // Every current AppearanceHeadwearData asset has its Prefab field unset
    // (a real, currently-outstanding limitation - see the character
    // creation plan notes: hand-authoring that reference isn't reliably
    // derivable outside the Editor). Resolving one should still no-op
    // cleanly rather than throw.
    [Test]
    public void ApplyWithAnUnassignedHeadwearPrefabDoesNotThrowOrCreateAnInstance()
    {
        Assert.DoesNotThrow(() => applier.Apply(Selection(false, "top_m_knight", "bottom_m_knight", "headwear_beret")));
        Assert.AreEqual(0, Find(maleRoot, "head_joint").transform.childCount);
    }

    private static AppearanceSelection Selection(bool female, string topId, string bottomId, string headwearId = "",
        string hairId = "", string accessoryIds = "")
    {
        return new AppearanceSelection
        {
            Female = female,
            TopId = topId,
            BottomId = bottomId,
            HeadwearId = headwearId,
            EyebrowsId = "",
            EyesId = "",
            MouthId = "",
            HairId = hairId,
            FacialHairId = "",
            AccessoryIds = accessoryIds,
            BodyColorIndex = 0,
            ObjectColorIndex = 0,
        };
    }

    [Test]
    public void ApplyHairShowsTheHeadwearSafeVariantWhenHeadwearIsSelected()
    {
        applier.Apply(Selection(false, "top_m_knight", "bottom_m_knight", headwearId: "headwear_beret", hairId: "hair_m_2"));

        Assert.IsFalse(Find(maleRoot, "M_hair_2").activeSelf);
        Assert.IsTrue(Find(maleRoot, "M_hair_2b").activeSelf);
    }

    [Test]
    public void ApplyHairShowsTheNormalNodeWhenNoHeadwearIsSelected()
    {
        applier.Apply(Selection(false, "top_m_knight", "bottom_m_knight", hairId: "hair_m_2"));

        Assert.IsTrue(Find(maleRoot, "M_hair_2").activeSelf);
        Assert.IsFalse(Find(maleRoot, "M_hair_2b").activeSelf);
    }

    // Every hairstyle other than the selected one must show neither of its
    // nodes - this specifically guards the "not selected" branch, which a
    // selected-style-only assertion (the two tests above) can't catch on
    // its own (e.g. dropping the isSelected guard from the normal-node
    // SetActive call would turn every unselected style's normal node on).
    [Test]
    public void ApplyHairDisablesEveryOtherHairstylesNode()
    {
        applier.Apply(Selection(false, "top_m_knight", "bottom_m_knight", hairId: "hair_m_2"));

        Assert.IsFalse(Find(maleRoot, "M_hair_1").activeSelf);
    }

    // hair_m_1 has no headwear-safe variant (HeadwearVariantNodeName is
    // empty) - selecting it while headwear is worn must fall back to the
    // normal node rather than disabling hair entirely.
    [Test]
    public void ApplyHairFallsBackToNormalNodeWhenSelectedHairHasNoVariant()
    {
        applier.Apply(Selection(false, "top_m_knight", "bottom_m_knight", headwearId: "headwear_beret", hairId: "hair_m_1"));

        Assert.IsTrue(Find(maleRoot, "M_hair_1").activeSelf);
    }

    [Test]
    public void ApplyAccessoriesActivatesOnlyTheSelectedOnesIndependently()
    {
        applier.Apply(Selection(false, "top_m_knight", "bottom_m_knight",
            accessoryIds: "accessory_m_knight_greathelm;accessory_m_knight_pauldrons"));

        Assert.IsTrue(Find(maleRoot, "M_Knight_GreatHelm").activeSelf);
        Assert.IsTrue(Find(maleRoot, "M_Knight_Pauldrons").activeSelf);
        Assert.IsFalse(Find(maleRoot, "M_Knight_NeckScarf").activeSelf);
    }

    [Test]
    public void ApplyAccessoriesTurnsOffAPreviouslySelectedOneWhenNoLongerListed()
    {
        applier.Apply(Selection(false, "top_m_knight", "bottom_m_knight", accessoryIds: "accessory_m_knight_greathelm"));
        applier.Apply(Selection(false, "top_m_knight", "bottom_m_knight", accessoryIds: "accessory_m_knight_pauldrons"));

        Assert.IsFalse(Find(maleRoot, "M_Knight_GreatHelm").activeSelf);
        Assert.IsTrue(Find(maleRoot, "M_Knight_Pauldrons").activeSelf);
    }

    [Test]
    public void HeadwearNeedsRebuildOnlyWhenGenderOrHeadwearIdActuallyChanges()
    {
        // Very first call ever (nothing applied yet) - must rebuild.
        Assert.IsTrue(CharacterAppearanceApplier.HeadwearNeedsRebuild(null, null, false, ""));
        // Identical to what's already applied - no rebuild needed.
        Assert.IsFalse(CharacterAppearanceApplier.HeadwearNeedsRebuild(false, "beret", false, "beret"));
        // Gender changed, headwearId didn't - still needs a rebuild, since
        // the existing instance is parented under the now-inactive rig.
        Assert.IsTrue(CharacterAppearanceApplier.HeadwearNeedsRebuild(false, "beret", true, "beret"));
        // headwearId changed, gender didn't.
        Assert.IsTrue(CharacterAppearanceApplier.HeadwearNeedsRebuild(false, "beret", false, "hood"));
    }

    private static GameObject Find(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name) return child.gameObject;
        }
        Assert.Fail($"No child named {name} found");
        return null;
    }
}
