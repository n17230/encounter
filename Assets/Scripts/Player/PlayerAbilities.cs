using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerTargeting))]
[RequireComponent(typeof(PlayerCamera))]
[RequireComponent(typeof(CharacterStats))]
[RequireComponent(typeof(CharacterEquipment))]
[RequireComponent(typeof(PlayerAutoAttack))]
[RequireComponent(typeof(PlayerMovement))]
public class PlayerAbilities : NetworkBehaviour
{
    [SerializeField] private float facingConeAngle = 120f;
    [SerializeField] private Color groundReticleColor = new Color(0.4f, 0.8f, 1f, 0.6f);
    // Starting ANY cast (instant or otherwise) locks out starting another
    // one for this long, on top of that ability's own Cooldown - a single
    // shared gate across every slot. Not specified by the user; 1.5s is the
    // standard MMO GCD length, flagged in review_with_fable.md.
    [SerializeField] private float globalCooldownDuration = 1.5f;
    public float FacingConeAngle => facingConeAngle;

    // How long before an AbilityData.PlaysCastAttackAnimation ability's
    // resolve() the cast-attack animation trigger fires - see
    // StartServerCast/ResolveAfterCastTime. For an instant ability (CastTime
    // <= 0) this becomes the ability's whole effective wait, so its
    // animation still gets to play before the effect lands instead of both
    // firing in the same instant.
    private const float CastAnimationLeadTime = 0.15f;

    private PlayerTargeting targeting;
    private PlayerCamera playerCameraComponent;
    private CharacterStats stats;
    private CharacterEquipment equipment;
    private PlayerAutoAttack autoAttack;
    private PlayerMovement movement;
    private CharacterAppearance appearance;

    // Owner-local aiming state for a ground-targeted ability (see
    // AbilityData.IsGroundTargeted). Exposed so PlayerTargeting can ignore
    // clicks meant for placement, not targeting.
    public bool IsAimingGroundTarget { get; private set; }
    private int aimingSlot = -1;
    private AbilityData aimingAbility;
    // Exactly one of these exists while aiming: the circle for ordinary
    // ground abilities, the wall rectangle for a persistent structure.
    private GroundTargetReticle reticle;
    private WallPlacementPreview wallPreview;
    private Vector3? aimedGroundPoint;

    private bool isCasting;
    private float castStartTime;
    private float castDuration;
    private string castingAbilityName;

    private string noticeMessage;
    private float noticeEndTime;

    // Owner-side prediction, MMO style: the client pre-checks what it can
    // (target, range, facing, mana, cooldown) and starts the cooldown the
    // instant the key is pressed, so casting feels immediate. The server
    // still decides; a rejection rolls the predicted cooldown back.
    private readonly Dictionary<AbilityData, float> predictedCooldownReady = new Dictionary<AbilityData, float>();
    // Same prediction/rollback treatment as predictedCooldownReady, but a
    // single shared value instead of per-ability - see globalCooldownDuration.
    private float predictedGlobalCooldownReady;

    // Server-authoritative: which ability (or null) the owning player has in
    // each loadout slot. Clients only ever send slot indices to cast, and
    // ability Ids to (re)assign slots - never the assets themselves.
    private readonly AbilityData[] serverSlotAbilities = new AbilityData[PlayerProfile.AbilitySlots];
    private readonly Dictionary<AbilityData, float> cooldownReadyTime = new Dictionary<AbilityData, float>();

    // Server-authoritative: for an AbilityData.ExclusiveSingleTarget
    // ability, who currently holds the effect from THIS caster's casts of
    // it (see ResolveAbility). Per-caster, not global.
    private readonly Dictionary<AbilityData, Targetable> exclusiveTargets = new Dictionary<AbilityData, Targetable>();

    // Server-authoritative: for an AbilityData.IsPersistentStructure
    // ability, the structures THIS caster's recent casts of it placed, oldest
    // first, up to AbilityData.MaxActiveStructures - see
    // ResolvePersistentStructure. Per-caster, not global (mirrors
    // exclusiveTargets above).
    private readonly Dictionary<AbilityData, List<NetworkObject>> activeStructures = new Dictionary<AbilityData, List<NetworkObject>>();

    // Server-authoritative state of the one cast currently in flight (null
    // when nothing is). Each cast gets its OWN object: the resolve coroutine
    // reads only this, so a new cast accepted in the same frame an old one
    // finishes (see ResolveAfterCastTime) can't retime the old one - and
    // cast pushback (HandleServerDamageTaken) extends EndTime on exactly the
    // cast being hit. A fresh Pushback tracker per cast is what makes the
    // pushback counter reset every cast.
    private sealed class ServerCast
    {
        public int Serial;
        public AbilityData Ability;
        public float EndTime;
        public CastPushbackTracker Pushback;
    }
    private ServerCast activeServerCast;
    // Server-authoritative cast lock - while Time.time is before this, no
    // new cast (instant or otherwise) can start, regardless of which
    // ability/slot. Client-side isCasting only gates the local UI/input;
    // this is what actually enforces the rule against a desynced or
    // malicious client. Derived from activeServerCast so there's one source
    // of truth; autoAttack.ServerCastLockUntil (another component) is a
    // mirror, re-set wherever EndTime changes.
    private float serverCastEndTime => activeServerCast?.EndTime ?? 0f;
    // The cast-time cast currently waiting to resolve, if any - kept so
    // dying mid-cast can cancel it (see CancelCast) instead of letting
    // it resolve once the caster is back.
    private Coroutine pendingCast;
    private int castSerial;
    // Server-authoritative global cooldown - see globalCooldownDuration.
    private float serverGlobalCooldownReadyTime;

    private void Awake()
    {
        targeting = GetComponent<PlayerTargeting>();
        playerCameraComponent = GetComponent<PlayerCamera>();
        stats = GetComponent<CharacterStats>();
        equipment = GetComponent<CharacterEquipment>();
        autoAttack = GetComponent<PlayerAutoAttack>();
        movement = GetComponent<PlayerMovement>();
        appearance = GetComponent<CharacterAppearance>();
    }

    public override void OnNetworkSpawn()
    {
        // Server subscription first: on a dedicated server IsOwner is false
        // for every player, so anything after the owner early-return below
        // would never run there.
        if (IsServer) stats.DamageTaken += HandleServerDamageTaken;

        if (!IsOwner) return;

        SyncLoadoutToServer();
        MainMenu.Closed += SyncLoadoutToServer;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            stats.DamageTaken -= HandleServerDamageTaken;
            // Frees this player's spells for others, the way
            // CharacterEquipment already frees its items.
            ReleaseAllClaims(OwnerClientId);
        }

        // A caster's walls go with them when they disconnect. Skipped on
        // host shutdown, where every object is being torn down anyway and
        // despawning one by one would only fight that.
        if (IsServer && NetworkManager != null && !NetworkManager.ShutdownInProgress)
        {
            foreach (List<NetworkObject> structures in activeStructures.Values)
            {
                foreach (NetworkObject structure in structures)
                {
                    if (structure != null && structure.IsSpawned) structure.Despawn();
                }
            }
            activeStructures.Clear();
        }

