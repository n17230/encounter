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

    private CharacterController controller;
    private CharacterStats stats;
    private NetworkTransform networkTransform;
    private float verticalVelocity;

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
        if (!IsOwner) return;

        bool grounded = controller.isGrounded;

        // The body always turns with look input, grounded or airborne.
        if (pendingLookDeltaYaw != 0f)
        {
            transform.Rotate(Vector3.up, pendingLookDeltaYaw);
            pendingLookDeltaYaw = 0f;
        }

        Vector3 horizontalVelocity;
        if (grounded)
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

    // Only the transform authority (the owner) may teleport; the server
    // asks for it via PlayerRespawn's ClientRpc.
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
