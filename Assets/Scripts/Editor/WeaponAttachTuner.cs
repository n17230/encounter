using UnityEditor;
using UnityEngine;

// Edit-mode tuning for WeaponAttachProfiles - no Play mode needed. Spawns a
// throwaway copy of the player rig, attaches the picked item's model to the
// same equip socket (and through the same CharacterWeaponVisual
// .ApplyAttachment math) the game uses, lets you position it with the normal
// Scene-view Move/Rotate/Scale gizmos, then writes the result back into the
// item's profile asset. Profiles are shared per weapon category, so saving
// once fixes every item that points at that profile.
public class WeaponAttachTuner : EditorWindow
{
    private const string RigPrefabPath = "Assets/Prefabs/Player/CharacterRig_M.prefab";
    private const string PreviewName = "__WeaponAttachPreview";

    private ItemData item;
    // The item the live preview was actually spawned for - Save/Reset act
    // on THIS, never on whatever the Item field currently shows, so
    // re-picking a different item after spawning can't write one item's
    // preview into another item's (shared) profile.
    private ItemData previewItem;
    private GameObject previewRig;
    private GameObject previewModel;
    private Transform previewSocket;
    // Which socket the live preview is actually attached to right now - starts
    // at whatever the profile/slot resolve to, but Switch Hand can flip it for
    // this session without touching the profile until Save To Profile.
    private bool previewIsRightHand;
    private bool handOverridden;
    private Vector2 scrollPosition;

    [MenuItem("Encounter/Weapon Attach Tuner")]
    private static void Open()
    {
        GetWindow<WeaponAttachTuner>("Weapon Attach Tuner");
    }

    private void OnDestroy()
    {
        ClearPreview();
    }

