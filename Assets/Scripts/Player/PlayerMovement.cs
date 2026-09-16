using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// Owner-authoritative movement: the owning client moves its own
// CharacterController and NetworkTransform (in Owner authority mode)
// replicates the result. Combat stays server-authoritative - the server
// only ever reads the replicated position/rotation for range and facing
// checks. Chosen over server-authoritative + prediction because this is a
// friends-only game: instant response matters, position cheating doesn't.
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(CharacterStats))]
[RequireComponent(typeof(NetworkTransform))]
public class PlayerMovement : NetworkBehaviour
{
    [SerializeField] private float lookSensitivity = 30f;
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float jumpSpeed = 8f;

    // Boots of Lightness: pressing Jump again while airborne (once per
    // airtime - resets on landing) suspends gravity for this long, with
    // WASD still steering normally the whole time (unlike a normal jump/
    // fall, which locks in launch-time momentum - see the FixedUpdate
    // horizontal-velocity branch below).
    [SerializeField] private float hoverDuration = 2f;

    // Server-side policing of the replicated transform (see MovementValidator).
    // A violation snaps the client back; repeated ones get it disconnected.
    [SerializeField] private int maxStrikes = 5;
    [SerializeField] private float strikeForgetSeconds = 30f;
    [SerializeField] private float correctionGraceSeconds = 1f;

    // Longer than correctionGraceSeconds on purpose: a fresh spawn has to
    // get through scene load + the owner's own terrain-snap + a full
    // network round trip before the server's view of this player's
    // position is trustworthy, which is a slower process than recovering
    // from a single mid-game correction.
    [SerializeField] private float spawnGraceSeconds = 2f;

    private CharacterController controller;
    private CharacterStats stats;
    private NetworkTransform networkTransform;
    private CharacterAppearance appearance;
    private float verticalVelocity;

    private readonly MovementValidator validator = new MovementValidator();
    private int strikes;
    private float lastStrikeTime;
    private float validationResumeTime;

    // Input is gathered every rendered frame and consumed once per physics
    // step. Mouse deltas are summed and the jump is latched so nothing is
    // dropped when several frames land between two FixedUpdates; held keys
    // just keep their latest value, a one-frame tap being imperceptible.
    private float forwardInput;
    private float strafeInput;
    private float pendingLookDeltaYaw;
    private bool pendingJump;
    private bool pendingHoverActivation;

    // Owner-local hover state (see Boots of Lightness above).
    // hoverAvailable resets the instant the player lands, so it can only
    // be triggered once per time spent airborne, not chained repeatedly.
    private bool isHovering;
    private bool hoverAvailable = true;
    private float hoverEndTime;

    // Horizontal momentum locked in at the moment of leaving the ground -
    // turning mid-air changes facing, not trajectory, same as real jumping
    // physics. Recomputed every grounded frame so it's always current the
    // instant a jump actually leaves the ground.
    private Vector3 airborneVelocity;

    // Owner-local only - toggled by the AutoRun key, and cancelled either
    // the same way, by any manual forward/backward input (mouse-both counts
    // as forward), or by opening the menu, so the player is never fighting
    // auto-run to regain control.
    private bool autoRun;

