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

    // Server-side policing of the replicated transform (see MovementValidator).
    // A violation snaps the client back; repeated ones get it disconnected.
    [SerializeField] private int maxStrikes = 5;
    [SerializeField] private float strikeForgetSeconds = 30f;
    [SerializeField] private float correctionGraceSeconds = 1f;

    private CharacterController controller;
    private CharacterStats stats;
    private NetworkTransform networkTransform;
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

        if (MovementInput.WasPressed(MovementAction.Jump)) pendingJump = true;

        if (Input.GetMouseButton(1))
        {
            pendingLookDeltaYaw += Input.GetAxis("Mouse X") * lookSensitivity;
        }
    }

    private void FixedUpdate()
    {
        // The host's own player is trusted; every remote owner is policed.
        if (IsServer && !IsOwner) ValidateReplicatedMovement();

        if (!IsOwner) return;

        bool grounded = controller.isGrounded;

        // The body always turns with look input, grounded or airborne.
        if (pendingLookDeltaYaw != 0f)
        {
            transform.Rotate(Vector3.up, pendingLookDeltaYaw);
            pendingLookDeltaYaw = 0f;
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
        else if (grounded)
        {
            Vector3 moveDirection = transform.forward * forwardInput + transform.right * strafeInput;
            if (moveDirection.sqrMagnitude > 1f) moveDirection.Normalize();

            horizontalVelocity = moveDirection * stats.SyncedRunSpeed.Value;
            airborneVelocity = horizontalVelocity;
        }
        else
        {
            // Airborne: keep whatever momentum existed at liftoff, ignoring
            // subsequent turning/input changes to direction.
            horizontalVelocity = airborneVelocity;
        }

        if (grounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        if (pendingJump && grounded)
        {
            verticalVelocity = jumpSpeed;
        }
        pendingJump = false;

        verticalVelocity += gravity * Time.fixedDeltaTime;

        Vector3 motion = horizontalVelocity + Vector3.up * verticalVelocity;
        controller.Move(motion * Time.fixedDeltaTime);
    }

    // Called server-side (e.g. by a resolving Vacuum cast) to drag this
    // player toward towardPosition for up to duration seconds, or until
    // they arrive. Overrides normal input for that window.
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