    private void OnGUI()
    {
        // try/finally, not an early return, so EndScrollView always runs even
        // though DrawContents below has its own early returns for "nothing to
        // show yet" states - an unbalanced Begin/EndScrollView throws on the
        // next OnGUI call.
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        try
        {
            DrawContents();
        }
        finally
        {
            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawContents()
    {
        EditorGUILayout.HelpBox(
            "1. Pick an item and Spawn Preview.\n" +
            "2. Move/rotate the selected model in the Scene view until it sits in the hand (scale is uniform - only X is kept).\n" +
            "   Switch Hand flips which socket it's attached to if it looks wrong (e.g. a bow's off-hand grip).\n" +
            "3. Save To Profile - every item sharing that profile picks it up.",
            MessageType.Info);

        item = (ItemData)EditorGUILayout.ObjectField("Item", item, typeof(ItemData), false);
        if (item == null) return;

        if (item.WeaponModelPrefab == null)
        {
            EditorGUILayout.HelpBox("This item has no Weapon Model Prefab assigned.", MessageType.Warning);
            return;
        }

        if (item.AttachProfile == null)
        {
            EditorGUILayout.HelpBox(
                "This item has no Attach Profile. Assign its category's profile (Assets/Data/WeaponAttachProfiles), " +
                "or create a new one via Create > Encounter > Weapon Attach Profile for a new category.",
                MessageType.Warning);
            return;
        }

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("Profile (shared)", item.AttachProfile, typeof(WeaponAttachProfile), false);
        }

        if (GUILayout.Button("Spawn Preview")) SpawnPreview();

        bool previewMatchesItem = previewModel != null && previewItem == item;
        if (previewModel != null && !previewMatchesItem)
        {
            EditorGUILayout.HelpBox(
                $"The preview in the scene is for {previewItem.ItemName}, not the item picked above - Spawn Preview again to tune this one.",
                MessageType.Warning);
        }

        using (new EditorGUI.DisabledScope(!previewMatchesItem))
        {
            if (GUILayout.Button(previewIsRightHand ? "Switch To Left Hand" : "Switch To Right Hand")) SwitchHand();
            if (GUILayout.Button("Save To Profile")) SaveToProfile();
            if (GUILayout.Button("Reset Preview To Saved Profile")) ApplyProfileToPreview();
        }

        if (GUILayout.Button("Clear Preview")) ClearPreview();
    }

    private void SpawnPreview()
    {
        ClearPreview();

        GameObject rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
        if (rigPrefab == null)
        {
            Debug.LogWarning($"[WeaponAttachTuner] Could not load the rig prefab at {RigPrefabPath}.");
            return;
        }

        previewRig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
        previewRig.name = PreviewName;
        // Never written into the open scene - this is a scratch object.
        previewRig.hideFlags = HideFlags.DontSave;
        if (SceneView.lastActiveSceneView != null) previewRig.transform.position = SceneView.lastActiveSceneView.pivot;

        bool slotIsRightHand = item.Slot != EquipmentSlot.OffHand;
        bool rightHand = CharacterWeaponVisual.ResolveRightHand(item.AttachProfile, slotIsRightHand);
        string socketName = rightHand ? CharacterAppearance.RightEquipSocketName : CharacterAppearance.LeftEquipSocketName;
        previewSocket = CharacterAppearance.FindEquipSocket(previewRig.transform, socketName);
        if (previewSocket == null)
        {
            Debug.LogWarning($"[WeaponAttachTuner] The rig has no '{socketName}' - nothing to attach to.");
            ClearPreview();
            return;
        }

        previewIsRightHand = rightHand;
        handOverridden = false;
        previewItem = item;
        previewModel = (GameObject)PrefabUtility.InstantiatePrefab(item.WeaponModelPrefab, previewSocket);
        ApplyProfileToPreview();

        Selection.activeGameObject = previewModel;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
    }

    private void ApplyProfileToPreview()
    {
        if (previewModel == null || previewItem == null) return;
        CharacterWeaponVisual.ApplyAttachment(previewModel.transform, previewItem.WeaponModelPrefab, previewItem.AttachProfile, previewSocket, previewRig.transform);
    }

    // Re-parents the live preview onto the opposite equip socket for this
    // session, without touching the profile asset - lets you compare both
    // hands before committing to one with Save To Profile.
    private void SwitchHand()
    {
        if (previewModel == null || previewRig == null) return;

        bool wantRightHand = !previewIsRightHand;
        string socketName = wantRightHand ? CharacterAppearance.RightEquipSocketName : CharacterAppearance.LeftEquipSocketName;
        Transform newSocket = CharacterAppearance.FindEquipSocket(previewRig.transform, socketName);
        if (newSocket == null)
        {
            Debug.LogWarning($"[WeaponAttachTuner] The rig has no '{socketName}' - nothing to attach to.");
            return;
        }

        previewIsRightHand = wantRightHand;
        handOverridden = true;
        previewSocket = newSocket;
        previewModel.transform.SetParent(previewSocket, false);
        ApplyProfileToPreview();

        Selection.activeGameObject = previewModel;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
    }

    // Exact inverse of CharacterWeaponVisual.ApplyAttachment, so what gets
    // saved reproduces this preview 1:1 in game. Scale is uniform (the
    // profile stores one float) - only the X axis of a gizmo scale is kept.
    private void SaveToProfile()
    {
        if (previewModel == null || previewItem == null || previewItem.AttachProfile == null) return;

        WeaponAttachProfile profile = previewItem.AttachProfile;
        Transform model = previewModel.transform;

        float authoredScale = previewItem.WeaponModelPrefab.transform.localScale.x
            * CharacterWeaponVisual.InternalScaleCompensation(previewSocket, previewRig.transform);

        Undo.RecordObject(profile, "Save Weapon Attach Profile");
        profile.Position = model.localPosition;
        profile.Rotation = model.localEulerAngles;
        profile.Scale = Mathf.Approximately(authoredScale, 0f) ? 1f : model.localScale.x / authoredScale;
        // Only touch Hand if Switch Hand was actually used this session -
        // otherwise leave whatever the profile already had (including
        // SlotDefault) alone.
        if (handOverridden) profile.Hand = previewIsRightHand ? AttachHand.Right : AttachHand.Left;
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        Debug.Log($"[WeaponAttachTuner] Saved {profile.name}: position {profile.Position}, rotation {profile.Rotation}, scale {profile.Scale}, hand {profile.Hand}.");
    }

    private void ClearPreview()
    {
        // By name as well as by reference - a script reload drops this
        // window's fields but not the DontSave object itself.
        GameObject stale = previewRig != null ? previewRig : GameObject.Find(PreviewName);
        if (stale != null) DestroyImmediate(stale);
        previewRig = null;
        previewModel = null;
        previewSocket = null;
        previewItem = null;
        previewIsRightHand = false;
        handOverridden = false;
    }
}
