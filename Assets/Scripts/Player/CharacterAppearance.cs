using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// Purely cosmetic character appearance - Top/Bottom/face-feature/hair
// pieces, freely-combined accessories, headwear, gender/base rig, and two
// recolor swatches. No gameplay effect at all, so unlike CharacterEquipment's
// equipment sync this is owner-authoritative: there's nothing to cheat by lying
// about your own appearance, the same reasoning this project already
// applies to movement. The actual apply-to-rig logic lives in
// CharacterAppearanceApplier (shared with the menu's local-only
// CharacterPreview); this class is just the networked feed for it.
public class CharacterAppearance : NetworkBehaviour
{
    // Both gender rigs are pre-placed as children (see the project's plan
    // notes) and toggled active/inactive rather than instantiated on
    // demand, to avoid juggling Animator setup at runtime.
    [SerializeField] private Transform maleRigRoot;
    [SerializeField] private Transform femaleRigRoot;

    public readonly NetworkVariable<bool> IsFemale =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public readonly NetworkVariable<FixedString32Bytes> TopId =
        new NetworkVariable<FixedString32Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public readonly NetworkVariable<FixedString32Bytes> BottomId =
        new NetworkVariable<FixedString32Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public readonly NetworkVariable<FixedString32Bytes> HeadwearId =
        new NetworkVariable<FixedString32Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public readonly NetworkVariable<FixedString32Bytes> EyebrowsId =
        new NetworkVariable<FixedString32Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public readonly NetworkVariable<FixedString32Bytes> EyesId =
        new NetworkVariable<FixedString32Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public readonly NetworkVariable<FixedString32Bytes> MouthId =
        new NetworkVariable<FixedString32Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public readonly NetworkVariable<FixedString32Bytes> HairId =
        new NetworkVariable<FixedString32Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public readonly NetworkVariable<FixedString32Bytes> FacialHairId =
        new NetworkVariable<FixedString32Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    // ';'-joined list of AppearanceAccessoryData Ids - up to ~16 short ids
    // per gender, comfortably within 512 bytes.
    public readonly NetworkVariable<FixedString512Bytes> AccessoryIds =
        new NetworkVariable<FixedString512Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public readonly NetworkVariable<int> BodyColorIndex =
        new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public readonly NetworkVariable<int> ObjectColorIndex =
        new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private CharacterAppearanceApplier applier;
    private NetworkAnimator networkAnimator;

    // Whichever gender's rig is currently active - re-resolved every Apply()
    // since which rig that is can change at runtime (a gender switch mid-
    // session). Movement/ability code (PlayerMovement, PlayerAbilities)
    // reads this instead of caching an Animator themselves, since only this
    // class knows which of the two rigs is currently live.
    public Animator ActiveAnimator { get; private set; }

    // The active rig's own root Transform (maleRigRoot or femaleRigRoot) -
    // distinct from this component's own transform (the network-replicated
    // player root, also the camera's parent and what movement/combat-facing
    // read). Purely-visual rotations (see PlayerMovement's strafe-turn)
    // belong on this transform instead, so they never affect movement
    // direction, the camera, or FacingCone checks.
    public Transform ActiveRigRoot { get; private set; }

    private void Awake()
    {
        applier = new CharacterAppearanceApplier(maleRigRoot, femaleRigRoot);
        networkAnimator = GetComponent<NetworkAnimator>();

        // NetworkAnimator.Awake() only builds its parameter/state sync
        // arrays if its own Animator field is already non-null at that exact
        // moment - if it's still null there, sync is silently broken for
        // this instance forever, and assigning .Animator later (see Apply()
        // below) does NOT retroactively fix it. Unity doesn't guarantee
        // component Awake() order, so this assignment here is only a
        // best-effort - the NetworkAnimator's Animator field must also be
        // pre-wired to either rig's Animator in the Editor (both
        // CharacterIdle_M/F controllers are kept parameter-for-parameter
        // identical for exactly this reason - either one is a valid initial
        // target) so it's guaranteed non-null before any Awake() runs at all.
        if (networkAnimator != null && networkAnimator.Animator == null && maleRigRoot != null)
        {
            networkAnimator.Animator = maleRigRoot.GetComponentInChildren<Animator>(true);
        }
    }

    // Right-hand bone of whichever rig is currently active, for effects that
    // should visually originate from/track the hand (see PlayerAbilities
    // .PlayCastVfxClientRpc, CharacterWeaponVisual) - both rigs share the
    // same Humanoid avatar, so this resolves correctly regardless of gender
    // without needing to know either rig's actual joint names.
    public Transform GetRightHandBone()
    {
        return ActiveAnimator != null ? ActiveAnimator.GetBoneTransform(HumanBodyBones.RightHand) : null;
    }

    // The rig's own purpose-built held-item sockets (children of the wrist
    // joints, placed in the palm) - what CharacterWeaponVisual parents
    // weapon/shield models to. Looked up by name since they're extra
    // joints outside the Humanoid mapping; falls back to the Humanoid hand
    // bone for a rig that lacks them. Cached per active rig - a gender
    // switch swaps which rig is live, so the cache is keyed on that.
    public const string RightEquipSocketName = "R_equip_joint";
    public const string LeftEquipSocketName = "L_equip_joint";

    private Transform socketCacheRig;
    private Transform cachedRightSocket;
    private Transform cachedLeftSocket;

