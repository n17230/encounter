using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// Purely cosmetic character appearance, built from Synty Sidekick presets
// (see SidekickSelection/SidekickPresetCatalog). No gameplay effect at all,
// so unlike CharacterEquipment's equipment sync this is owner-authoritative:
// there's nothing to cheat by lying about your own appearance, the same
// reasoning this project already applies to movement. Every peer builds
// its own local copy of the character from the synced selection, the same
// everyone-sees-it approach CharacterWeaponVisual uses for weapon models.
public class CharacterAppearance : NetworkBehaviour
{
    // A persistent, pre-placed child of the player root. The built Sidekick
    // character is parented under it at runtime (clients only), and it
    // carries a placeholder Animator (controller assigned, no avatar) so
    // NetworkAnimator always has a non-null Animator at Awake() - see the
    // note in Awake(). On a headless server this placeholder IS the rig:
    // nothing is ever built there.
    [SerializeField] private Transform characterRoot;
    [SerializeField] private RuntimeAnimatorController animatorController;

    // ';'-joined SidekickSelection (nine preset names, ~30 chars each) -
    // comfortably within 512 bytes.
    public readonly NetworkVariable<FixedString512Bytes> Selection =
        new NetworkVariable<FixedString512Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private NetworkAnimator networkAnimator;
    private BuiltSidekickCharacter built;
    private SidekickSelection builtSelection;

    // The Animator actually animating this character right now - the built
    // Sidekick character's on a client, the placeholder on the server.
    // Movement/ability code (PlayerMovement, PlayerAbilities) reads this
    // instead of caching an Animator themselves, since only this class
    // knows when a rebuild swapped it.
    public Animator ActiveAnimator { get; private set; }

    // The built character's own root Transform (or the placeholder root on
    // the server) - distinct from this component's own transform (the
    // network-replicated player root, also the camera's parent and what
    // movement/combat-facing read). Purely-visual rotations (see
    // PlayerMovement's strafe-turn) belong on this transform instead, so
    // they never affect movement direction, the camera, or FacingCone
    // checks.
    public Transform ActiveRigRoot { get; private set; }

    private void Awake()
    {
        networkAnimator = GetComponent<NetworkAnimator>();

        Animator placeholder = characterRoot != null ? characterRoot.GetComponent<Animator>() : null;
        ActiveRigRoot = characterRoot;
        ActiveAnimator = placeholder;

        // NetworkAnimator.Awake() only builds its parameter/state sync
        // arrays if its own Animator field is already non-null at that exact
        // moment - if it's still null there, sync is silently broken for
        // this instance forever, and assigning .Animator later (see Apply()
        // below) does NOT retroactively fix it. Unity doesn't guarantee
        // component Awake() order, so this assignment here is only a
        // best-effort - the NetworkAnimator's Animator field must also be
        // pre-wired to characterRoot's placeholder Animator in the Editor so
        // it's guaranteed non-null before any Awake() runs at all.
        if (networkAnimator != null && networkAnimator.Animator == null && placeholder != null)
        {
            networkAnimator.Animator = placeholder;
        }
    }

    // Right-hand bone of the live character, for effects that should
    // visually originate from/track the hand (see PlayerAbilities
    // .PlayCastVfxClientRpc, CharacterWeaponVisual) - resolved through the
    // Humanoid avatar, so no joint names are needed here.
    public Transform GetRightHandBone()
    {
        return ActiveAnimator != null ? ActiveAnimator.GetBoneTransform(HumanBodyBones.RightHand) : null;
    }

    // The Sidekick rig's own held-item sockets (children of the hand
    // joints) - what CharacterWeaponVisual parents weapon/shield models
    // to. Looked up by name since they're extra joints outside the Humanoid
    // mapping; falls back to the Humanoid hand bone for a rig that lacks
    // them. Cached per rig root - a rebuild swaps the root, so the cache is
    // keyed on that.
    public const string RightEquipSocketName = "prop_r";
    public const string LeftEquipSocketName = "prop_l";

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
    // player) and CharacterPreview resolve sockets exactly the way the
    // game does.
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

        Selection.OnValueChanged += (previous, current) => Apply();
        Apply();
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner) MainMenu.Closed -= PushProfileToNetworkVariables;
        built?.Destroy(false);
        built = null;
    }

    private void PushProfileToNetworkVariables()
    {
        Selection.Value = SidekickSelection.FromProfile(ProfileStore.Current).ToJoined();
    }

    private void Apply()
    {
        // Building meshes/materials is invisible on a headless dedicated
        // server - skip it there, same as ArcaneShieldVisual/
        // EffectOverheadVisual/AuraGroundVisual already do. The server keeps
        // the placeholder as its rig: NetworkAnimator's parameter sync still
        // drives that Animator, and server-side code that asks for a hand
        // bone (PlayerAbilities spawning a projectile at GetRightHandBone())
        // gets null from an avatar-less placeholder, which those call sites
        // already handle.
        if (!IsClient) return;

        SidekickSelection selection = SidekickSelection.Parse(Selection.Value.ToString());
        if (built != null && selection.Equals(builtSelection)) return;

        SidekickCharacterBuilder builder = SidekickCharacterBuilder.Shared;
        if (!builder.IsReady) return;

        built?.Destroy(false);
        built = builder.Build(selection, characterRoot, animatorController, "SidekickCharacter");
        builtSelection = selection;
        socketCacheRig = null;

        if (built == null)
        {
            ActiveRigRoot = characterRoot;
            ActiveAnimator = characterRoot != null ? characterRoot.GetComponent<Animator>() : null;
            return;
        }

        ActiveRigRoot = built.Root.transform;
        if (built.Animator != ActiveAnimator)
        {
            ActiveAnimator = built.Animator;
            if (networkAnimator != null) networkAnimator.Animator = built.Animator;
        }
    }
}
