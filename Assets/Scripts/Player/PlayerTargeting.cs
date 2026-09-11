using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerCamera))]
public class PlayerTargeting : NetworkBehaviour
{
    [SerializeField] private float maxTargetDistance = 100f;
    [SerializeField] private float tabTargetRange = 70f;

    public Targetable CurrentTarget { get; private set; }

    private PlayerCamera playerCameraComponent;
    private Targetable self;

    private void Awake()
    {
        playerCameraComponent = GetComponent<PlayerCamera>();
        self = GetComponent<Targetable>();
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (MainMenu.IsOpen) return;

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            CycleTarget();
        }

        if (!Input.GetMouseButtonDown(0)) return;
        if (Input.GetMouseButton(1)) return;

        Camera cam = playerCameraComponent.Camera;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        CurrentTarget = Physics.Raycast(ray, out RaycastHit hit, maxTargetDistance)
            ? hit.collider.GetComponentInParent<Targetable>()
            : null;
    }

    // Alternates through every valid Targetable within range (players and
    // mobs alike, excluding self and anything dead), nearest-first, wrapping
    // back to the start once the end of the list is reached.
    private void CycleTarget()
    {
        Targetable[] all = FindObjectsByType<Targetable>(FindObjectsSortMode.None);
        List<Targetable> candidates = new List<Targetable>();

        foreach (Targetable candidate in all)
        {
            if (candidate == null || candidate == self) continue;
            if (candidate.Stats != null && candidate.Stats.CurrentHealth.Value <= 0f) continue;
            if (Vector3.Distance(transform.position, candidate.transform.position) > tabTargetRange) continue;
            candidates.Add(candidate);
        }

        if (candidates.Count == 0)
        {
            CurrentTarget = null;
            return;
        }

        candidates.Sort((a, b) =>
            Vector3.Distance(transform.position, a.transform.position)
                .CompareTo(Vector3.Distance(transform.position, b.transform.position)));

        int currentIndex = CurrentTarget != null ? candidates.IndexOf(CurrentTarget) : -1;
        CurrentTarget = candidates[(currentIndex + 1) % candidates.Count];
    }
}
