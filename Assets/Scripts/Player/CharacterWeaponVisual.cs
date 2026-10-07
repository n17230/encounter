using Unity.Netcode;
using UnityEngine;

// Shows the equipped MainHand/OffHand item's 3D model (ItemData
// .WeaponModelPrefab) in the wearer's equip socket, for every client - not
// just the owner - reacting to CharacterEquipment's
// MainHandItemId/OffHandItemId (already synced to everyone). Not a
// NetworkObject itself: purely cosmetic, each client instantiates its own
// local copy, same approach PlayerAbilities.PlayCastVfxClientRpc already
// uses for cast VFX, just persistent instead of timed.
[RequireComponent(typeof(CharacterEquipment))]
[RequireComponent(typeof(CharacterAppearance))]
public class CharacterWeaponVisual : NetworkBehaviour
{
    private class HeldModel
    {
        public GameObject Instance;
        public string AppliedItemId;
        public Transform AppliedSocket;
        public ItemData Item;
    }

    private CharacterEquipment equipment;
    private CharacterAppearance appearance;
    private readonly HeldModel mainHand = new HeldModel();
    private readonly HeldModel offHand = new HeldModel();

    // The currently-instantiated MainHand model's own transform (e.g. the
    // bow) - for code that needs to attach something to the weapon itself
    // rather than a rig socket (e.g. PlayerAutoAttack's nocked-arrow
    // visual). Null if nothing's equipped or the model hasn't been
    // instantiated yet.
    public Transform MainHandModelTransform => mainHand.Instance != null ? mainHand.Instance.transform : null;

    private void Awake()
    {
        equipment = GetComponent<CharacterEquipment>();
        appearance = GetComponent<CharacterAppearance>();
    }

    public override void OnNetworkDespawn()
    {
        if (mainHand.Instance != null) Destroy(mainHand.Instance);
        if (offHand.Instance != null) Destroy(offHand.Instance);
    }

    private void Update()
    {
        // Purely cosmetic, invisible on a headless server - same guard
        // CharacterAppearance.Apply()/AuraGroundVisual use.
        if (!IsClient) return;

        UpdateHeldModel(mainHand, equipment.MainHandItemId.Value.ToString(), slotIsRightHand: true);
        UpdateHeldModel(offHand, equipment.OffHandItemId.Value.ToString(), slotIsRightHand: false);
    }

    // Polls every frame rather than only reacting to OnValueChanged - a
    // socket that isn't resolvable yet (rig not applied on the first frame
    // or two) just means "try again next frame", since the applied state is
    // only recorded once the attach actually happens, with no ordering
    // needed against CharacterAppearance's own OnNetworkSpawn.
    private void UpdateHeldModel(HeldModel held, string itemId, bool slotIsRightHand)
    {
        ItemData item = !string.IsNullOrEmpty(itemId) ? GameDatabase.GetItem(itemId) : null;
        GameObject prefab = item != null ? item.WeaponModelPrefab : null;
        Transform socket = prefab != null ? appearance.GetEquipSocket(ResolveRightHand(item.AttachProfile, slotIsRightHand)) : null;

        if (NeedsRebuild(held.AppliedItemId, held.AppliedSocket, itemId, socket))
        {
            if (prefab != null && socket == null) return;

            if (held.Instance != null) Destroy(held.Instance);
            held.Instance = prefab != null ? Instantiate(prefab, socket) : null;
            held.Item = item;
            held.AppliedItemId = itemId;
            held.AppliedSocket = socket;
            if (held.Instance != null) ApplyAttachment(held.Instance.transform, prefab, item.AttachProfile, socket, appearance.ActiveRigRoot);
            return;
        }

#if UNITY_EDITOR
        // Live tuning: editing a WeaponAttachProfile asset in the Inspector
        // during Play mode shows immediately (and ScriptableObject edits
        // made in Play mode persist afterwards).
        if (held.Instance != null && held.Item != null)
        {
            ApplyAttachment(held.Instance.transform, held.Item.WeaponModelPrefab, held.Item.AttachProfile, held.AppliedSocket, appearance.ActiveRigRoot);
        }
#endif
    }

    // A changed socket needs a rebuild even if the item didn't change - a
    // gender switch swaps which rig is live, and the old instance is
    // parented under the now-inactive rig's socket (same case
    // CharacterAppearanceApplier.HeadwearNeedsRebuild covers for headwear).
    // Pure + public so it's directly unit-testable.
    public static bool NeedsRebuild(string appliedItemId, Object appliedSocket, string itemId, Object socket)
    {
        return appliedItemId != itemId || appliedSocket != socket;
    }

    public static bool ResolveRightHand(WeaponAttachProfile profile, bool slotIsRightHand)
    {
        if (profile == null || profile.Hand == AttachHand.SlotDefault) return slotIsRightHand;
        return profile.Hand == AttachHand.Right;
    }

    // The one place a held model's socket-local transform is computed -
    // shared with the Weapon Attach Tuner so what's tuned in the Editor is
    // exactly what the game shows. Scale cancels the skeleton's *internal*
    // bone scaling (socket vs. rig root) so a model renders at its authored
    // size without a per-profile fudge factor, while still following any
    // scale applied to the character as a whole.
    public static void ApplyAttachment(Transform instance, GameObject prefab, WeaponAttachProfile profile, Transform socket, Transform rigRoot)
    {
        instance.localPosition = profile != null ? profile.Position : Vector3.zero;
        instance.localRotation = profile != null ? Quaternion.Euler(profile.Rotation) : Quaternion.identity;
        instance.localScale = prefab.transform.localScale * (profile != null ? profile.Scale : 1f) * InternalScaleCompensation(socket, rigRoot);
    }

    public static float InternalScaleCompensation(Transform socket, Transform rigRoot)
    {
        if (socket == null || rigRoot == null) return 1f;
        float socketScale = socket.lossyScale.x;
        return Mathf.Approximately(socketScale, 0f) ? 1f : rigRoot.lossyScale.x / socketScale;
    }
}
