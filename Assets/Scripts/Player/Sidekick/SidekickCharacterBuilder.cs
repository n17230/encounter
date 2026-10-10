using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Synty.SidekickCharacters.API;
using Synty.SidekickCharacters.Database;
using Synty.SidekickCharacters.Database.DTO;
using Synty.SidekickCharacters.Enums;
using Synty.SidekickCharacters.Utils;
using UnityEngine;

// One character built from a SidekickSelection: the root Sidekick produced
// (skeleton + one SkinnedMeshRenderer per part, Animator with the humanoid
// SK_BaseModel avatar) plus the material/texture copies it was colored
// with, all destroyed together.
public class BuiltSidekickCharacter
{
    public GameObject Root;
    public Animator Animator;
    public Material Material;
    public Texture2D[] Textures;

    public void Destroy(bool immediate)
    {
        // Sidekick copies every part's Mesh per build (Combiner ->
        // MeshUtils.CopyMesh); destroying the GameObject doesn't free a
        // renderer's sharedMesh, so each rebuild would otherwise leak them
        // for the whole session.
        if (Root != null)
        {
            foreach (SkinnedMeshRenderer renderer in Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                DestroyObject(renderer.sharedMesh, immediate);
            }
        }
        DestroyObject(Root, immediate);
        DestroyObject(Material, immediate);
        if (Textures != null)
        {
            foreach (Texture2D texture in Textures) DestroyObject(texture, immediate);
        }
        Root = null;
        Animator = null;
        Material = null;
        Textures = null;
    }

    private static void DestroyObject(UnityEngine.Object obj, bool immediate)
    {
        if (obj == null) return;
        if (immediate) UnityEngine.Object.DestroyImmediate(obj);
        else UnityEngine.Object.Destroy(obj);
    }
}

// The one Sidekick runtime for this process (client/Editor only - never
// touched on a headless server): opens the database, loads the part
// library once, and builds characters from selections the way Sidekick's
// own RuntimePresetDemo does. Each build gets its own copy of the base
// material AND its six maps, because SidekickRuntime.UpdateColor paints
// preset colors straight into the material's textures with SetPixel -
// shared, every player would recolor every other player. Works in Edit
// mode too (the Weapon Attach Tuner builds its preview through this).
public class SidekickCharacterBuilder
{
    private const string BaseModelResource = "Meshes/SK_BaseModel";
    private const string BaseMaterialResource = "Materials/M_BaseMaterial";

    private static readonly string[] MapProperties =
    {
        "_ColorMap", "_MetallicMap", "_SmoothnessMap", "_ReflectionMap", "_EmissionMap", "_OpacityMap",
    };

    private static SidekickCharacterBuilder shared;
    public static SidekickCharacterBuilder Shared => shared ??= new SidekickCharacterBuilder();

    private readonly DatabaseManager db;
    private readonly SidekickRuntime runtime;
    private readonly Material baseMaterial;

    public SidekickPresetCatalog Catalog { get; }
    public bool IsReady => runtime != null && Catalog != null;

    private SidekickCharacterBuilder()
    {
        GameObject baseModel = Resources.Load<GameObject>(BaseModelResource);
        baseMaterial = Resources.Load<Material>(BaseMaterialResource);
        if (baseModel == null || baseMaterial == null)
        {
            Debug.LogError($"[SidekickCharacterBuilder] Missing Resources/{BaseModelResource} or Resources/{BaseMaterialResource} - is the Sidekick Characters package installed?");
            return;
        }

        db = new DatabaseManager();
        runtime = new SidekickRuntime(baseModel, baseMaterial, null, db);
        if (db.GetCurrentDbConnection() == null)
        {
            // GetDbConnection already logged why (a player build without the
            // synced Resources/Database/Side_Kick_Data.bytes).
            runtime = null;
            return;
        }

        // Async in signature only - the part library is loaded from
        // Resources, so this has already completed by the time it returns.
        Task populate = SidekickRuntime.PopulateToolData(runtime);
        if (!populate.IsCompleted) Debug.LogWarning("[SidekickCharacterBuilder] Sidekick part library is still loading - the first build may be incomplete.");

        Catalog = new SidekickPresetCatalog(db);
    }