    public Transform GetEquipSocket(bool rightHand)
    {
        if (ActiveRigRoot == null) return null;

        if (socketCacheRig != ActiveRigRoot)
        {
            socketCacheRig = ActiveRigRoot;
            cachedRightSocket = FindEquipSocket(ActiveRigRoot, RightEquipSocketName);
            cachedLeftSocket = FindEquipSocket(ActiveRigRoot, LeftEquipSocketName);
        }

        Transform socket = rightHand ? cachedRightSocket : cachedLeftSocket;
        if (socket != null) return socket;
        if (ActiveAnimator == null) return null;
        return ActiveAnimator.GetBoneTransform(rightHand ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
    }

    // Static + rig-root-in so the Weapon Attach Tuner (Editor, no spawned
    // player) resolves sockets exactly the way the game does.
    public static Transform FindEquipSocket(Transform rigRoot, string socketName)
    {
        foreach (Transform child in rigRoot.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == socketName) return child;
        }
        return null;
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            PushProfileToNetworkVariables();
            MainMenu.Closed += PushProfileToNetworkVariables;
        }

        IsFemale.OnValueChanged += (previous, current) => Apply();
        TopId.OnValueChanged += (previous, current) => Apply();
        BottomId.OnValueChanged += (previous, current) => Apply();
        HeadwearId.OnValueChanged += (previous, current) => Apply();
        EyebrowsId.OnValueChanged += (previous, current) => Apply();
        EyesId.OnValueChanged += (previous, current) => Apply();
        MouthId.OnValueChanged += (previous, current) => Apply();
        HairId.OnValueChanged += (previous, current) => Apply();
        FacialHairId.OnValueChanged += (previous, current) => Apply();
        AccessoryIds.OnValueChanged += (previous, current) => Apply();
        BodyColorIndex.OnValueChanged += (previous, current) => Apply();
        ObjectColorIndex.OnValueChanged += (previous, current) => Apply();

        Apply();
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner) MainMenu.Closed -= PushProfileToNetworkVariables;
        applier.Cleanup();
    }

    private void PushProfileToNetworkVariables()
    {
        PlayerProfile profile = ProfileStore.Current;
        IsFemale.Value = profile.AppearanceIsFemale;
        TopId.Value = profile.AppearanceTopId;
        BottomId.Value = profile.AppearanceBottomId;
        HeadwearId.Value = profile.AppearanceHeadwearId;
        EyebrowsId.Value = profile.AppearanceEyebrowsId;
        EyesId.Value = profile.AppearanceEyesId;
        MouthId.Value = profile.AppearanceMouthId;
        HairId.Value = profile.AppearanceHairId;
        FacialHairId.Value = profile.AppearanceFacialHairId;
        AccessoryIds.Value = profile.AppearanceAccessoryIds;
        BodyColorIndex.Value = profile.AppearanceBodyColorIndex;
        ObjectColorIndex.Value = profile.AppearanceObjectColorIndex;
    }

    private void Apply()
    {
        bool female = IsFemale.Value;

        // Which gender's rig GameObject is active must be decided on the
        // server too, not just on clients - Unity never evaluates an
        // Animator (or anything else) on an inactive GameObject, so without
        // this the server's rig would just sit at whichever gender the
        // prefab happens to default to, frozen at rest pose, regardless of
        // IsFemale. NetworkAnimator's own parameter/crossfade sync
        // (SendTo.NotAuthority) already drives that now-active server-side
        // Animator to match the owner's real pose, which is what lets
        // server-side code ask "where is this character's hand right now"
        // (e.g. PlayerAbilities spawning a projectile at GetRightHandBone())
        // and get a meaningful answer instead of always null/frozen.
        // applier.Apply() below redundantly re-does this same toggle on a
        // client - harmless, since setting a GameObject active to what it
        // already is is a no-op.
        if (maleRigRoot != null) maleRigRoot.gameObject.SetActive(!female);
        if (femaleRigRoot != null) femaleRigRoot.gameObject.SetActive(female);

        // The rest of the cosmetic work (clothing meshes, materials,
        // recolors) is invisible to anyone on a headless dedicated server -
        // skip it there, same as ArcaneShieldVisual/EffectOverheadVisual/
        // AuraGroundVisual already do, rather than doing renderer scans and
        // MaterialPropertyBlock writes for every connected player for
        // nothing.
        if (IsClient)
        {
            applier.Apply(new AppearanceSelection
            {
                Female = female,
                TopId = TopId.Value.ToString(),
                BottomId = BottomId.Value.ToString(),
                HeadwearId = HeadwearId.Value.ToString(),
                EyebrowsId = EyebrowsId.Value.ToString(),
                EyesId = EyesId.Value.ToString(),
                MouthId = MouthId.Value.ToString(),
                HairId = HairId.Value.ToString(),
                FacialHairId = FacialHairId.Value.ToString(),
                AccessoryIds = AccessoryIds.Value.ToString(),
                BodyColorIndex = BodyColorIndex.Value,
                ObjectColorIndex = ObjectColorIndex.Value,
            });
        }

        // The active rig (and therefore its Animator) may have just changed
        // (a gender switch) - re-resolve after the activation toggle above.
        // Skipped when unchanged so a color-only Apply() doesn't needlessly
        // re-point the NetworkAnimator every time.
        Transform activeRigRoot = female ? femaleRigRoot : maleRigRoot;
        ActiveRigRoot = activeRigRoot;
        Animator resolvedAnimator = activeRigRoot != null ? activeRigRoot.GetComponentInChildren<Animator>() : null;
        if (resolvedAnimator != ActiveAnimator)
        {
            ActiveAnimator = resolvedAnimator;
            if (networkAnimator != null) networkAnimator.Animator = resolvedAnimator;
        }
    }
}
