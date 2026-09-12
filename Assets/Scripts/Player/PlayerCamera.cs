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

        if (turning || freeLooking)
        {
            pitch -= Input.GetAxis("Mouse Y") * pitchSensitivity;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        if (freeLooking)
        {
            freeLookYaw += Input.GetAxis("Mouse X") * freeLookYawSensitivity;
        }
        else if (turning)
        {
            // Right-drag turning rotates the body itself to match the
            // camera, so the camera re-centers behind it.
            freeLookYaw = 0f;
        }
        // Otherwise (free look just released, nothing held): hold the
        // camera where it was left rather than snapping back.

        cameraPivot.localRotation = Quaternion.Euler(pitch, freeLookYaw, 0f);
    }
}