        if (!IsOwner) return;
        MainMenu.Closed -= SyncLoadoutToServer;
        reticle?.Destroy();
        wallPreview?.Destroy();
    }

    private void SyncLoadoutToServer()
    {
        SetLoadoutServerRpc(string.Join(";", ProfileStore.Current.SlotAbilityIds));
    }

    // Server-wide spell uniqueness: at most one connected player may have a
    // given ability Id slotted at a time - the exact mirror of
    // CharacterEquipment.globalItemOwners, one map for spells. Claimed both
    // by the lobby's spells confirm (LobbyState) and by SetLoadoutServerRpc
    // through ValidateAndClaimLoadout below, keyed by the same clientId, so
    // the spawn-time sync at Start re-claims what the lobby granted as a
    // no-op. An Id someone else holds is silently dropped from the loadout.
    private static readonly OwnershipMap globalAbilityOwners = new OwnershipMap();
    private static readonly List<string> keptIdScratch = new List<string>();

    // Called when a server session ends / the process starts - see NetworkBootstrap.
    public static void ResetServerSessionState()
    {
        globalAbilityOwners.Clear();
    }

    public static void ReleaseAllClaims(ulong clientId)
    {
        globalAbilityOwners.ReleaseAll(clientId);
    }

    // The shared validation core (see CharacterEquipment.ValidateAndClaim
    // for the same shape on the gear side): Ids must resolve, the same Id
    // can't fill two of the client's own slots, and each kept Id is claimed
    // for the client; everything the client held that isn't kept is
    // released. Null = empty or dropped slot.
    public static AbilityData[] ValidateAndClaimLoadout(string joinedAbilityIds, ulong clientId)
    {
        string[] ids = (joinedAbilityIds ?? "").Split(';');
        AbilityData[] result = new AbilityData[PlayerProfile.AbilitySlots];
        for (int i = 0; i < result.Length; i++)
        {
            AbilityData ability = i < ids.Length ? GameDatabase.GetAbility(ids[i]) : null;
            if (ability == null) continue;
            if (Array.IndexOf(result, ability, 0, i) >= 0) continue;
            if (!globalAbilityOwners.TryClaim(ability.Id, clientId)) continue;
            result[i] = ability;
        }

        keptIdScratch.Clear();
        foreach (AbilityData kept in result)
        {
            if (kept != null) keptIdScratch.Add(kept.Id);
        }
        globalAbilityOwners.ReleaseAllExcept(clientId, keptIdScratch);
        return result;
    }

    // The ';'-joined form of a validated result - what LobbyState stores in
    // the entry and sends back to the client (empty segment = empty slot).
    public static string JoinIds(AbilityData[] abilities)
    {
        string[] ids = new string[abilities.Length];
        for (int i = 0; i < abilities.Length; i++) ids[i] = abilities[i] != null ? abilities[i].Id : "";
        return string.Join(";", ids);
    }

    [ServerRpc]
    private void SetLoadoutServerRpc(string joinedAbilityIds)
    {
        AbilityData[] validated = ValidateAndClaimLoadout(joinedAbilityIds, OwnerClientId);
        Array.Copy(validated, serverSlotAbilities, serverSlotAbilities.Length);

        // Aura spells need no cast/keybind - having one slotted is enough to
        // keep it continuously active, so the set just tracks the loadout.
        equipment.SetActiveAuras(Array.FindAll(serverSlotAbilities, a => a != null && a.IsAuraSpell));
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (MainMenu.IsOpen)
        {
            // Opening the menu (whatever triggered it) abandons any in-progress
            // aim rather than leaving a stuck reticle up behind it.
            if (IsAimingGroundTarget) CancelGroundTargeting();
            return;
        }

        // Dead: no casting (the server rejects it too - "You are dead"),
        // and dying mid-aim abandons the reticle the same way the menu does.
        if (!stats.IsAlive)
        {
            if (IsAimingGroundTarget) CancelGroundTargeting();
            return;
        }

        if (IsAimingGroundTarget)
        {
            UpdateGroundAiming();
            if (Input.GetMouseButtonDown(0))
            {
                ConfirmGroundTarget();
                return;
            }
        }

        if (isCasting) return; // can't start a new cast until the current one finishes

        PlayerProfile profile = ProfileStore.Current;
        for (int slot = 0; slot < PlayerProfile.AbilitySlots; slot++)
        {
            AbilityData ability = profile.GetSlotAbility(slot);
            // Aura spells have no cast/keybind - they're always active once
            // slotted (see SetLoadoutServerRpc/CharacterEquipment.SetActiveAuras).
            if (ability != null && ability.IsAuraSpell) continue;
            KeyBindingOption? key = profile.GetSlotKey(slot);
            if (ability == null || !key.HasValue) continue;
            if (!key.Value.WasPressedThisFrame()) continue;

            if (ability.IsGroundTargeted)
            {
                if (IsAimingGroundTarget)
                {
                    // Only the aiming ability's own key does anything (cancels);
                    // other hotkeys are inert while aiming.
                    if (aimingSlot == slot) CancelGroundTargeting();
                    continue;
                }

                string groundRejection = ClientPrecheck(ability, out _);
                if (groundRejection != null)
                {
                    ShowNotice(groundRejection);
                    continue;
                }

                BeginGroundTargeting(slot, ability);
                continue;
            }

            string rejection = ClientPrecheck(ability, out NetworkObject targetNetworkObject);
            if (rejection != null)
            {
                ShowNotice(rejection);
                continue;
            }

            CastAbilityServerRpc(slot, targetNetworkObject != null ? targetNetworkObject.NetworkObjectId : 0);
            BeginPredictedCast(ability);
        }
    }

    // Owner-side optimistic start, shared by unit-targeted and
    // ground-targeted casts: cooldown + global cooldown begin the instant
    // the request is sent, and the cast bar starts for anything with a cast
    // time. NotifyCastRejectedClientRpc rolls all of it back.
    private void BeginPredictedCast(AbilityData ability)
    {
        predictedCooldownReady[ability] = Time.time + ability.Cooldown;
        predictedGlobalCooldownReady = Time.time + globalCooldownDuration;

        if (ability.CastTime > 0f)
        {
            isCasting = true;
            castStartTime = Time.time;
            castDuration = ability.CastTime;
            castingAbilityName = ability.AbilityName;
        }
    }

    private void BeginGroundTargeting(int slot, AbilityData ability)
    {
        IsAimingGroundTarget = true;
        aimingSlot = slot;
        aimingAbility = ability;
        aimedGroundPoint = null;

        reticle?.Destroy();
        reticle = null;
        wallPreview?.Destroy();
        wallPreview = null;

        if (ability.IsPersistentStructure)
        {
            wallPreview = new WallPlacementPreview(ability.StructureWidth, ability.StructureThickness, groundReticleColor);
        }
        else
        {
            reticle = new GroundTargetReticle(ability.GroundEffectRadius, groundReticleColor);
        }
    }

    private void CancelGroundTargeting()
    {
        IsAimingGroundTarget = false;
        aimingSlot = -1;
        aimingAbility = null;
        aimedGroundPoint = null;
        reticle?.Destroy();
        reticle = null;
        wallPreview?.Destroy();
        wallPreview = null;
    }

    // The owner's facing on the ground plane. Sent with a ground cast and
    // used for the wall preview, so both go through the same
    // WallSegmentLayout.FacingRotation.
    private Vector2 FlatFacing()
    {
        Vector3 forward = transform.forward;
        return new Vector2(forward.x, forward.z);
    }

    // Raycasts the mouse against the world (ignoring characters, so you can
    // aim through a mob standing in front of the spot) and moves the
    // reticle there; no hit just hides it until the mouse finds ground.
    private void UpdateGroundAiming()
    {
        Camera cam = playerCameraComponent.Camera;
        if (cam == null) return;

        // Triggers ignored too - a ground patch or following zone is a
        // trigger volume, not ground, and would otherwise catch the ray and
        // float the reticle up onto its surface.
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        int ignoreCharacters = ~LayerMask.GetMask("Characters");
        if (Physics.Raycast(ray, out RaycastHit hit, 200f, ignoreCharacters, QueryTriggerInteraction.Ignore))
        {
            aimedGroundPoint = hit.point;
            if (wallPreview != null)
            {
                wallPreview.Update(hit.point, WallSegmentLayout.FacingRotation(FlatFacing(), transform.forward));
                wallPreview.SetVisible(true);
            }
            else
            {
                reticle.SetPosition(hit.point);
                reticle.SetVisible(true);
            }
        }
        else
        {
            aimedGroundPoint = null;
            wallPreview?.SetVisible(false);
            reticle?.SetVisible(false);
        }
    }

    private void ConfirmGroundTarget()
    {
        if (!aimedGroundPoint.HasValue) return; // nothing under the cursor yet - stay aiming

        Vector3 point = aimedGroundPoint.Value;
        if (Vector3.Distance(transform.position, point) > aimingAbility.Range)
        {
            ShowNotice("Out of range");
            return; // stay aiming - move closer and click again
        }

        AbilityData ability = aimingAbility;
        int slot = aimingSlot;
        CancelGroundTargeting();

        CastGroundTargetedAbilityServerRpc(slot, point, FlatFacing());
        BeginPredictedCast(ability);
    }

    // Mirrors the server's start-of-cast checks using replicated state, so
    // the common failures are reported instantly without a round trip.
    private string ClientPrecheck(AbilityData ability, out NetworkObject targetNetworkObject)
    {
        targetNetworkObject = null;

        if (predictedCooldownReady.TryGetValue(ability, out float ready) && Time.time < ready) return "Not ready";
        if (Time.time < predictedGlobalCooldownReady) return "Global cooldown";
        if (stats.CurrentMana.Value < ability.ManaCost * stats.SyncedManaCostMultiplier.Value) return "Not enough mana";

        // Client-side mirror of the server's authoritative equipment (see
        // CastAbilityServerRpc) - CharacterEquipment.MainHandWeapon is only
        // ever populated on the server, so the owner checks its own known
        // loadout instead.
        if (ability.RequiresMeleeWeapon)
        {
            ItemData mainHand = ProfileStore.Current.GetEquipment(EquipmentSlot.MainHand);
            if (mainHand == null || mainHand.Weapon == null) return "Requires a melee weapon";
        }
        if (ability.RequiresShield)
        {
            ItemData offHand = ProfileStore.Current.GetEquipment(EquipmentSlot.OffHand);
            if (offHand == null || !offHand.IsShield) return "Requires a shield";
        }

        if (!ability.RequiresTarget) return null;

        Targetable target = targeting.CurrentTarget;
        if (target == null) return "No target";
        targetNetworkObject = target.GetComponent<NetworkObject>();
        if (targetNetworkObject == null) return "Invalid target";
        // CurrentHealth/IsMob are known on every client, so the dead/alive
        // rule (see TargetStateRule) is predictable here like range is.
        if (target.Stats != null)
        {
            string stateRejection = TargetStateRule.Check(ability.ResurrectTarget, target.Stats.IsAlive, !target.Stats.IsMob);
            if (stateRejection != null) return stateRejection;
        }
        if (Vector3.Distance(transform.position, target.transform.position) > ability.Range) return "Out of range";
        if (!IsWithinFacingCone(targetNetworkObject)) return "Target not in front of you";
        return null;
    }

    private void ShowNotice(string message)
    {
        noticeMessage = message;
        noticeEndTime = Time.time + 2f;
    }

    private float PredictedCooldownRemaining(AbilityData ability)
    {
        return predictedCooldownReady.TryGetValue(ability, out float ready) ? Mathf.Max(0f, ready - Time.time) : 0f;
    }

    private void OnGUI()
    {
        if (!IsOwner) return;

        DevGui.Begin();
        const float barWidth = 300f;
        const float barHeight = 24f;
        float x = (UIScale.Width - barWidth) * 0.5f;
        // Bottom-centre stack, bottom to top: ability names, icons, hotkey
        // labels, then the cast bar (and its notice above it) - derived from
        // the ability bar's own constants so the cast bar can't drift back
        // onto the icons.
        float y = UIScale.Height - AbilityBarBottomOffset - AbilitySlotSize - HotkeyLabelOffset - 4f - CastBarGap - barHeight;

        DrawAbilityBar(UIScale.Height - AbilityBarBottomOffset);

        if (isCasting)
        {
            float elapsed = Time.time - castStartTime;
            if (elapsed >= castDuration)
            {
                isCasting = false;
            }
            else
            {
                float progress = Mathf.Clamp01(elapsed / castDuration);

                GUI.Box(new Rect(x, y, barWidth, barHeight), GUIContent.none);

                Color previousColor = GUI.color;
                GUI.color = Color.cyan;
                GUI.DrawTexture(new Rect(x, y, barWidth * progress, barHeight), Texture2D.whiteTexture);
                GUI.color = previousColor;

                GUIStyle centered = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(x, y, barWidth, barHeight), castingAbilityName, centered);
            }
        }

        // Notices (local pre-check failures, server rejections, and fizzles
        // that are only known once a cast resolves) share one spot above
        // the cast bar.
        if (Time.time < noticeEndTime)
        {
            GUIStyle noticeStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            noticeStyle.normal.textColor = Color.red;
            GUI.Label(new Rect(x, y - 22f, barWidth, 20f), noticeMessage, noticeStyle);
        }
    }

    // Bottom-centre row of hotkey-castable loadout slots: key, name, and a
    // cooldown sweep driven by the predicted cooldowns (so it starts on key
    // press). Aura spells have no hotkey or cooldown at all (see
    // PlayerAbilities' own key-press loop, which already skips them), so
    // they get no bar slot either - only slots holding a real castable
    // ability are shown, compacted left to right with no gaps for empty or
    // aura slots.
    private const float AbilityBarBottomOffset = 56f; // icon row's bottom edge, from the screen bottom
    private const float AbilitySlotSize = 56f;
    private const float HotkeyLabelOffset = 16f;      // hotkey label sits this far above an icon's top
    private const float CastBarGap = 6f;

    private void DrawAbilityBar(float bottomY)
    {
        const float slotSize = AbilitySlotSize;
        const float gap = 4f;
        PlayerProfile profile = ProfileStore.Current;

        List<int> castableSlots = new List<int>();
        for (int i = 0; i < PlayerProfile.AbilitySlots; i++)
        {
            AbilityData slotAbility = profile.GetSlotAbility(i);
            if (slotAbility != null && !slotAbility.IsAuraSpell) castableSlots.Add(i);
        }

        int slotCount = castableSlots.Count;
        if (slotCount == 0) return;
        float totalWidth = slotCount * slotSize + (slotCount - 1) * gap;
        float x0 = (UIScale.Width - totalWidth) * 0.5f;
        float y0 = bottomY - slotSize;

        GUIStyle small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10, wordWrap = true };
        small.normal.textColor = Color.black;
        GUIStyle timer = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold };

        for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            int i = castableSlots[slotIndex];
            Rect rect = new Rect(x0 + slotIndex * (slotSize + gap), y0, slotSize, slotSize);
            GUI.Box(rect, GUIContent.none);

            AbilityData ability = profile.GetSlotAbility(i);
            if (ability.Icon != null) GUI.DrawTexture(rect, ability.Icon.texture);

            string keyText = profile.GetSlotKey(i)?.DisplayName ?? "-";
            GUI.Label(new Rect(rect.x, rect.y - HotkeyLabelOffset, rect.width, 14f), keyText, small);
            GUI.Label(new Rect(rect.x, rect.yMax + 2f, rect.width, 28f), ability.AbilityName, small);

            float remaining = PredictedCooldownRemaining(ability);
            if (remaining <= 0f) continue;

            float fraction = Mathf.Clamp01(remaining / Mathf.Max(0.01f, ability.Cooldown));
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - rect.height * fraction, rect.width, rect.height * fraction), Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.Label(rect, remaining.ToString("0.0"), timer);
        }
    }

    [ClientRpc]
    private void NotifyCastFizzledClientRpc(string reason)
    {
        if (!IsOwner) return;
        ShowNotice(reason);
    }

    // Server-only. Called on death (see PlayerRespawn.HandleDeath) -
    // cancels only the cast still in progress, both server-side and (via
    // RPC) the owner's cast bar, and nothing else: ability cooldowns and
    // the global cooldown deliberately keep running through death, so a
    // resurrected player picks up exactly where their cooldowns were.
    // Without this the dead player would be "Already casting" until the
    // cancelled cast's timer ran out, and it would resolve once they're
    // back.
    public void CancelCast()
    {
        if (!IsServer) return;
        if (pendingCast != null) StopCoroutine(pendingCast);
        pendingCast = null;
        activeServerCast = null;
        autoAttack.ServerCastLockUntil = 0f;
        CancelCastClientRpc();
    }

    // Server-only. The full reset, for a respawn (see
    // PlayerRespawn.ServerRespawn) - CancelCast plus every ability's
    // cooldown and the global cooldown, server-side and (via RPC) the
    // owner's own predicted copies, so the respawned player doesn't still
    // show abilities on cooldown from the fight that killed them.
    public void ResetCooldowns()
    {
        if (!IsServer) return;
        CancelCast();
        cooldownReadyTime.Clear();
        serverGlobalCooldownReadyTime = 0f;
        ResetCooldownsClientRpc();
    }

    // Only the cast bar - the owner's predicted cooldowns are left alone,
    // matching the server's untouched ones (see CancelCast).
    [ClientRpc]
    private void CancelCastClientRpc()
    {
        if (!IsOwner) return;
        isCasting = false;
    }

    // Server-only, from CharacterStats.DamageTaken: a direct hit just
    // lowered this caster's health. If a cast-time cast is in flight, push
    // its end out by this hit's share (see CastPushbackTracker) and tell the
    // owner how long is now left so the cast bar stretches to match.
    // Instant casts (CastTime 0, including the animation-only 0.15s lock)
    // can't be pushed back.
    private void HandleServerDamageTaken(float healthLost)
    {
        ServerCast cast = activeServerCast;
        if (cast == null || cast.Ability.CastTime <= 0f || Time.time >= cast.EndTime) return;

        float delay = cast.Pushback.RegisterHit();
        if (delay <= 0f) return;

        cast.EndTime += delay;
        autoAttack.ServerCastLockUntil = cast.EndTime;
        NotifyCastPushedBackClientRpc(cast.Ability.Id, cast.EndTime - Time.time);
    }

    // The owner's cast bar runs off its own clock (started at key press, so
    // ~one-way latency ahead of the server's), and OnGUI clears isCasting
    // the moment it runs out. So rather than adding the delay to the local
    // duration, take the server's remaining time and re-arm the bar from
    // it - which also re-opens a bar that had already run out early. Same
    // ability-name match NotifyCastRejectedClientRpc uses, so a pushback
    // can't re-arm a bar for a different cast than the one it's about.
    [ClientRpc]
    private void NotifyCastPushedBackClientRpc(string abilityId, float remaining)
    {
        if (!IsOwner) return;
        AbilityData ability = GameDatabase.GetAbility(abilityId);
        if (ability == null || ability.AbilityName != castingAbilityName) return;

        isCasting = true;
        castDuration = (Time.time - castStartTime) + remaining;
    }

    [ClientRpc]
    private void ResetCooldownsClientRpc()
    {
        if (!IsOwner) return;
        predictedCooldownReady.Clear();
        predictedGlobalCooldownReady = 0f;
        isCasting = false;
    }

    // Start-of-cast rejection: undo the optimistic cooldown and cast bar.
    [ClientRpc]
    private void NotifyCastRejectedClientRpc(string abilityId, string reason)
    {
        if (!IsOwner) return;
        AbilityData ability = GameDatabase.GetAbility(abilityId);
        if (ability != null) predictedCooldownReady.Remove(ability);
        // Also rolled back unconditionally: worst case the client tries
        // again a moment early, and the server (whose own GCD state is
        // untouched by this) simply rejects it again for real if it's
        // still active from a different, earlier successful cast.
        predictedGlobalCooldownReady = 0f;
        if (isCasting && castingAbilityName == ability?.AbilityName) isCasting = false;
        ShowNotice(reason);
    }

    [ServerRpc]
    private void CastAbilityServerRpc(int slotIndex, ulong targetNetworkObjectId)
    {
        if (slotIndex < 0 || slotIndex >= serverSlotAbilities.Length) return;

        AbilityData ability = serverSlotAbilities[slotIndex];
        if (ability == null) return;

        if (RejectIfDead(ability)) return;
        if (RejectIfNotReady(ability)) return;

        if (ability.RequiresTarget)
        {
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject startTargetObject))
            {
                NotifyCastRejectedClientRpc(ability.Id, "Target lost");
                return;
            }
            // Same dead/alive rule the owner already predicted (see
            // ClientPrecheck) and ResolveAbility re-checks at resolve time.
            string stateRejection = TargetStateRejection(ability, startTargetObject.GetComponent<Targetable>());
            if (stateRejection != null)
            {
                NotifyCastRejectedClientRpc(ability.Id, stateRejection);
                return;
            }
            if (!IsWithinFacingCone(startTargetObject))
            {
                NotifyCastRejectedClientRpc(ability.Id, "Target not in front of you");
                return;
            }
            // A ranged spell can't reach a target standing inside an active
            // Arcane Shield dome - melee-range abilities are unaffected.
            // Server-only knowledge (see ArcaneShieldZones), so this can't
            // be predicted client-side the way the checks above are.
            if (ability.Range > WeaponData.BasicAttackRange && ArcaneShieldZones.Blocks(startTargetObject.transform.position))
            {
                NotifyCastRejectedClientRpc(ability.Id, "Target is shielded");
                return;
            }
        }

        if (!stats.HasEnoughMana(ability.ManaCost))
        {
            NotifyCastRejectedClientRpc(ability.Id, "Not enough mana");
            return;
        }

        if (ability.RequiresMeleeWeapon && equipment.MainHandWeapon == null)
        {
            NotifyCastRejectedClientRpc(ability.Id, "Requires a melee weapon");
            return;
        }
        if (ability.RequiresShield && !equipment.HasShieldEquipped)
        {
            NotifyCastRejectedClientRpc(ability.Id, "Requires a shield");
            return;
        }

        // Mana is only actually spent once the cast succeeds - see
        // ResolveAbility - not here at cast start. The check above just
        // stops an unaffordable cast from starting in the first place.
        StartServerCast(ability, () => ResolveAbility(ability, targetNetworkObjectId));
    }

    // A dead caster casts nothing - checked before anything else, both for
    // unit-targeted and ground-targeted casts. The owner's own Update
    // already stops sending while dead; this is the authoritative half.
    private bool RejectIfDead(AbilityData ability)
    {
        if (stats.IsAlive) return false;
        NotifyCastRejectedClientRpc(ability.Id, "You are dead");
        return true;
    }

    // TargetStateRule applied to a resolved target - null if it may be
    // cast on (or if it has no CharacterStats at all, which the later
    // "Invalid target" checks already cover).
    private static string TargetStateRejection(AbilityData ability, Targetable target)
    {
        if (target == null || target.Stats == null) return null;
        return TargetStateRule.Check(ability.ResurrectTarget, target.Stats.IsAlive, !target.Stats.IsMob);
    }

    // The start-of-cast gate every cast shares, whatever it targets: one
    // cast at a time, the global cooldown, then the ability's own cooldown.
    // Returns true (and tells the owner why) if the cast may not start.
    private bool RejectIfNotReady(AbilityData ability)
    {
        if (Time.time < serverCastEndTime)
        {
            NotifyCastRejectedClientRpc(ability.Id, "Already casting");
            return true;
        }
        if (Time.time < serverGlobalCooldownReadyTime)
        {
            NotifyCastRejectedClientRpc(ability.Id, "Global cooldown");
            return true;
        }
        if (cooldownReadyTime.TryGetValue(ability, out float readyTime) && Time.time < readyTime)
        {
            NotifyCastRejectedClientRpc(ability.Id, "Not ready");
            return true;
        }
        return false;
    }

    // Every start-of-cast check has passed: commit the cooldowns, then
    // either resolve now (instant) or after the cast time, with the hand
    // VFX playing for everyone in the meantime. A PlaysAnyCastAnimation
    // ability always waits at least CastAnimationLeadTime before resolving,
    // even if it's normally instant - see ResolveAfterCastTime - so its
    // animation always gets to play before the effect lands.
    private void StartServerCast(AbilityData ability, Action resolve)
    {
        cooldownReadyTime[ability] = Time.time + ability.Cooldown;
        serverGlobalCooldownReadyTime = Time.time + globalCooldownDuration;

        if (ability.CastTime <= 0f && !ability.PlaysAnyCastAnimation)
        {
            resolve();
            return;
        }

        float totalWait = ability.PlaysAnyCastAnimation
            ? Mathf.Max(ability.CastTime, CastAnimationLeadTime)
            : ability.CastTime;

        ServerCast cast = new ServerCast
        {
            Serial = ++castSerial,
            Ability = ability,
            EndTime = Time.time + totalWait,
            Pushback = new CastPushbackTracker(ability.CastTime),
        };
        activeServerCast = cast;
        autoAttack.ServerCastLockUntil = cast.EndTime;
        if (ability.CastTime > 0f) PlayCastVfxClientRpc(ability.Id, ability.CastTime);
        pendingCast = StartCoroutine(ResolveAfterCastTime(cast, resolve));
    }

    // Everyone sees the caster's hand-glow VFX, not just the owner - it's
    // purely cosmetic (no gameplay state), so each client just instantiates
    // it locally rather than it being a NetworkObject. Parented to the
    // right-hand bone (same one for every spell - see
    // CharacterAppearance.GetRightHandBone) so it tracks through any
    // animation instead of floating at a fixed offset from the root; falls
    // back to the old root-relative placement if the bone isn't resolvable
    // (e.g. rig not wired yet).
    [ClientRpc]
    private void PlayCastVfxClientRpc(string abilityId, float duration)
    {
        AbilityData ability = GameDatabase.GetAbility(abilityId);
        if (ability == null || ability.CastVfxPrefab == null) return;

        Transform rightHand = appearance != null ? appearance.GetRightHandBone() : null;
        GameObject vfxInstance = rightHand != null
            ? Instantiate(ability.CastVfxPrefab, rightHand.position, rightHand.rotation, rightHand)
            : Instantiate(ability.CastVfxPrefab, transform.position + Vector3.up * 1.2f + transform.forward * 0.5f, transform.rotation, transform);
        VfxScale.Apply(vfxInstance, ability.CastVfxScale);
        Destroy(vfxInstance, duration);
    }

    // cast.Serial identifies WHICH cast this coroutine belongs to: a new cast
    // can be accepted (network messages are processed early in the frame)
    // in the same frame this one finishes waiting (coroutines resume later
    // in it), by which point pendingCast/activeServerCast already point at
    // the NEW cast - so only clear them if they're still this cast's own,
    // or a death during that second cast would find nothing to cancel.
    //
    // Waits are polled against cast.EndTime rather than a fixed
    // WaitForSeconds, because cast pushback (HandleServerDamageTaken) can
    // move EndTime out while this is waiting. For a PlaysAnyCastAnimation
    // ability EndTime is at least CastAnimationLeadTime away even if
    // CastTime is 0, so the animation trigger always fires
    // CastAnimationLeadTime before resolve() rather than in the same
    // instant. A hit landing inside that lead window pushes resolve() out
    // again after the gesture has already played - accepted cosmetic edge.
    private IEnumerator ResolveAfterCastTime(ServerCast cast, Action resolve)
    {
        AbilityData ability = cast.Ability;
        float lead = ability.PlaysAnyCastAnimation ? CastAnimationLeadTime : 0f;

        while (Time.time < cast.EndTime - lead) yield return null;
        if (ability.PlaysCastAttackAnimation) PlayCastAttackAnimationClientRpc();
        if (ability.PlaysCastHealAnimation) PlayCastHealAnimationClientRpc();
        while (Time.time < cast.EndTime) yield return null;

        if (cast.Serial == castSerial)
        {
            pendingCast = null;
            activeServerCast = null;
        }
        resolve();
    }

    // Purely cosmetic - the caster's Stander@Magic_Attack1 swing (see
    // AbilityData.PlaysCastAttackAnimation), fired via the shared
    // "castAttack" Animator trigger. Same owner-authoritative-NetworkAnimator
    // pattern PlayerAutoAttack.PlayAttackSwingClientRpc uses for basic-attack
    // swings: the server decides the cast is happening, but only the
    // owner's own SetTrigger call actually replicates to everyone else.
    [ClientRpc]
    private void PlayCastAttackAnimationClientRpc()
    {
        if (!IsOwner) return;
        appearance.ActiveAnimator?.SetTrigger("castAttack");
    }

    // Same pattern as PlayCastAttackAnimationClientRpc, for the shared
    // Stander@Sub_Spell1 (1) heal gesture (see
    // AbilityData.PlaysCastHealAnimation) via the "castHeal" trigger.
    [ClientRpc]
    private void PlayCastHealAnimationClientRpc()
    {
        if (!IsOwner) return;
        appearance.ActiveAnimator?.SetTrigger("castHeal");
    }

    private void ResolveAbility(AbilityData ability, ulong targetNetworkObjectId)
    {
        if (ability.EnemiesAroundCaster)
        {
            ResolveEnemiesAroundCaster(ability);
            return;
        }
        if (ability.ConeAroundCaster)
        {
            ResolveConeAroundCaster(ability);
            return;
        }
        if (ability.ChargeForwardDistance > 0f)
        {
            ResolveChargeForward(ability);
            return;
        }
        if (ability.AreaAroundCaster)
        {
            ResolveAreaAroundCaster(ability);
            return;
        }
        if (ability.SelfBuff)
        {
            ResolveSelfBuff(ability);
            return;
        }

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObject))
        {
            NotifyCastFizzledClientRpc("Target lost");
            return;
        }

        Targetable target = targetObject.GetComponent<Targetable>();
        if (target == null || target.Stats == null)
        {
            NotifyCastFizzledClientRpc("Invalid target");
            return;
        }
        // Re-checked at resolve, not just at cast start: the target can die
        // during a cast time (any ability), or take the Escape menu's
        // Respawn mid-Resurrect ("Target is not dead") - and a fizzle here,
        // before TrySpendOrFizzle, costs nothing.
        string stateRejection = TargetStateRejection(ability, target);
        if (stateRejection != null)
        {
            NotifyCastFizzledClientRpc(stateRejection);
            return;
        }

        float distance = Vector3.Distance(transform.position, targetObject.transform.position);
        if (distance > ability.Range)
        {
            NotifyCastFizzledClientRpc("Target out of range");
            return;
        }
        if (!IsWithinFacingCone(targetObject))
        {
            NotifyCastFizzledClientRpc("Target out of facing cone");
            return;
        }
        if (!CombatPhysics.HasLineOfSight(transform.position, targetObject.transform.position))
        {
            NotifyCastFizzledClientRpc("Line of sight blocked");
            return;
        }
        if (ability.IsFollowingZone && ability.FollowingZonePrefab == null)
        {
            NotifyCastFizzledClientRpc("Zone not configured yet");
            return;
        }

        // Every fizzle check above has passed - the cast is actually
        // succeeding, so this is where mana is spent (not at cast start;
        // a fizzled cast costs nothing).
        if (!TrySpendOrFizzle(ability)) return;

        if (ability.ChargeToTarget)
        {
            ResolveChargeToTarget(ability, target, targetObject);
            return;
        }

        if (ability.IsFollowingZone)
        {
            ResolveFollowingZone(ability, targetObject);
            return;
        }

        if (ability.RemovesNegativeEffect)
        {
            target.Stats.RemoveOneNegativeEffect();
            return;
        }

        if (ability.ResurrectTarget)
        {
            // The target is a dead player by now (TargetStateRejection
            // above); they stand back up where they fell. Same target-side
            // VFX path a direct heal uses, if the asset has one wired.
            target.Stats.Resurrect(ability.ResurrectHealthPercent, ability.ResurrectManaPercent);
            if (ability.TargetVfxPrefab != null) PlayTargetVfxClientRpc(ability.Id, targetNetworkObjectId);
        }
        else if (ability.RecallTarget)
        {
            Vector3 recallPosition = transform.position;
            if (targetObject.TryGetComponent(out PlayerMovement targetMovement))
            {
                targetMovement.ServerTeleportTo(recallPosition);
            }
            else if (targetObject.TryGetComponent(out EnemyAI targetEnemyAi))
            {
                targetEnemyAi.ServerTeleportTo(recallPosition);
            }
        }
        else if (ability.ProjectilePrefab != null)
        {
            // Right hand if resolvable (see CharacterAppearance.Apply() -
            // the rig is now kept active server-side too, not just on
            // clients), otherwise the old fixed offset from the root - e.g.
            // the very first frame or two before Apply() has ever run.
            // Rotation is always Quaternion.identity regardless: Projectile
            // .FixedUpdate overwrites it via LookRotation toward the target
            // starting the very first tick after spawn, so spawn rotation
            // is never actually visible.
            Transform rightHand = appearance != null ? appearance.GetRightHandBone() : null;
            Vector3 spawnPosition = rightHand != null
                ? rightHand.position
                : transform.position + Vector3.up * 1.5f + transform.forward * 0.5f;
            GameObject projectileInstance = Instantiate(ability.ProjectilePrefab, spawnPosition, Quaternion.identity);
            projectileInstance.GetComponent<NetworkObject>().Spawn();
            projectileInstance.GetComponent<Projectile>().Initialize(targetNetworkObjectId, ability, OwnerClientId, ResolveTotalDamage(ability));
        }
        else
        {
            if (ability.ExclusiveSingleTarget && ability.Effect != null)
            {
                // Strip it from whoever held it before, if that's someone
                // else - recasting on the same person just refreshes below.
                if (exclusiveTargets.TryGetValue(ability, out Targetable previous)
                    && previous != null && previous != target)
                {
                    previous.Stats?.RemoveEffect(ability.Effect);
                }
                exclusiveTargets[ability] = target;
            }

            target.Stats.ReceiveHit(new HitInfo
            {
                Damage = ResolveTotalDamage(ability),
                Heal = ability.HealAmount,
                ShieldAmount = ability.ShieldAmount,
                ExtraThreat = ability.ThreatValue,
                AttackerClientId = OwnerClientId,
                Source = HitSource.Ability,
                Effect = ability.Effect,
                EffectDuration = ability.DirectHitEffectDuration,
            });

            if (ability.TargetVfxPrefab != null) PlayTargetVfxClientRpc(ability.Id, targetNetworkObjectId);
        }
    }

    // Everyone sees this at the target's position, not just the caster -
    // purely cosmetic (no gameplay state), same instantiate-locally
    // approach as PlayCastVfxClientRpc. E.g. Blessing of Vitality's
    // Spell_Light_6 on the healed target.
    [ClientRpc]
    private void PlayTargetVfxClientRpc(string abilityId, ulong targetNetworkObjectId)
    {
        AbilityData ability = GameDatabase.GetAbility(abilityId);
        if (ability == null || ability.TargetVfxPrefab == null) return;
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObject)) return;

        GameObject vfxInstance = Instantiate(ability.TargetVfxPrefab, targetObject.transform.position, Quaternion.identity);
        VfxScale.Apply(vfxInstance, ability.TargetVfxScale);
        Destroy(vfxInstance, 5f);
    }

    // No unit/ground targeting at all - resolves centered on the caster's
    // own current position, affecting every player (caster included)
    // within GroundEffectRadius. Mana is spent here (this ability's
    // "successful cast" point), not at cast start, same as the other
    // resolve paths.
    private void ResolveAreaAroundCaster(AbilityData ability)
    {
        if (!TrySpendOrFizzle(ability)) return;

        Vector3 center = transform.position;
        ForEachLivingTarget(includeCaster: true,
            candidate => Vector3.Distance(center, candidate.transform.position) <= ability.GroundEffectRadius,
            candidate => candidate.Stats.ReceiveHit(new HitInfo
            {
                Heal = ability.HealAmount,
                ShieldAmount = ability.ShieldAmount,
                AttackerClientId = OwnerClientId,
                Source = HitSource.Ability,
                Effect = ability.Effect,
                EffectDuration = ability.DirectHitEffectDuration,
            }));
    }

    // Mana is spent at the moment a cast actually succeeds, never at cast
    // start - every resolve path calls this once its own fizzle checks have
    // passed. Returns false (and tells the owner) if it can't be paid.
    private bool TrySpendOrFizzle(AbilityData ability)
    {
        if (stats.TrySpendMana(ability.ManaCost)) return true;
        NotifyCastFizzledClientRpc("Not enough mana");
        return false;
    }

    // The one area-effect loop: every living Targetable (player or mob -
    // area effects hit anyone, by design) that inArea accepts. Iterates a
    // snapshot rather than the live registry, since apply() deals hits and
    // a hit can kill. includeCaster is false for every damaging shape
    // (never hits yourself) and true for the friendly/neutral ones.
    private void ForEachLivingTarget(bool includeCaster, Func<Targetable, bool> inArea, Action<Targetable> apply)
    {
        foreach (Targetable candidate in Registry<Targetable>.Snapshot())
        {
            if (candidate == null || candidate.Stats == null) continue;
            if (!includeCaster && candidate.transform == transform) continue;
            if (candidate.Stats.CurrentHealth.Value <= 0f) continue;
            if (!inArea(candidate)) continue;
            apply(candidate);
        }
    }

    // The damaging-hit shape every weapon/area attack shares.
    private HitInfo DamageHit(AbilityData ability, float damage) => new HitInfo
    {
        Damage = damage,
        AttackerClientId = OwnerClientId,
        Source = HitSource.Ability,
        Effect = ability.Effect,
        EffectDuration = ability.DirectHitEffectDuration,
    };

    // No targeting: applies Effect directly to the caster's own
    // CharacterStats - a plain timed self-buff, not an AoE and not a
    // permanent aura. E.g. Aegis of the Ancient.
    private void ResolveSelfBuff(AbilityData ability)
    {
        if (!TrySpendOrFizzle(ability)) return;

        stats.ApplyEffect(ability.Effect, ability.DirectHitEffectDuration, OwnerClientId, HitSource.Ability);
    }


    // What this caster actually swings with right now (equipped main hand,
    // or fists), including flat equipment bonuses (StatType.WeaponDamageBonus -
    // e.g. Amulet of the Berserker) - same total PlayerAutoAttack's basic
    // swing deals.
    private float ResolveWeaponDamage()
    {
        WeaponData weapon = autoAttack.ResolvedWeapon;
        float baseDamage = weapon != null ? weapon.Damage : 0f;
        return baseDamage + stats.WeaponDamageBonus.Value;
    }

    // Damage + a fraction of the caster's current weapon damage - see
    // AbilityData.WeaponDamagePercent. The single place every damage
    // source (flat, weapon-scaled, or both) is combined.
    private float ResolveTotalDamage(AbilityData ability)
    {
        float damage = ability.Damage;
        if (ability.WeaponDamagePercent > 0f) damage += ResolveWeaponDamage() * ability.WeaponDamagePercent;
        return damage;
    }

    // Full circle around the caster's own position, hitting every OTHER
    // Targetable (player or mob - never the caster) within
    // GroundEffectRadius. No targeting at all. E.g. Reaper's Wheel,
    // Seismic Slam.
    private void ResolveEnemiesAroundCaster(AbilityData ability)
    {
        if (ability.RequiresMeleeWeapon && equipment.MainHandWeapon == null)
        {
            NotifyCastFizzledClientRpc("Requires a melee weapon");
            return;
        }
        if (!TrySpendOrFizzle(ability)) return;

        float damage = ResolveTotalDamage(ability);
        Vector3 center = transform.position;
        ForEachLivingTarget(includeCaster: false,
            candidate => Vector3.Distance(center, candidate.transform.position) <= ability.GroundEffectRadius,
            candidate => candidate.Stats.ReceiveHit(DamageHit(ability, damage)));
    }

    // Same as ResolveEnemiesAroundCaster, but only targets within ConeAngle
    // degrees of the caster's current facing. E.g. Cleave.
    private void ResolveConeAroundCaster(AbilityData ability)
    {
        if (ability.RequiresMeleeWeapon && equipment.MainHandWeapon == null)
        {
            NotifyCastFizzledClientRpc("Requires a melee weapon");
            return;
        }
        if (!TrySpendOrFizzle(ability)) return;

        float damage = ResolveTotalDamage(ability);
        Vector3 center = transform.position;
        ForEachLivingTarget(includeCaster: false,
            candidate => Vector3.Distance(center, candidate.transform.position) <= ability.GroundEffectRadius
                && FacingCone.IsWithin(transform, candidate.transform.position, ability.ConeAngle),
            candidate => candidate.Stats.ReceiveHit(DamageHit(ability, damage)));
    }

    // No targeting: the caster charges straight forward (their own current
    // facing) for ChargeForwardDistance units, hitting every other
    // Targetable (player or mob - never the caster) near the path along
    // the way, then rides the same ServerBeginPull rail the ground-targeted
    // abilities use for the actual movement. E.g. Trample.
    private void ResolveChargeForward(AbilityData ability)
    {
        if (!TrySpendOrFizzle(ability)) return;

        Vector3 start = transform.position;
        Vector3 flatForward = transform.forward;
        flatForward.y = 0f;
        flatForward = flatForward.sqrMagnitude > 0.0001f ? flatForward.normalized : Vector3.forward;
        Vector3 end = start + flatForward * ability.ChargeForwardDistance;

        const float pathHitRadius = 2.5f; // how close to the charge line a target needs to be to get hit
        float damage = ResolveTotalDamage(ability);
        ForEachLivingTarget(includeCaster: false,
            candidate =>
            {
                Vector3 flatPos = candidate.transform.position;
                flatPos.y = start.y;
                return Vector3.Distance(flatPos, ClosestPointOnSegment(start, end, flatPos)) <= pathHitRadius;
            },
            candidate => candidate.Stats.ReceiveHit(DamageHit(ability, damage)));

        float duration = ability.ChargeForwardDistance / Mathf.Max(0.01f, ability.ChargeSpeed) + 0.5f;
        movement.ServerBeginPull(end, ability.ChargeSpeed, duration);
    }

    // Unit-targeted gap-closer: the caster charges to just short of the
    // target's position, then Effect is applied to the TARGET (not the
    // caster) - for support "peel" abilities. E.g. Team Up. All the usual
    // range/facing/LoS/mana checks already happened in ResolveAbility
    // before this is called.
    private void ResolveChargeToTarget(AbilityData ability, Targetable target, NetworkObject targetObject)
    {
        const float meleeClearance = 2f; // stop just short of the target instead of overlapping them
        Vector3 toTarget = targetObject.transform.position - transform.position;
        toTarget.y = 0f;
        Vector3 end = toTarget.magnitude > meleeClearance
            ? targetObject.transform.position - toTarget.normalized * meleeClearance
            : transform.position;

        float duration = Vector3.Distance(transform.position, end) / Mathf.Max(0.01f, ability.ChargeSpeed) + 0.5f;
        movement.ServerBeginPull(end, ability.ChargeSpeed, duration);

        target.Stats.ApplyEffect(ability.Effect, ability.DirectHitEffectDuration, OwnerClientId, HitSource.Ability);
    }

    // Unit-targeted: spawns a following GroundPatch centered on the target that
    // tracks their position for the ability's duration, reapplying Effect
    // to everyone caught inside - e.g. Arctic Winds. Reuses GroundEffectRadius
    // for the zone's radius and PatchDuration for how long it lasts (same
    // fields ground patches use for their own radius/lifetime).
    private void ResolveFollowingZone(AbilityData ability, NetworkObject targetObject)
    {
        GameObject instance = Instantiate(ability.FollowingZonePrefab, targetObject.transform.position, Quaternion.identity);
        instance.GetComponent<NetworkObject>().Spawn();
        // Same GroundPatch component a fixed fire/ice patch uses - just told
        // to follow the target and never affect this caster.
        instance.GetComponent<GroundPatch>().Initialize(ability.Effect, ability.PatchDuration, ability.GroundEffectRadius, OwnerClientId,
            follow: targetObject.transform, excludedId: NetworkObjectId);
    }

    private static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 point)
    {
        Vector3 ab = b - a;
        float sqrLen = ab.sqrMagnitude;
        if (sqrLen < 0.0001f) return a;
        float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / sqrLen);
        return a + ab * t;
    }

    [ServerRpc]
    private void CastGroundTargetedAbilityServerRpc(int slotIndex, Vector3 groundPosition, Vector2 facing)
    {
        if (slotIndex < 0 || slotIndex >= serverSlotAbilities.Length) return;

        AbilityData ability = serverSlotAbilities[slotIndex];
        if (ability == null || !ability.IsGroundTargeted) return;

        if (RejectIfDead(ability)) return;
        if (RejectIfNotReady(ability)) return;
        if (Vector3.Distance(transform.position, groundPosition) > ability.Range)
        {
            NotifyCastRejectedClientRpc(ability.Id, "Out of range");
            return;
        }
        if (!stats.HasEnoughMana(ability.ManaCost))
        {
            NotifyCastRejectedClientRpc(ability.Id, "Not enough mana");
            return;
        }

        // Mana is spent at resolve, not here - see ResolveGroundAbility.
        StartServerCast(ability, () => ResolveGroundAbility(ability, groundPosition, facing));
    }

    // No range/facing/LoS re-check at resolve time (unlike unit-targeted
    // abilities) - the point was already fixed and validated at cast start,
    // and a ground AoE has no single thing to lose sight of. Mana is still
    // spent here rather than at cast start, for consistency with
    // ResolveAbility (and so a class of failure this method might grow
    // later - e.g. a re-check - doesn't cost mana on fizzle).
    private void ResolveGroundAbility(AbilityData ability, Vector3 groundPosition, Vector2 facing)
    {
        if (ability.IsPersistentStructure)
        {
            ResolvePersistentStructure(ability, groundPosition, facing);
            return;
        }

        if (!TrySpendOrFizzle(ability)) return;

        if (ability.ForceSpeed <= 0f || ability.GroundEffectRadius <= 0f) return;

        // A push is implemented as "pull toward a point on the far side of
        // you" - ServerBeginPull only ever drives a character straight at
        // whatever point it's given, so the two directions share every line
        // of the actual movement code (including the movement-validator
        // suppression, which is the part worth not duplicating).
        const float pushClearance = 8f; // how far past the radius a push sends its victims
        float pushDistance = ability.GroundEffectRadius + pushClearance;

        ForEachLivingTarget(includeCaster: true, candidate => FlatOffset(candidate).magnitude <= ability.GroundEffectRadius, candidate =>
        {
            Vector3 offset = FlatOffset(candidate);

            Vector3 forceTarget;
            if (ability.PushAway)
            {
                Vector3 radial = offset.sqrMagnitude > 0.01f ? offset.normalized : Vector3.forward;
                forceTarget = groundPosition + radial * pushDistance;
            }
            else
            {
                forceTarget = groundPosition;
            }

            // Generous cap in case something never quite arrives (e.g.
            // blocked by terrain) - it simply regains control then.
            float duration = Vector3.Distance(candidate.transform.position, forceTarget) / ability.ForceSpeed + 0.5f;

            if (candidate.TryGetComponent(out PlayerMovement playerMovement))
            {
                playerMovement.ServerBeginPull(forceTarget, ability.ForceSpeed, duration);
            }
            else if (candidate.TryGetComponent(out EnemyAI enemyAi))
            {
                enemyAi.ServerBeginPull(forceTarget, ability.ForceSpeed, duration);
            }
        });

        Vector3 FlatOffset(Targetable candidate)
        {
            Vector3 offset = candidate.transform.position - groundPosition;
            offset.y = 0f;
            return offset;
        }
    }

    // Ground-targeted, no combat effect: spawns a StructurePrefab instance
    // for this ability - e.g. Summon Wall - evicting this caster's oldest one
    // once they already have AbilityData.MaxActiveStructures standing.
    // The prefab carries a SegmentedWall, oriented so its width axis is
    // perpendicular to the owner's facing at click time (sent with the cast,
    // since a ground-targeted cast only ever gives a point). It's placed on
    // the terrain height at the aimed X/Z, not the raycast hit's Y (which
    // can be a roof or prop), with the server deciding which segments a
    // cliff removes before spawn. Anything that fizzles does so before any
    // wall is removed.
    private void ResolvePersistentStructure(AbilityData ability, Vector3 groundPosition, Vector2 facing)
    {
        if (ability.StructurePrefab == null || !ability.StructurePrefab.TryGetComponent(out SegmentedWall _))
        {
            NotifyCastFizzledClientRpc("Structure not configured yet");
            return;
        }
        // A NaN/Infinity point would poison every terrain sample and the
        // spawn position; refuse it before mana is spent.
        if (!float.IsFinite(groundPosition.x) || !float.IsFinite(groundPosition.y) || !float.IsFinite(groundPosition.z))
        {
            NotifyCastFizzledClientRpc("Invalid target");
            return;
        }
        if (!TrySpendOrFizzle(ability)) return;

        Quaternion rotation = WallSegmentLayout.FacingRotation(facing, transform.forward);

        SegmentedWall.WallSpec spec = SegmentedWall.BuildSpec(
            groundPosition, rotation * Vector3.right, ability.StructureWidth, ability.StructureHeight, ability.StructureThickness, out float anchorY);

        GameObject instance = Instantiate(ability.StructurePrefab, new Vector3(groundPosition.x, anchorY, groundPosition.z), rotation);
        instance.GetComponent<SegmentedWall>().Configure(spec);
        NetworkObject instanceObject = instance.GetComponent<NetworkObject>();

        if (!activeStructures.TryGetValue(ability, out List<NetworkObject> structures))
        {
            structures = new List<NetworkObject>();
            activeStructures[ability] = structures;
        }

        // Dead entries (already despawned) are dropped first so they can't
        // push a live wall out; then the oldest live ones make room.
        foreach (NetworkObject evicted in StructureEviction.MakeRoom(structures, ability.MaxActiveStructures, s => s == null || !s.IsSpawned))
        {
            if (evicted != null && evicted.IsSpawned) evicted.Despawn();
        }

        instanceObject.Spawn();
        structures.Add(instanceObject);
    }

    private bool IsWithinFacingCone(NetworkObject targetObject)
    {
        return FacingCone.IsWithin(transform, targetObject.transform.position, facingConeAngle);
    }

}
