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

    // Up/Down arrow zoom by default (MovementAction.ZoomIn/ZoomOut,
    // rebindable on the Options page). Distance is how far back along the camera's own
    // local Z the camera sits from the pivot; held-key, not a per-press
    // step, so it feels like a continuous zoom rather than notches.
    // Saved to the profile via CameraZoomScale (min/max live there, same
    // pattern as UIScale/LookSensitivityScale). Speed isn't saved - it's
    // a feel setting, not a preference value. Not specified by the user
    // beyond "up/down to zoom in/out" - speed is a placeholder guess,
    // flagged in review_with_fable.md.
    [SerializeField] private float zoomSpeed = 8f;

    // Extra gap kept between the camera and whatever it collided with, on
    // top of the near-plane-sized sphere the collision cast already
    // sweeps (see ApplyCollisionAwarePosition). Not specified by the user -
    // a small reasonable default, flagged for later tuning like zoomSpeed
    // above.
    [SerializeField] private float cameraCollisionPadding = 0.05f;

    // Unordered and truncated by Physics.*NonAlloc, and creatures in the
    // line consume slots before being skipped - sized generously so the
    // nearest wall can't be dropped in dense ruins/foliage.
    private static readonly RaycastHit[] cameraCollisionHits = new RaycastHit[32];

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
            // Right-drag turning rotates the body to match wherever the
            // camera was already looking (any free-look yaw built up via
            // left-drag), rather than snapping the camera back to match
            // the body's current facing - so starting to turn-and-run
            // continues in the direction you were already viewing. Only
            // fires once per turn-session: freeLookYaw is already 0 on
            // every subsequent frame while turning (freeLooking can't be
            // true at the same time, so nothing re-accumulates it).
            if (freeLookYaw != 0f)
            {
                transform.Rotate(Vector3.up, freeLookYaw);
                freeLookYaw = 0f;
            }
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
    // ApplyCollisionAwarePosition runs every call (not just while a zoom
    // key is held) so the camera also pulls in from plain movement/pitch
    // changes pushing it into the ground or a wall, not just zooming.
    private void UpdateZoom()
    {
        if (MovementInput.IsHeld(MovementAction.ZoomIn)) zoomDistance -= zoomSpeed * Time.deltaTime;
        else if (MovementInput.IsHeld(MovementAction.ZoomOut)) zoomDistance += zoomSpeed * Time.deltaTime;
        else
        {
            ApplyCollisionAwarePosition();
            return;
        }

        zoomDistance = Mathf.Clamp(zoomDistance, CameraZoomScale.Min, CameraZoomScale.Max);
        CameraZoomScale.Value = zoomDistance;
        ApplyCollisionAwarePosition();
    }

    // Pulls the camera in along the pivot-to-camera line when something
    // solid (terrain, a wall, a prop) is in the way, so it can never clip
    // through the environment - same "real geometry only" rule
    // CombatPhysics.HasLineOfSight uses: Physics.DefaultRaycastLayers +
    // QueryTriggerInteraction.Ignore (ground patches, pickups, etc. aren't
    // walls) + skipping anything under a Targetable (a mob/player standing
    // between the pivot and the camera shouldn't yank the camera in).
    // zoomDistance itself (the player's chosen zoom level) is never
    // mutated here - only this frame's actual camera placement is, so
    // backing away from the obstruction smoothly restores full zoom.
    // A SphereCast the size of the camera's near-plane corner, not a ray:
    // a thin ray stopped a fixed padding short of an oblique wall still
    // leaves the near plane's side corners inside it. And when shortened,
    // the camera is placed ON the swept line (origin + direction * d),
    // not re-composed from local (x, y, -d) - the prefab's +Y camera
    // offset makes those two different points once d < zoomDistance, and
    // the latter sits in space the cast never checked (e.g. inside a
    // lintel the line passed under).
    private void ApplyCollisionAwarePosition()
    {
        Vector3 origin = cameraPivot.position;
        Vector3 desiredLocal = new Vector3(baseCameraLocalOffset.x, baseCameraLocalOffset.y, -zoomDistance);
        Vector3 desiredWorld = cameraPivot.TransformPoint(desiredLocal);
        Vector3 delta = desiredWorld - origin;
        float desiredDistance = delta.magnitude;

        if (desiredDistance <= 0.001f)
        {
            playerCamera.transform.localPosition = desiredLocal;
            return;
        }

        Vector3 direction = delta / desiredDistance;
        float radius = NearPlaneCornerRadius();
        int count = Physics.SphereCastNonAlloc(origin, radius, direction, cameraCollisionHits, desiredDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float closest = desiredDistance;
        for (int i = 0; i < count; i++)
        {
            if (cameraCollisionHits[i].collider.GetComponentInParent<Targetable>() != null) continue;
            if (cameraCollisionHits[i].distance < closest) closest = cameraCollisionHits[i].distance;
        }

        float distance = closest < desiredDistance ? Mathf.Max(0f, closest - cameraCollisionPadding) : desiredDistance;
        playerCamera.transform.position = origin + direction * distance;
    }

    // Distance from the camera's position to a corner of its near clip
    // plane - the smallest sphere that fully contains the near plane, so a
    // cast of that radius stopping clear of a surface guarantees no corner
    // of the near plane is inside it.
    private float NearPlaneCornerRadius()
    {
        float halfHeight = playerCamera.nearClipPlane * Mathf.Tan(playerCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfWidth = halfHeight * playerCamera.aspect;
        return Mathf.Sqrt(halfHeight * halfHeight + halfWidth * halfWidth + playerCamera.nearClipPlane * playerCamera.nearClipPlane);
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
        else
        {
            RestoreFog();
        }
    }

    private void RestoreFog()
    {
        if (!visionEffectActive) return;
        visionEffectActive = false;
        RenderSettings.fog = cachedFogEnabled;
        RenderSettings.fogMode = cachedFogMode;
        RenderSettings.fogStartDistance = cachedFogStart;
        RenderSettings.fogEndDistance = cachedFogEnd;
        RenderSettings.fogColor = cachedFogColor;
    }

    // RenderSettings outlives this player object - leaving (disconnect,
    // returning to the menu) while still darkened would otherwise strand
    // the scene in black fog, since nothing is left to notice the effect
    // ending.
    public override void OnNetworkDespawn()
    {
        if (IsOwner) RestoreFog();
    }
}
