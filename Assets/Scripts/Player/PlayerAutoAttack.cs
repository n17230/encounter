using System.Collections;
using Unity.Netcode;
using UnityEngine;

// MMO-style auto-attack: right-clicking a mob targets it and arms the
// attack (the AutoAttack key toggles it too); from then on the server swings the equipped main-hand weapon
// (or Fists) every SwingInterval whenever the target is in range and in
// front of the player. It stays armed while closing distance and follows
// target changes (Tab), and disarms when the target is dropped or dies.
[RequireComponent(typeof(PlayerTargeting))]
[RequireComponent(typeof(CharacterStats))]
[RequireComponent(typeof(CharacterEquipment))]
[RequireComponent(typeof(CharacterAppearance))]
[RequireComponent(typeof(CharacterWeaponVisual))]
public class PlayerAutoAttack : NetworkBehaviour
{
    [SerializeField] private WeaponData unarmedWeapon;
    [SerializeField] private float facingConeAngle = 120f;

    private PlayerTargeting targeting;
    private CharacterEquipment equipment;
    private CharacterStats stats;
    private CharacterAppearance appearance;
    private CharacterWeaponVisual weaponVisual;
    private Targetable self;

    // Owner-side state, mirrored to the server via SetAutoAttackServerRpc.
    public bool IsArmed { get; private set; }
    private Targetable armedFor;

    // Server-authoritative: mirrors PlayerAbilities' own cast-lock timestamp
    // (serverCastEndTime), set externally by PlayerAbilities (which already
    // holds a reference to this component) rather than this class reaching
    // back into PlayerAbilities - keeps the existing one-directional
    // dependency. A timestamp rather than a bool: a same-frame recast just
    // overwrites it again (last-write-wins, same as serverCastEndTime
    // itself), so no extra race-guard is needed, and it needs no explicit
    // "clear" call anywhere - the lock simply expires once Time.time passes
    // it. See FixedUpdate.
    public float ServerCastLockUntil { get; set; }

    private bool serverArmed;
    private ulong serverTargetId;
    private float nextSwingTime;
    // Whether this swing cycle's windup animation has already been fired -
    // reset once the swing actually resolves, so a weapon with a nonzero
    // AttackAnimationLeadTime only plays it once per swing.
    private bool windupPlayed;

