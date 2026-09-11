using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(CharacterStats))]
public class PlayerMovement : NetworkBehaviour
{
    [SerializeField] private float lookSensitivity = 30f;
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float jumpSpeed = 8f;

    private CharacterController controller;
    private CharacterStats stats;
    private float pendingLookDeltaYaw;
    private float verticalVelocity;

    private float serverForwardInput;
    private float serverStrafe;
    private float serverLookDeltaYaw;
    private bool serverJumpRequested;

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
    }

    private void Update()
    {
        if (!IsOwner) return;

        // Keep sending (zeroed) input while the menu is up - the server holds
        // whatever it last received, so going silent would leave the player
        // running off in whatever direction they were last moving.
        if (MainMenu.IsOpen)
        {
            autoRun = false;
            SubmitMovementServerRpc(0f, 0f, 0f, false);
            return;
        }

        if (MovementBindings.WasPressed(MovementAction.AutoRun))
        {
            autoRun = !autoRun;
        }

        bool forwardHeld = MovementBindings.IsHeld(MovementAction.Forward) || (Input.GetMouseButton(0) && Input.GetMouseButton(1));
        bool backwardHeld = MovementBindings.IsHeld(MovementAction.Backward);
        if (autoRun && (forwardHeld || backwardHeld))
        {
            autoRun = false;
        }

        float forwardInput = 0f;
        if (autoRun || forwardHeld) forwardInput = 1f;
        else if (backwardHeld) forwardInput = -1f;

        float strafe = 0f;
        if (MovementBindings.IsHeld(MovementAction.StrafeRight)) strafe += 1f;
        if (MovementBindings.IsHeld(MovementAction.StrafeLeft)) strafe -= 1f;

        bool jumpRequested = MovementBindings.WasPressed(MovementAction.Jump);

        float lookDeltaYaw = 0f;
        if (Input.GetMouseButton(1))
        {
            lookDeltaYaw = Input.GetAxis("Mouse X") * lookSensitivity;
        }

        SubmitMovementServerRpc(forwardInput, strafe, lookDeltaYaw, jumpRequested);
    }

    [ServerRpc]
    private void SubmitMovementServerRpc(float forwardInput, float strafe, float lookDeltaYaw, bool jumpRequested)
    {
        serverForwardInput = forwardInput;
        serverStrafe = strafe;
        serverLookDeltaYaw = lookDeltaYaw;
        if (jumpRequested) serverJumpRequested = true;
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        bool grounded = controller.isGrounded;

        // The body always turns with look input, grounded or airborne.
        if (serverLookDeltaYaw != 0f)
        {
            transform.Rotate(Vector3.up, serverLookDeltaYaw);
        }

        Vector3 horizontalVelocity;
        if (grounded)
        {
            Vector3 moveDirection = transform.forward * serverForwardInput;
            moveDirection += transform.right * serverStrafe;
            if (moveDirection.sqrMagnitude > 1f) moveDirection.Normalize();

            horizontalVelocity = moveDirection * stats.RunSpeed.Value;
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

        if (serverJumpRequested && grounded)
        {
            verticalVelocity = jumpSpeed;
        }
        serverJumpRequested = false;

        verticalVelocity += gravity * Time.fixedDeltaTime;

        Vector3 motion = horizontalVelocity + Vector3.up * verticalVelocity;
        controller.Move(motion * Time.fixedDeltaTime);
    }
}
