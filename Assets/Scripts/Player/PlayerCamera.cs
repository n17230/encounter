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

    // Up/Down arrow zoom - fixed keys (not rebindable), same pattern as
    // Tab/backtick/F1-F5. Distance is how far back along the camera's own
    // local Z the camera sits from the pivot; held-key, not a per-press
    // step, so it feels like a continuous zoom rather than notches.
    // Saved to the profile via CameraZoomScale (min/max live there, same
    // pattern as UIScale/LookSensitivityScale). Speed isn't saved - it's
    // a feel setting, not a preference value. Not specified by the user
    // beyond "up/down to zoom in/out" - speed is a placeholder guess,
    // flagged in review_with_fable.md.
    [SerializeField] private float zoomSpeed = 8f;

    private float pitch;
    private float freeLookYaw;
    private float zoomDistance;
    private Vector3 baseCameraLocalOffset;

    private CharacterStats stats;

    // Cached scene fog state, restored exactly once a vision-reducing
    // effect (e.g. Enshroud, Concussive Shot) ends - RenderSettings.fog is
    // per-client/local, not networked, so toggling it here only ever
    // changes what THIS player sees on their own screen.
    private bool visionEffectActive;
    private bool cachedFogEnabled;
    private FogMode cachedFogMode;
    private float cachedFogStart;
    private float cachedFogEnd;
    private Color cachedFogColor;

    public Camera Camera => playerCamera;

    private void Awake()
    {
        stats = GetComponent<CharacterStats>();
        baseCameraLocalOffset = playerCamera.transform.localPosition;

        float defaultDistance = -baseCameraLocalOffset.z;
        zoomDistance = CameraZoomScale.Value > 0f ? CameraZoomScale.Value : defaultDistance;
        zoomDistance = Mathf.Clamp(zoomDistance, CameraZoomScale.Min, CameraZoomScale.Max);
        playerCamera.transform.localPosition = new Vector3(baseCameraLocalOffset.x, baseCameraLocalOffset.y, -zoomDistance);
    }

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

        UpdateVisionEffect();

        bool menuOpen = MainMenu.IsOpen;
        bool turning = !menuOpen && Input.GetMouseButton(1);
        // Left-click alone: camera-only free look. Rotates the camera
        // pivot, not the player's own transform (only right-click drives
        // that, via PlayerMovement), so looking around never changes which
        // way the player is actually facing/moving.
        bool freeLooking = !menuOpen && Input.GetMouseButton(0) && !turning;

        if (turning || freeLooking)
        {
            pitch -= Input.GetAxis("Mouse Y") * pitchSensitivity * LookSensitivityScale.Value;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        if (freeLooking)
        {
            freeLookYaw += Input.GetAxis("Mouse X") * freeLookYawSensitivity * LookSensitivityScale.Value;
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

        if (!menuOpen) UpdateZoom();
    }

    // ZoomIn/ZoomOut (Options page, default Up/Down arrow), held: moves
    // the camera closer/farther along its own local Z (the offset baked
    // into the prefab), clamped between CameraZoomScale.Min/Max. Written
    // back into the profile's in-memory copy on every change; an existing
    // Save() trigger (menu close, entering the testing area) is what
    // actually persists it to disk, same as UiScale/LookSensitivity.
    private void UpdateZoom()
    {
        if (MovementInput.IsHeld(MovementAction.ZoomIn)) zoomDistance -= zoomSpeed * Time.deltaTime;
        else if (MovementInput.IsHeld(MovementAction.ZoomOut)) zoomDistance += zoomSpeed * Time.deltaTime;
        else return;

        zoomDistance = Mathf.Clamp(zoomDistance, CameraZoomScale.Min, CameraZoomScale.Max);
        playerCamera.transform.localPosition = new Vector3(baseCameraLocalOffset.x, baseCameraLocalOffset.y, -zoomDistance);
        CameraZoomScale.Value = zoomDistance;
    }

    // The smallest VisionRange among this player's own currently active
    // effects (there's normally at most one, but smallest/most-restrictive
    // wins if they ever overlap) - the environment and everything else
    // fades to black past that distance, own screen only.
    private void UpdateVisionEffect()
    {
        float visionRange = 0f;
        foreach (ActiveEffectNet entry in stats.ActiveEffects)
        {
            StatusEffectData effect = GameDatabase.GetEffect(entry.EffectId.ToString());
            if (effect == null || effect.VisionRange <= 0f) continue;
            if (visionRange <= 0f || effect.VisionRange < visionRange) visionRange = effect.VisionRange;
        }

        if (visionRange > 0f)
        {
            if (!visionEffectActive)
            {
                visionEffectActive = true;
                cachedFogEnabled = RenderSettings.fog;
                cachedFogMode = RenderSettings.fogMode;
                cachedFogStart = RenderSettings.fogStartDistance;
                cachedFogEnd = RenderSettings.fogEndDistance;
                cachedFogColor = RenderSettings.fogColor;
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Color.black;
            RenderSettings.fogStartDistance = visionRange * 0.6f;
            RenderSettings.fogEndDistance = visionRange;
        }
        else if (visionEffectActive)
        {
            visionEffectActive = false;
            RenderSettings.fog = cachedFogEnabled;
            RenderSettings.fogMode = cachedFogMode;
            RenderSettings.fogStartDistance = cachedFogStart;
            RenderSettings.fogEndDistance = cachedFogEnd;
            RenderSettings.fogColor = cachedFogColor;
        }
    }
}