    private void Awake()
    {
        targeting = GetComponent<PlayerTargeting>();
        equipment = GetComponent<CharacterEquipment>();
        stats = GetComponent<CharacterStats>();
        appearance = GetComponent<CharacterAppearance>();
        weaponVisual = GetComponent<CharacterWeaponVisual>();
        self = GetComponent<Targetable>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner) targeting.AttackRequested += Arm;
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner) targeting.AttackRequested -= Arm;
    }

    // What the local player would swing with - for the HUD.
    public WeaponData LocalWeapon
    {
        get
        {
            ItemData mainHand = ProfileStore.Current.GetEquipment(EquipmentSlot.MainHand);
            return mainHand != null && mainHand.Weapon != null ? mainHand.Weapon : unarmedWeapon;
        }
    }

    // Server-authoritative equivalent of LocalWeapon - what this character
    // actually swings with right now (equipped main hand, or fists). Used
    // by weapon-scaling abilities (e.g. Reaper's Wheel, Cleave).
    public WeaponData ResolvedWeapon => equipment.MainHandWeapon != null ? equipment.MainHandWeapon : unarmedWeapon;

    private void Arm(Targetable target)
    {
        IsArmed = true;
        armedFor = target;
        SendState();
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (!MainMenu.IsOpen && MovementInput.WasPressed(MovementAction.AutoAttack))
        {
            if (IsArmed) Disarm();
            else if (targeting.CurrentTarget != null) Arm(targeting.CurrentTarget);
        }

        if (!IsArmed) return;

        Targetable target = targeting.CurrentTarget;
        if (target == null || target.Stats == null || target.Stats.CurrentHealth.Value <= 0f)
        {
            Disarm();
            return;
        }

        if (target != armedFor)
        {
            armedFor = target;
            SendState();
        }
    }

    private void Disarm()
    {
        IsArmed = false;
        armedFor = null;
        SetAutoAttackServerRpc(false, 0);
    }

    private void SendState()
    {
        // self is never a valid auto-attack target - only reachable via the
        // backtick self-target hotkey, whether arming fresh or following a
        // target change while already armed.
        NetworkObject targetObject = armedFor != null && armedFor != self ? armedFor.GetComponent<NetworkObject>() : null;
        if (targetObject == null)
        {
            Disarm();
            return;
        }
        SetAutoAttackServerRpc(true, targetObject.NetworkObjectId);
    }

    [ServerRpc]
    private void SetAutoAttackServerRpc(bool armed, ulong targetNetworkObjectId)
    {
        serverArmed = armed;
        serverTargetId = targetNetworkObjectId;
        // A new arm/target-change means any windup already played belonged
        // to a swing cycle that's no longer happening - otherwise the very
        // next cycle's windup could be silently skipped (already "played").
        windupPlayed = false;
    }

    private void FixedUpdate()
    {
        if (!IsServer || !serverArmed) return;

        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(serverTargetId, out NetworkObject targetObject))
        {
            serverArmed = false;
            windupPlayed = false;
            return;
        }

        Targetable target = targetObject.GetComponent<Targetable>();
        if (target == null || target.Stats == null || target.Stats.CurrentHealth.Value <= 0f)
        {
            serverArmed = false;
            windupPlayed = false;
            return;
        }

        WeaponData weapon = equipment.MainHandWeapon != null ? equipment.MainHandWeapon : unarmedWeapon;
        if (weapon == null) return;

        // Casting interrupts and blocks auto-attack entirely - continuously
        // pushing nextSwingTime out for as long as the lock is active means
        // the instant it expires, a full fresh SwingInterval is required
        // before the next swing, rather than resuming from wherever the
        // timer was or firing immediately because time already passed a
        // stale nextSwingTime.
        if (Time.time < ServerCastLockUntil)
        {
            windupPlayed = false;
            nextSwingTime = Time.time + weapon.SwingInterval;
            return;
        }

        Vector3 toTarget = targetObject.transform.position - transform.position;
        toTarget.y = 0f;
        bool inRange = toTarget.magnitude <= weapon.Range;
        bool inFacing = FacingCone.IsWithin(transform, targetObject.transform.position, facingConeAngle);
        // A ranged weapon (e.g. Hunter's Bow) can't reach a target standing
        // inside an active Arcane Shield dome, and - same rule abilities
        // already follow - can't shoot through walls. Melee weapons are
        // unaffected by either (short-circuited, so no LoS raycast for them).
        bool hasShot = !weapon.IsRanged || (!ArcaneShieldZones.Blocks(targetObject.transform.position)
            && CombatPhysics.HasLineOfSight(transform.position, targetObject.transform.position));
        bool canSwingNow = inRange && inFacing && hasShot;

        // Fire the windup animation AttackAnimationLeadTime seconds before
        // the hit itself resolves, so a long wind-up (e.g. drawing a bow)
        // finishes exactly as the hit lands instead of both firing in the
        // same instant. Still gated on canSwingNow, so it doesn't play while
        // clearly out of position - 0 lead time (every other weapon) makes
        // this fire at the exact same tick as the resolve below, identical
        // to before this field existed.
        if (!windupPlayed && canSwingNow && Time.time >= nextSwingTime - weapon.AttackAnimationLeadTime)
        {
            windupPlayed = true;
            // Only weapon-pose categories with an actual Attack_* state
            // wired in CharacterIdle_M/F.controller should fire this - other
            // pose types just have no matching AnyState transition yet, so
            // the trigger would sit unconsumed on the Animator until one did.
            if (weapon.PoseType == WeaponPoseType.TwoHand || weapon.PoseType == WeaponPoseType.Bow) PlayAttackSwingClientRpc();
            // Cosmetic animation choice, independent of PoseType and of
            // whether the swing actually heals - see WeaponData
            // .PlaysHealSwingAnimation.
            if (weapon.PlaysHealSwingAnimation) PlayHealSwingClientRpc();
        }

        if (Time.time < nextSwingTime) return;
        if (!canSwingNow) return; // armed, waiting to get in reach/facing/LoS

        nextSwingTime = Time.time + weapon.SwingInterval;
        windupPlayed = false;
        bool isHealingSwing = weapon.HealAmount > 0f;
        target.Stats.ReceiveHit(new HitInfo
        {
            Damage = isHealingSwing ? 0f : weapon.Damage + stats.WeaponDamageBonus.Value,
            Heal = weapon.HealAmount,
            AttackerClientId = OwnerClientId,
            Source = weapon.BasicAttackSource,
            Effect = weapon.Effect,
        });
        if (weapon.ArrowPrefab != null) PlayArrowShotClientRpc(targetObject.transform.position + Vector3.up);
    }

    // The swing itself is decided server-side (combat is server-authoritative),
    // but the Animator's NetworkAnimator on this rig is owner-authoritative
    // (movement is owner-authoritative) - so the server tells the owner a
    // swing happened, and the owner's own SetTrigger call is what actually
    // replicates to everyone else, same pattern NotifyCastRejectedClientRpc
    // uses for owner-only reactions to a server decision.
    [ClientRpc]
    private void PlayAttackSwingClientRpc()
    {
        if (!IsOwner) return;
        appearance.ActiveAnimator?.SetTrigger("attack");
    }

    // Same owner-authoritative-NetworkAnimator pattern as
    // PlayAttackSwingClientRpc, for a healing weapon's swing - see
    // WeaponData.HealAmount.
    [ClientRpc]
    private void PlayHealSwingClientRpc()
    {
        if (!IsOwner) return;
        appearance.ActiveAnimator?.SetTrigger("castHeal");
    }

    // Resolves the equipped MainHand weapon the same way every client can -
    // via the already-synced MainHandItemId, same lookup
    // CharacterWeaponVisual.UpdateHeldModel uses for rendering the model
    // itself. Deliberately NOT equipment.MainHandWeapon/LocalWeapon: the
    // former reads a server-only cache, the latter reads the OWNER's own
    // local profile - neither is valid for an arbitrary client rendering a
    // remote character from a ClientRpc.
    private WeaponData ResolveSyncedMainHandWeapon()
    {
        string itemId = equipment.MainHandItemId.Value.ToString();
        return !string.IsNullOrEmpty(itemId) ? GameDatabase.GetItem(itemId)?.Weapon : null;
    }

    // Purely cosmetic, seen by everyone (no IsOwner guard - unlike
    // PlayAttackSwingClientRpc, this is a visible model, not an Animator
    // trigger relayed by the owner's own NetworkAnimator) - same
    // reasoning CharacterWeaponVisual uses for the weapon model itself.
    // The hit has already landed by this point regardless (see
    // FixedUpdate) - this is purely the arrow's cosmetic flight from the
    // bow to the target, spawned fresh rather than nocked/shown during the
    // windup.
    [ClientRpc]
    private void PlayArrowShotClientRpc(Vector3 targetPosition)
    {
        WeaponData weapon = ResolveSyncedMainHandWeapon();
        if (weapon == null || weapon.ArrowPrefab == null) return;

        Transform bowModel = weaponVisual.MainHandModelTransform;
        Vector3 spawnPosition = bowModel != null
            ? bowModel.TransformPoint(weapon.ArrowSpawnOffset)
            : transform.position + Vector3.up * 1.5f;
        GameObject arrow = Instantiate(weapon.ArrowPrefab, spawnPosition, Quaternion.identity);

        if (weapon.ArrowAnimationStartOffset > 0f)
        {
            ParticleSystem rootParticles = arrow.GetComponent<ParticleSystem>();
            if (rootParticles != null) rootParticles.Simulate(weapon.ArrowAnimationStartOffset, withChildren: true, restart: true);
        }

        float speed = weapon.ArrowMissileSpeed > 0f ? weapon.ArrowMissileSpeed : 1f;
        float duration = Vector3.Distance(spawnPosition, targetPosition) / speed;

        // Safety net: if this character (and therefore the coroutine below)
        // is destroyed/despawns mid-flight - e.g. the shooter disconnects -
        // the coroutine is killed without ever reaching its own
        // Destroy(arrow), leaking the now-orphaned, unparented arrow
        // forever. This scheduled destroy doesn't depend on that
        // GameObject's lifetime at all, so it cleans the arrow up
        // regardless. Redundant (and harmless - Destroy on an
        // already-destroyed object is a no-op) if the coroutine finishes
        // normally first.
        Destroy(arrow, duration + 0.1f);
        StartCoroutine(FlyArrowVisual(arrow, targetPosition, duration, weapon.ArrowModelRotationOffset));
    }

    private IEnumerator FlyArrowVisual(GameObject arrow, Vector3 targetPosition, float duration, Vector3 modelRotationOffset)
    {
        Vector3 start = arrow.transform.position;
        // A zero-length direction (point-blank shot, start == target) would
        // make LookRotation log a Unity console error - just keep whatever
        // rotation the arrow already had in that case.
        Vector3 direction = targetPosition - start;
        if (direction.sqrMagnitude > 0.0001f)
        {
            // modelRotationOffset corrects for the imported model's own
            // authored "forward" axis not matching LookRotation's +Z
            // convention - see WeaponData.ArrowModelRotationOffset.
            arrow.transform.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(modelRotationOffset);
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            arrow.transform.position = Vector3.Lerp(start, targetPosition, elapsed / duration);
            yield return null;
        }

        Destroy(arrow);
    }
}
