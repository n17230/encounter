using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// Purely cosmetic character appearance - Top/Bottom/face-feature/hair
// pieces, freely-combined accessories, headwear, gender/base rig, and two
// recolor swatches. No gameplay effect at all, so unlike CharacterEquipment's
// gear sync this is owner-authoritative: there's nothing to cheat by lying
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
    // .PlayCastVfxClientRpc) - both rigs share the same Humanoid avatar, so
    // this resolves correctly regardless of gender without needing to know
    // either rig's actual joint names.
    public Transform GetRightHandBone()
    {
        return ActiveAnimator != null ? ActiveAnimator.GetBoneTransform(HumanBodyBones.RightHand) : null;
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
        // Purely cosmetic and invisible to anyone on a headless dedicated
        // server - skip entirely there, same as ShieldVisual/
        // EffectOverheadVisual/AuraGroundVisual already do, rather than
        // doing renderer scans and MaterialPropertyBlock writes for every
        // connected player for nothing.
        if (!IsClient) return;

        applier.Apply(new AppearanceSelection
        {
            Female = IsFemale.Value,
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

        // The active rig (and therefore its Animator) may have just changed
        // (a gender switch) - re-resolve after applier.Apply() above, since
        // that's what actually toggled maleRigRoot/femaleRigRoot active.
        // Skipped when unchanged so a color-only Apply() doesn't needlessly
        // re-point the NetworkAnimator every time.
        Transform activeRigRoot = IsFemale.Value ? femaleRigRoot : maleRigRoot;
        Animator resolvedAnimator = activeRigRoot != null ? activeRigRoot.GetComponentInChildren<Animator>() : null;
        if (resolvedAnimator != ActiveAnimator)
        {
            ActiveAnimator = resolvedAnimator;
            if (networkAnimator != null) networkAnimator.Animator = resolvedAnimator;
        }
    }
}
