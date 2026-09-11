using Unity.Netcode;
using UnityEngine;

public class PlayerCamera : NetworkBehaviour
{
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private AudioListener audioListener;
    [SerializeField] private float pitchSensitivity = 30f;
    [SerializeField] private float minPitch = -40f;
    [SerializeField] private float maxPitch = 75f;
    [SerializeField] private float freeLookYawSensitivity = 30f;

    private float pitch;
    private float freeLookYaw;
    private bool cursorLocked;

    public Camera Camera => playerCamera;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            playerCamera.gameObject.SetActive(false);
            return;
        }

        if (Camera.main != null && Camera.main.gameObject != playerCamera.gameObject)
        {
            Camera.main.gameObject.SetActive(false);
        }

        playerCamera.gameObject.SetActive(true);
    }

    private void Update()
    {
        if (!IsOwner) return;

        bool menuOpen = MainMenu.IsOpen;
        bool turning = !menuOpen && Input.GetMouseButton(1);
        // Left-click alone: camera-only free look. Rotates the camera
        // pivot, not the player's own transform (only right-click drives
        // that, via PlayerMovement), so looking around never changes which
        // way the player is actually facing/moving.
        bool freeLooking = !menuOpen && Input.GetMouseButton(0) && !turning;

        // While dragging to look, hide and lock the cursor (MMO style). This
        // also gives the Game view exclusive input in the Editor, so keys
        // like Tab can't move focus elsewhere and drop the held button.
        SetCursorLocked(turning || freeLooking);

        if (turning || freeLooking)
        {
            pitch -= Input.GetAxis("Mouse Y") * pitchSensitivity;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        if (freeLooking)
        {
            freeLookYaw += Input.GetAxis("Mouse X") * freeLookYawSensitivity;
        }
        else
        {
            // Snap the camera back behind the player once free look ends.
            freeLookYaw = 0f;
        }

        cameraPivot.localRotation = Quaternion.Euler(pitch, freeLookYaw, 0f);
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner) SetCursorLocked(false);
    }

    private void SetCursorLocked(bool locked)
    {
        if (cursorLocked == locked) return;
        cursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