    // Server-initiated external movement (e.g. Vacuum's pull): while active,
    // normal input is ignored and the character is driven straight toward
    // pullTargetPosition instead. Set directly server-side (so the movement
    // validator can ignore this window - see ValidateReplicatedMovement)
    // and mirrored to the owner via ClientRpc so it can actually move.
    private Vector3 pullTargetPosition;
    private float pullSpeed;
    private float pullEndTime;
    private bool IsPulling => Time.time < pullEndTime;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        stats = GetComponent<CharacterStats>();
        networkTransform = GetComponent<NetworkTransform>();
        appearance = GetComponent<CharacterAppearance>();
    }

    public override void OnNetworkSpawn()
    {
        // The owner's own spawn-time terrain snap (see PlayerRespawn) needs
        // a round trip to replicate back to the server before it's fair to
        // judge this player's position. Without this, a remote (non-host)
        // player could get their very first FixedUpdate validated against
        // the prefab's raw baked spawn position - which, on a terrain
        // that's since been sculpted, can look like it's below ground - and
        // get "corrected" right back to that same bad position, landing
        // them under the terrain with nothing to stand on.
        validationResumeTime = Time.time + spawnGraceSeconds;
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (MainMenu.IsOpen)
        {
            autoRun = false;
            forwardInput = 0f;
            strafeInput = 0f;
            return;
        }

        if (MovementInput.WasPressed(MovementAction.AutoRun))
        {
            autoRun = !autoRun;
        }

        bool forwardHeld = MovementInput.IsHeld(MovementAction.Forward) || (Input.GetMouseButton(0) && Input.GetMouseButton(1));
        bool backwardHeld = MovementInput.IsHeld(MovementAction.Backward);
        if (autoRun && (forwardHeld || backwardHeld))
        {
            autoRun = false;
        }

        forwardInput = 0f;
        if (autoRun || forwardHeld) forwardInput = 1f;
        else if (backwardHeld) forwardInput = -1f;

        strafeInput = 0f;
        if (MovementInput.IsHeld(MovementAction.StrafeRight)) strafeInput += 1f;
        if (MovementInput.IsHeld(MovementAction.StrafeLeft)) strafeInput -= 1f;

        if (MovementInput.WasPressed(MovementAction.Jump))
        {
            pendingJump = true; // only takes effect next FixedUpdate if grounded
            pendingHoverActivation = true; // only takes effect if airborne and eligible
        }

        if (Input.GetMouseButton(1))
        {
            pendingLookDeltaYaw += Input.GetAxis("Mouse X") * lookSensitivity * LookSensitivityScale.Value;
        }
    }

    private void FixedUpdate()
    {
        // The host's own player is trusted; every remote owner is policed.
        if (IsServer && !IsOwner) ValidateReplicatedMovement();

        if (!IsOwner) return;

        bool grounded = controller.isGrounded;

        if (grounded)
        {
            hoverAvailable = true;
            isHovering = false;
        }

        // Resolved before horizontal velocity below, since hovering changes
        // how that's computed this same tick.
        if (pendingHoverActivation && !grounded && !isHovering && hoverAvailable && HasHoverBoots)
        {
            isHovering = true;
            hoverAvailable = false;
            hoverEndTime = Time.time + hoverDuration;
        }
        pendingHoverActivation = false;

        if (isHovering && Time.time >= hoverEndTime)
        {
            isHovering = false;
        }

        // The body always turns with look input, grounded or airborne.
        if (pendingLookDeltaYaw != 0f)
        {
            transform.Rotate(Vector3.up, pendingLookDeltaYaw);
            pendingLookDeltaYaw = 0f;
        }

        // Movement-facing: the visual rig (CharacterAppearance
        // .ActiveRigRoot) faces the direction of net WASD movement, but
        // only while forwardInput is forward or neutral - pure strafe
        // (A/D alone) faces 90 degrees, forward+strafe (W+A/W+D) blends
        // to a smooth 45 degrees, plain forward faces straight ahead.
        // Any backward component (S alone, or S+A/S+D) instead always
        // faces forward, full stop, ignoring strafe entirely for this
        // calculation - the character keeps facing forward rather than
        // spinning to face away from the camera (or to some back-left/
        // back-right angle) while backpedaling in any combination. Actual
        // movement itself is unaffected either way - only which way the
        // model visibly faces while doing it. Idle (no input) also faces
        // forward (Atan2(0,0) is 0 by convention).
        // Level-triggered - recomputed from live input every tick, no
        // accumulation possible - and never touches this transform,
        // which is also what movement direction, the camera, and combat
        // facing (FacingCone) all read: purely cosmetic, doesn't affect
        // movement direction, the camera, or combat facing/hit
        // detection - only what the character visibly looks like it's
        // facing. Owner-local only, like the rest of this method; other
        // clients don't see this yet (same limitation as the still-open
        // cross-client animation sync issue).
        Transform rigRoot = appearance != null ? appearance.ActiveRigRoot : null;
        if (rigRoot != null)
        {
            float facingYaw = forwardInput < 0f
                ? 0f
                : Mathf.Atan2(strafeInput, forwardInput) * Mathf.Rad2Deg;
            rigRoot.localRotation = Quaternion.Euler(0f, facingYaw, 0f);
        }

        Vector3 horizontalVelocity;
        if (IsPulling)
        {
            Vector3 toPullTarget = pullTargetPosition - transform.position;
            toPullTarget.y = 0f;
            if (toPullTarget.magnitude < 0.3f)
            {
                horizontalVelocity = Vector3.zero;
                pullEndTime = Time.time; // arrived - hand control back immediately
            }
            else
            {
                horizontalVelocity = toPullTarget.normalized * pullSpeed;
            }
            airborneVelocity = horizontalVelocity;
        }
        else if (grounded || isHovering)
        {
            // Hovering steers exactly like being grounded (Boots of
            // Lightness - "player maintains WASD controls") instead of the
            // launch-momentum lock a normal fall uses below.
            Vector3 moveDirection = transform.forward * forwardInput + transform.right * strafeInput;
            if (moveDirection.sqrMagnitude > 1f) moveDirection.Normalize();

            horizontalVelocity = moveDirection * stats.SyncedRunSpeed.Value;
            airborneVelocity = horizontalVelocity;
        }
        else
        {
            // Airborne, not hovering: keep whatever momentum existed at
            // liftoff, ignoring subsequent turning/input changes to direction.
            horizontalVelocity = airborneVelocity;
        }

        // Owner-authoritative, same as everything else in this method - the
        // NetworkAnimator (Authority Mode: Owner) replicates this to every
        // other client, matching the same "speed" parameter/threshold
        // convention every mob's Animator Controller already uses.
        appearance?.ActiveAnimator?.SetFloat("speed", horizontalVelocity.magnitude);

        if (grounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        if (pendingJump && grounded)
        {
            verticalVelocity = jumpSpeed;
        }
        pendingJump = false;

        if (isHovering)
        {
            verticalVelocity = 0f; // suspended - no gravity while hovering
        }
        else
        {
            verticalVelocity += gravity * Time.fixedDeltaTime;
        }

        Vector3 motion = horizontalVelocity + Vector3.up * verticalVelocity;
        controller.Move(motion * Time.fixedDeltaTime);
    }

    // Owner-local check of the player's own known loadout - matches the
    // same "the owner already trusts itself for movement" model as every
    // other input this script reads (RunSpeed, jump, etc.); no server
    // round trip needed since movement here is owner-authoritative.
    private bool HasHoverBoots => ProfileStore.Current.GetGear(GearSlot.Boots)?.GrantsAirHover ?? false;

    // Called server-side (e.g. by a resolving ground-targeted ability) to
    // drive this player straight toward towardPosition for up to duration
    // seconds, or until they arrive. Overrides normal input for that
    // window. Direction-agnostic - a "push away" effect just passes a
    // towardPosition on the far side of the victim (see
    // PlayerAbilities.ResolveGroundAbility).
    public void ServerBeginPull(Vector3 towardPosition, float speed, float duration)
    {
        if (!IsServer) return;

        // Harmless for the host's own player (validation never runs for
        // it); for a remote owner this is what stops the pull itself from
        // being flagged as a speed/teleport violation.
        pullEndTime = Time.time + duration;
        BeginPullClientRpc(towardPosition, speed, duration);
    }

    [ClientRpc]
    private void BeginPullClientRpc(Vector3 towardPosition, float speed, float duration)
    {
        if (!IsOwner) return;
        pullTargetPosition = towardPosition;
        pullSpeed = speed;
        pullEndTime = Time.time + duration;
    }

    // Called server-side (e.g. by a resolving Recall cast) to instantly
    // move this player to position, bypassing normal movement entirely.
    public void ServerTeleportTo(Vector3 position)
    {
        if (!IsServer) return;

        // Same safety as a validation correction (see below): re-baseline
        // immediately and pause checks briefly, so this server-initiated
        // jump isn't itself read as a teleport violation and undone.
        validator.Reset(position, Time.time, stats.RunSpeed.Value);
        validationResumeTime = Time.time + correctionGraceSeconds;
        TeleportToClientRpc(position);
    }

    [ClientRpc]
    private void TeleportToClientRpc(Vector3 position)
    {
        if (!IsOwner) return;
        TeleportTo(position);
    }

    private void ValidateReplicatedMovement()
    {
        if (Time.time < validationResumeTime) return;

        if (Time.time < pullEndTime)
        {
            // Being pulled - keep re-baselining so the check window starts
            // fresh (from wherever the pull leaves them) once it ends,
            // instead of reading the pull itself as a violation.
            validator.Reset(transform.position, Time.time, stats.RunSpeed.Value);
            return;
        }

        Vector3 position = transform.position;
        float feetY = position.y + controller.center.y - controller.height * 0.5f;
        Terrain terrain = Terrain.activeTerrain;
        float groundY = terrain != null ? terrain.SampleHeight(position) + terrain.transform.position.y : float.NaN;

        MovementValidator.Verdict verdict = validator.Check(position, feetY, groundY, Time.time, stats.RunSpeed.Value);
        if (verdict == MovementValidator.Verdict.Ok) return;

        if (Time.time - lastStrikeTime > strikeForgetSeconds) strikes = 0;
        strikes++;
        lastStrikeTime = Time.time;
        Debug.LogWarning($"[PlayerMovement] client {OwnerClientId} movement rejected ({verdict}), strike {strikes}/{maxStrikes}");

        if (strikes >= maxStrikes)
        {
            NetworkManager.DisconnectClient(OwnerClientId, "Movement validation failed repeatedly");
            return;
        }

        // Skip checks until the correction has had time to round-trip, so
        // the states still in flight from before it don't count as strikes.
        validationResumeTime = Time.time + correctionGraceSeconds;
        CorrectPositionClientRpc(validator.LastAcceptedPosition);
    }

    [ClientRpc]
    private void CorrectPositionClientRpc(Vector3 position)
    {
        if (!IsOwner) return;
        TeleportTo(position);
    }

    // Only the transform authority (the owner) may teleport; the server
    // asks for it via PlayerRespawn's ClientRpc or a validation correction.
    public void TeleportTo(Vector3 position)
    {
        if (!IsOwner) return;

        controller.enabled = false;
        transform.position = position;
        controller.enabled = true;

        verticalVelocity = 0f;
        airborneVelocity = Vector3.zero;
        networkTransform.Teleport(position, transform.rotation, transform.localScale);
    }
}
