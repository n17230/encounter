using System.Collections.Generic;
using UnityEngine;

// Everything needed to drive one Apply() call - built by CharacterAppearance
// from its NetworkVariables, or by CharacterPreview from ProfileStore
// .Current. AccessoryIds is a single ';'-joined string (parsed internally),
// matching the join convention CharacterEquipment already uses for
// multi-value fields.
public struct AppearanceSelection
{
    public bool Female;
    public string TopId;
    public string BottomId;
    public string HeadwearId;
    public string EyebrowsId;
    public string EyesId;
    public string MouthId;
    public string HairId;
    public string FacialHairId;
    public string AccessoryIds;
    public int BodyColorIndex;
    public int ObjectColorIndex;
}

// Shared cosmetic-appearance apply logic - toggling the resolved Top/Bottom/
// face-feature/hair/accessory nodes, instantiating/destroying the resolved
// headwear piece, and swapping the resolved recolor textures via
// MaterialPropertyBlock. Plain C#, not a NetworkBehaviour, so it can drive
// both the live networked CharacterAppearance (fed from its
// NetworkVariables) and the local-only menu CharacterPreview (fed directly
// from ProfileStore.Current) without either duplicating this logic.
public class CharacterAppearanceApplier
{
    // The pack ships every renderer defaulted to the shader-graph
    // RGBRecolor_* material; ApplyColor reassigns them to the palette's
    // plain PreColor_* material the first time it runs (and is a no-op to
    // match again afterward, since the reassigned name is included too).
    private static readonly string[] BodyMaterialNames = { "RGBRecolor_Body", "PreColor_Body" };
    private static readonly string[] ObjectMaterialNames = { "RGBRecolor_Objects", "PreColor_Objects" };
    private const string HeadBoneName = "head_joint";

    private readonly Transform maleRigRoot;
    private readonly Transform femaleRigRoot;

    // Every named node under each rig root, built once - Top/Bottom/face-
    // feature/hair/accessory/headwear resolution reads the candidate node
    // names straight from the data catalogs (GameDatabase.AppearancePieces/
    // AppearanceAccessories), so a new pack piece is just a new data asset,
    // no code change here.
    private readonly Dictionary<string, GameObject> maleNodes = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, GameObject> femaleNodes = new Dictionary<string, GameObject>();
    private GameObject headwearInstance;
    // What ApplyHeadwear last actually built the instance for - lets it skip
    // the Destroy+Instantiate when neither changed (e.g. only a color index
    // changed). appliedFemale starts null so the very first call always runs
    // once, even though headwearId itself may start at "" (no headwear).
    private bool? appliedFemale;
    private string appliedHeadwearId;

    public CharacterAppearanceApplier(Transform maleRigRoot, Transform femaleRigRoot)
    {
        this.maleRigRoot = maleRigRoot;
        this.femaleRigRoot = femaleRigRoot;
        IndexNodes(maleRigRoot, maleNodes);
        IndexNodes(femaleRigRoot, femaleNodes);
    }