    // Returns null if nothing could be built (empty catalog, missing part
    // models). The result is parented under parent at local identity.
    public BuiltSidekickCharacter Build(SidekickSelection selection, Transform parent, RuntimeAnimatorController controller, string name)
    {
        if (!IsReady) return null;
        if (parent == null)
        {
            // An unwired characterRoot/stageRoot would otherwise leave the
            // character at the world origin, following nobody.
            Debug.LogWarning($"[SidekickCharacterBuilder] No parent for '{name}' - wire the Character Root / Stage Root field in the Editor.");
            return null;
        }

        SidekickSelection resolved = Catalog.Resolve(selection);

        List<SkinnedMeshRenderer> parts = new List<SkinnedMeshRenderer>();
        CollectParts(PartGroup.Head, resolved.Head, parts);
        CollectParts(PartGroup.UpperBody, resolved.UpperBody, parts);
        CollectParts(PartGroup.LowerBody, resolved.LowerBody, parts);
        if (parts.Count == 0)
        {
            Debug.LogWarning("[SidekickCharacterBuilder] No part models resolved for the selection - nothing built.");
            return null;
        }

        SidekickBodyShapePreset bodyShape = Catalog.GetBodyShape(resolved.BodyShape);
        runtime.BodyTypeBlendValue = bodyShape != null ? bodyShape.BodyType : 0f;
        runtime.BodySizeHeavyBlendValue = bodyShape != null && bodyShape.BodySize > 0 ? bodyShape.BodySize : 0f;
        runtime.BodySizeSkinnyBlendValue = bodyShape != null && bodyShape.BodySize < 0 ? -bodyShape.BodySize : 0f;
        runtime.MusclesBlendValue = bodyShape != null ? bodyShape.Musculature : 0f;

        Material material = new Material(baseMaterial);
        List<Texture2D> textures = new List<Texture2D>();
        foreach (string property in MapProperties)
        {
            if (!material.HasProperty(property)) continue;
            if (!(material.GetTexture(property) is Texture2D source)) continue;
            Texture2D copy = UnityEngine.Object.Instantiate(source);
            material.SetTexture(property, copy);
            textures.Add(copy);
        }
        runtime.CurrentMaterial = material;

        ApplyColorPreset(ColorGroup.Species, resolved.ColorSpecies);
        ApplyColorPreset(ColorGroup.Outfits, resolved.ColorOutfits);
        ApplyColorPreset(ColorGroup.Attachments, resolved.ColorAttachments);
        ApplyColorPreset(ColorGroup.Materials, resolved.ColorMaterials);
        ApplyColorPreset(ColorGroup.Elements, resolved.ColorElements);

        GameObject root = runtime.CreateCharacter(name, parts, false, true);
        root.transform.SetParent(parent, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        Animator animator = root.GetComponent<Animator>();
        if (animator != null && controller != null) animator.runtimeAnimatorController = controller;

        return new BuiltSidekickCharacter { Root = root, Animator = animator, Material = material, Textures = textures.ToArray() };
    }

    private void CollectParts(PartGroup group, string presetName, List<SkinnedMeshRenderer> parts)
    {
        SidekickPartPreset preset = Catalog.GetPartPreset(group, presetName);
        if (preset == null) return;

        foreach (SidekickPartPresetRow row in SidekickPartPresetRow.GetAllByPreset(db, preset))
        {
            if (string.IsNullOrEmpty(row.PartName)) continue;
            string typeName = CharacterPartTypeUtils.GetTypeNameFromShortcode(row.PartType);
            if (!Enum.TryParse(typeName, out CharacterPartType type)) continue;
            if (!runtime.MappedPartDictionary.TryGetValue(type, out Dictionary<string, SidekickPart> byName)) continue;
            if (!byName.TryGetValue(row.PartName, out SidekickPart part)) continue;

            GameObject model = part.GetPartModel();
            SkinnedMeshRenderer renderer = model != null ? model.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            if (renderer != null) parts.Add(renderer);
        }
    }

    private void ApplyColorPreset(ColorGroup group, string presetName)
    {
        SidekickColorPreset preset = Catalog.GetColorPreset(group, presetName);
        if (preset == null) return;

        foreach (SidekickColorPresetRow row in SidekickColorPresetRow.GetAllByPreset(db, preset))
        {
            SidekickColorRow colorRow = SidekickColorRow.CreateFromPresetColorRow(row);
            foreach (ColorType colorType in Enum.GetValues(typeof(ColorType)))
            {
                runtime.UpdateColor(colorType, colorRow);
            }
        }
    }
}
