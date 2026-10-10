using UnityEngine;

// Local-only, non-networked live preview of the character currently being
// built in the Character Creation menu panel. Deliberately a separate
// Sidekick build from the live networked player (CharacterAppearance)
// rather than a reuse of it - this menu is used both pregame, before any
// NetworkObject exists at all, and from the in-game Escape menu, and a
// single implementation that doesn't depend on a network session covers
// both without repositioning/hijacking the real in-world character while
// the menu is open. The stage sits far from the playable area (see the
// Editor setup notes) and is captured by its own Camera into a
// RenderTexture that MainMenu displays.
public class CharacterPreview : MonoBehaviour
{
    // Where the built character is parented (the stage's own child, at the
    // spot the camera is framed on).
    [SerializeField] private Transform stageRoot;
    // Same controller as the live player's (CharacterIdle_M) so the preview
    // idles instead of standing in bind pose - its parameters are never
    // driven here, so it just plays the out-of-combat idle.
    [SerializeField] private RuntimeAnimatorController animatorController;

    private class HeldModel
    {
        public GameObject Instance;
        public string AppliedItemId;
        public Transform AppliedSocket;
    }

    private BuiltSidekickCharacter built;
    private SidekickSelection builtSelection;
    // Not reset on a rebuild so rotating to a preferred viewing angle
    // survives changing a preset.
    private float rotationY;

    private readonly HeldModel mainHandPreview = new HeldModel();
    private readonly HeldModel offHandPreview = new HeldModel();

    private Transform socketCacheRig;
    private Transform cachedRightSocket;
    private Transform cachedLeftSocket;

    // Called every frame the Character Creation panel is drawn - cheap
    // unless the selection actually changed, in which case the character
    // is rebuilt (no in-place update exists in Sidekick's API).
    public void Refresh(PlayerProfile profile)
    {
        SidekickSelection selection = SidekickSelection.FromProfile(profile);
        if (built == null || !selection.Equals(builtSelection))
        {
            SidekickCharacterBuilder builder = SidekickCharacterBuilder.Shared;
            if (!builder.IsReady) return;

            built?.Destroy(false);
            built = builder.Build(selection, stageRoot, animatorController, "SidekickPreview");
            builtSelection = selection;
            socketCacheRig = null;
        }

        if (built != null) built.Root.transform.localRotation = Quaternion.Euler(0f, rotationY, 0f);
    }

    // Positive turns right, negative turns left - see MainMenu's rotate
    // buttons on the Character Creation panel.
    public void Rotate(float degrees)
    {
        rotationY += degrees;
    }

    // Called every frame from MainMenu.Update() - fed null whenever the
    // Equipment panel isn't the active panel, which tears down any
    // leftover instance before any other panel (notably Character
    // Creation, via Refresh()) can render a frame with it still attached.
    // Refresh() itself never shows weapons.
    public void RefreshWeapons(PlayerProfile profile)
    {
        Transform activeRig = profile != null && built != null ? built.Root.transform : null;

        UpdateHeldModel(mainHandPreview, activeRig, profile?.GetEquipment(EquipmentSlot.MainHand), slotIsRightHand: true);
        UpdateHeldModel(offHandPreview, activeRig, profile?.GetEquipment(EquipmentSlot.OffHand), slotIsRightHand: false);
    }

    private void UpdateHeldModel(HeldModel held, Transform activeRig, ItemData item, bool slotIsRightHand)
    {
        string itemId = item != null ? item.Id : null;
        GameObject prefab = item != null ? item.WeaponModelPrefab : null;
        Transform socket = prefab != null
            ? GetEquipSocket(activeRig, CharacterWeaponVisual.ResolveRightHand(item.AttachProfile, slotIsRightHand))
            : null;

        if (!CharacterWeaponVisual.NeedsRebuild(held.AppliedItemId, held.AppliedSocket, itemId, socket)) return;
        if (prefab != null && socket == null) return;

        if (held.Instance != null) Destroy(held.Instance);
        held.Instance = prefab != null ? Instantiate(prefab, socket) : null;
        held.AppliedItemId = itemId;
        held.AppliedSocket = socket;
        if (held.Instance != null)
        {
            CharacterWeaponVisual.ApplyAttachment(held.Instance.transform, prefab, item.AttachProfile, socket, activeRig);
        }
    }

    // Mirrors CharacterAppearance.GetEquipSocket's own caching/fallback,
    // just rig-root-in instead of reading a live
    // ActiveRigRoot/ActiveAnimator - there's no NetworkBehaviour here.
    private Transform GetEquipSocket(Transform activeRig, bool rightHand)
    {
        if (activeRig == null) return null;

        if (socketCacheRig != activeRig)
        {
            socketCacheRig = activeRig;
            cachedRightSocket = CharacterAppearance.FindEquipSocket(activeRig, CharacterAppearance.RightEquipSocketName);
            cachedLeftSocket = CharacterAppearance.FindEquipSocket(activeRig, CharacterAppearance.LeftEquipSocketName);
        }

        Transform socket = rightHand ? cachedRightSocket : cachedLeftSocket;
        if (socket != null) return socket;
        Animator animator = activeRig.GetComponentInChildren<Animator>();
        return animator != null ? animator.GetBoneTransform(rightHand ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand) : null;
    }

    private void OnDestroy()
    {
        if (mainHandPreview.Instance != null) Destroy(mainHandPreview.Instance);
        if (offHandPreview.Instance != null) Destroy(offHandPreview.Instance);
        built?.Destroy(false);
        built = null;
    }
}