    private static void IndexNodes(Transform root, Dictionary<string, GameObject> nodes)
    {
        if (root == null) return;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            nodes[child.name] = child.gameObject;
        }
    }

    public void Apply(AppearanceSelection selection)
    {
        // Both roots are only null before the Editor-side wiring step is
        // done - not a case that should happen once a rig is actually
        // placed, but callers can exist ahead of that step.
        if (maleRigRoot == null || femaleRigRoot == null) return;

        bool female = selection.Female;
        maleRigRoot.gameObject.SetActive(!female);
        femaleRigRoot.gameObject.SetActive(female);
        Dictionary<string, GameObject> nodes = female ? femaleNodes : maleNodes;
        Transform rigRoot = female ? femaleRigRoot : maleRigRoot;
        AppearanceGender gender = female ? AppearanceGender.Female : AppearanceGender.Male;

        AppearancePieceData topPiece = ApplySlot(nodes, gender, AppearanceSlot.Top, selection.TopId);
        AppearancePieceData bottomPiece = ApplySlot(nodes, gender, AppearanceSlot.Bottom, selection.BottomId);
        HideBaseBodyPart(nodes, topPiece != null, "M_TopBody", "F_TopBody");
        HideBaseBodyPart(nodes, bottomPiece != null && bottomPiece.CoversLegs, "M_BottomBody", "F_BottomBody");
        ApplySlot(nodes, gender, AppearanceSlot.Eyebrows, selection.EyebrowsId);
        ApplySlot(nodes, gender, AppearanceSlot.Eyes, selection.EyesId);
        ApplySlot(nodes, gender, AppearanceSlot.Mouth, selection.MouthId);
        ApplySlot(nodes, gender, AppearanceSlot.FacialHair, selection.FacialHairId);
        ApplyHair(nodes, gender, selection.HairId, selection.HeadwearId);
        ApplyAccessories(nodes, gender, selection.AccessoryIds);
        ApplyHeadwear(nodes, female, selection.HeadwearId);

        AppearanceColorPalette palette = GameDatabase.Palette;
        if (palette == null) return;

        ApplyColor(rigRoot, BodyMaterialNames, palette.BodyMaterial, palette.BodyColors, selection.BodyColorIndex);
        ApplyColor(rigRoot, ObjectMaterialNames, palette.ObjectMaterial, palette.ObjectColors, selection.ObjectColorIndex);
        if (headwearInstance != null)
        {
            ApplyColor(headwearInstance.transform, ObjectMaterialNames, palette.ObjectMaterial, palette.ObjectColors, selection.ObjectColorIndex);
        }
    }

    // Destroys any live headwear instance - callers invoke this from their
    // own despawn/destroy lifecycle.
    public void Cleanup()
    {
        if (headwearInstance != null) Object.Destroy(headwearInstance);
        headwearInstance = null;
        appliedFemale = null;
        appliedHeadwearId = null;
    }

    // Enables the one node matching the resolved piece, disables every other
    // candidate node for this slot+gender. Returns the resolved piece (or
    // null) - Top/Bottom use this to decide whether/how to hide the
    // base-skin mesh underneath (see HideBaseBodyPart).
    private static AppearancePieceData ApplySlot(Dictionary<string, GameObject> nodes, AppearanceGender gender, AppearanceSlot slot, string selectedId)
    {
        AppearancePieceData selected = string.IsNullOrEmpty(selectedId) ? null : GameDatabase.GetAppearancePiece(selectedId);
        foreach (AppearancePieceData piece in GameDatabase.AppearancePieces)
        {
            if (piece.Slot != slot || piece.Gender != gender) continue;
            if (nodes.TryGetValue(piece.NodeName, out GameObject node) && node != null)
            {
                node.SetActive(piece == selected);
            }
        }
        return selected;
    }

    // The base body ships with its bare-skin torso/legs always active, with
    // nothing hiding them under whatever's worn. Top and Bottom are required
    // slots (always resolved to something once Normalize has run), so the
    // matching base-skin part should never actually be visible - leaving it
    // on is exactly what let bare legs poke through equipped Bottom pieces
    // wherever the clothing mesh doesn't fully enclose it. Only hides it once
    // a piece actually resolved, so an unnormalized/edge-case empty
    // selection still falls back to showing skin rather than nothing.
    private static void HideBaseBodyPart(Dictionary<string, GameObject> nodes, bool pieceResolved, string maleNodeName, string femaleNodeName)
    {
        if (!pieceResolved) return;
        if (nodes.TryGetValue(maleNodeName, out GameObject maleNode) && maleNode != null) maleNode.SetActive(false);
        if (nodes.TryGetValue(femaleNodeName, out GameObject femaleNode) && femaleNode != null) femaleNode.SetActive(false);
    }

    // Same one-of-many selection as ApplySlot, but the selected hairstyle
    // shows its HeadwearVariantNodeName instead of its normal NodeName
    // whenever headwear is currently selected and a variant exists - avoids
    // the hair clipping through the hat. Every other hairstyle's normal and
    // variant nodes both stay off regardless.
    private static void ApplyHair(Dictionary<string, GameObject> nodes, AppearanceGender gender, string hairId, string headwearId)
    {
        bool headwearActive = !string.IsNullOrEmpty(headwearId);
        AppearancePieceData selected = string.IsNullOrEmpty(hairId) ? null : GameDatabase.GetAppearancePiece(hairId);

        foreach (AppearancePieceData piece in GameDatabase.AppearancePieces)
        {
            if (piece.Slot != AppearanceSlot.Hair || piece.Gender != gender) continue;
            bool isSelected = piece == selected;
            bool hasVariant = !string.IsNullOrEmpty(piece.HeadwearVariantNodeName);
            bool useVariant = isSelected && headwearActive && hasVariant;

            if (nodes.TryGetValue(piece.NodeName, out GameObject normalNode) && normalNode != null)
            {
                normalNode.SetActive(isSelected && !useVariant);
            }
            if (hasVariant && nodes.TryGetValue(piece.HeadwearVariantNodeName, out GameObject variantNode) && variantNode != null)
            {
                variantNode.SetActive(useVariant);
            }
        }
    }

    // Free multi-select: every catalog entry's node is active exactly when
    // its Id is present in the joined list, independent of every other
    // entry - unlike ApplySlot, there's no "exactly one selected" rule here.
    private static void ApplyAccessories(Dictionary<string, GameObject> nodes, AppearanceGender gender, string accessoryIds)
    {
        HashSet<string> selected = ParseAccessoryIds(accessoryIds);
        foreach (AppearanceAccessoryData accessory in GameDatabase.AppearanceAccessories)
        {
            if (accessory.Gender != gender) continue;
            if (nodes.TryGetValue(accessory.NodeName, out GameObject node) && node != null)
            {
                node.SetActive(selected.Contains(accessory.Id));
            }
        }
    }

    public static HashSet<string> ParseAccessoryIds(string accessoryIds)
    {
        HashSet<string> result = new HashSet<string>();
        if (string.IsNullOrEmpty(accessoryIds)) return result;
        foreach (string id in accessoryIds.Split(';'))
        {
            if (!string.IsNullOrEmpty(id)) result.Add(id);
        }
        return result;
    }

    private void ApplyHeadwear(Dictionary<string, GameObject> nodes, bool female, string headwearId)
    {
        if (!HeadwearNeedsRebuild(appliedFemale, appliedHeadwearId, female, headwearId)) return;
        appliedFemale = female;
        appliedHeadwearId = headwearId;

        if (headwearInstance != null)
        {
            Object.Destroy(headwearInstance);
            headwearInstance = null;
        }

        AppearanceHeadwearData headwear = string.IsNullOrEmpty(headwearId) ? null : GameDatabase.GetAppearanceHeadwear(headwearId);
        if (headwear == null || headwear.Prefab == null) return;
        if (!nodes.TryGetValue(HeadBoneName, out GameObject headBone) || headBone == null) return;

        headwearInstance = Object.Instantiate(headwear.Prefab, headBone.transform);
        // These standalone headwear prefabs weren't authored with their
        // pivot at their own geometric center - Vector3.zero/identity left
        // them floating well in front of the face. This offset was found
        // empirically (placed under head_joint, nudged until it sat right)
        // and confirmed consistent across multiple different pieces, so one
        // shared constant covers all of them rather than needing per-piece
        // tuning.
        headwearInstance.transform.localPosition = HeadwearLocalPosition;
        headwearInstance.transform.localRotation = Quaternion.Euler(HeadwearLocalEulerAngles);
    }

    private static readonly Vector3 HeadwearLocalPosition = new Vector3(1.771f, 0f, 0.014f);
    private static readonly Vector3 HeadwearLocalEulerAngles = new Vector3(-87.473f, 16.501f, 73.199f);

    // A gender change needs a rebuild even if the Id itself didn't change -
    // the old instance is parented under the now-inactive rig's head bone,
    // not the newly active one. Pulled out as its own pure, public method so
    // this branching is directly unit-testable without needing a real
    // headwear prefab/instance - see CharacterAppearanceApplierTests.
    public static bool HeadwearNeedsRebuild(bool? previousFemale, string previousHeadwearId, bool female, string headwearId)
    {
        return previousFemale != female || previousHeadwearId != headwearId;
    }

    // renderer.sharedMaterial (singular) only ever reads/writes a
    // renderer's FIRST material slot - several pieces in this pack bake a
    // skin-toned region and a clothing-toned region into two submeshes of
    // the same renderer (e.g. Element 0 "Objects", Element 1 "Body"), so
    // this has to walk the full sharedMaterials array and reassign/tint
    // each slot independently, or a second slot is silently never swapped
    // away from the shader-graph default at all.
    private static void ApplyColor(Transform root, string[] matchNames, Material targetMaterial, Texture2D[] textures, int index)
    {
        if (targetMaterial == null || textures == null || index < 0 || index >= textures.Length) return;
        Texture2D texture = textures[index];
        if (texture == null) return;

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null || System.Array.IndexOf(matchNames, materials[i].name) < 0) continue;
                if (materials[i] != targetMaterial)
                {
                    materials[i] = targetMaterial;
                    changed = true;
                }
                renderer.GetPropertyBlock(block, i);
                block.SetTexture("_BaseMap", texture);
                renderer.SetPropertyBlock(block, i);
            }
            if (changed) renderer.sharedMaterials = materials;
        }
    }
}
