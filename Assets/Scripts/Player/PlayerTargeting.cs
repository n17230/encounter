using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerCamera))]
public class PlayerTargeting : NetworkBehaviour
{
    [SerializeField] private float maxTargetDistance = 100f;
    [SerializeField] private float tabTargetRange = 70f;

    // A right-click that doesn't move the mouse is a click; anything more
    // is the turn-drag that also uses the right button.
    [SerializeField] private float clickMaxPixels = 5f;
    [SerializeField] private float clickMaxSeconds = 0.3f;

    // Rebindable via MovementAction.PartyTarget1-5 (Options page), default
    // F1-F5. Index 0 = PartyTarget1's row, 1 = PartyTarget2's, etc. - see
    // PartyFrames.GetDisplayOrder for why that's not simply "party member N".
    private static readonly MovementAction[] PartyTargetActions =
    {
        MovementAction.PartyTarget1, MovementAction.PartyTarget2, MovementAction.PartyTarget3,
        MovementAction.PartyTarget4, MovementAction.PartyTarget5,
    };

    public Targetable CurrentTarget { get; private set; }

    // Raised when the player right-clicks a target: "attack this".
    public event Action<Targetable> AttackRequested;

    private PlayerCamera playerCameraComponent;
    private PlayerAbilities abilities;
    private Targetable self;
    private Vector3 rightDownPosition;
    private float rightDownTime;

    private void Awake()
    {
        playerCameraComponent = GetComponent<PlayerCamera>();
        abilities = GetComponent<PlayerAbilities>();
        self = GetComponent<Targetable>();
    }

    public void ClearTarget()
    {
        CurrentTarget = null;
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (MainMenu.IsOpen) return;
        // While aiming a ground-targeted ability (see PlayerAbilities), the
        // same clicks confirm/relate to placement, not targeting.
        if (abilities != null && abilities.IsAimingGroundTarget) return;

        if (MovementInput.WasPressed(MovementAction.CycleTarget))
        {
            CycleTarget();
        }

        if (MovementInput.WasPressed(MovementAction.SelfTarget))
        {
            CurrentTarget = self;
        }

        for (int i = 0; i < PartyTargetActions.Length; i++)
        {
            if (!MovementInput.WasPressed(PartyTargetActions[i])) continue;
            TargetPartySlot(i);
            break;
        }

        if (Input.GetMouseButtonDown(1))
        {
            rightDownPosition = Input.mousePosition;
            rightDownTime = Time.time;
        }
        else if (Input.GetMouseButtonUp(1) && IsClick(rightDownPosition, rightDownTime))
        {
            Targetable clicked = RaycastTarget();
            if (clicked != null && clicked != self)
            {
                CurrentTarget = clicked;
                AttackRequested?.Invoke(clicked);
            }
        }
    }

    private bool IsClick(Vector3 downPosition, float downTime)
    {
        return (Input.mousePosition - downPosition).magnitude <= clickMaxPixels
            && Time.time - downTime <= clickMaxSeconds;
    }

    private Targetable RaycastTarget()
    {
        Camera cam = playerCameraComponent.Camera;
        if (cam == null) return null;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        return Physics.Raycast(ray, out RaycastHit hit, maxTargetDistance)
            ? hit.collider.GetComponentInParent<Targetable>()
            : null;
    }

    // rowIndex 0 = PartyTarget1's row, 1 = PartyTarget2's, etc. - the exact
    // same viewer-relative ordering PartyFrames draws, so a key always
    // targets whoever is actually sitting in that row on screen. Does
    // nothing if nobody occupies that row.
    private void TargetPartySlot(int rowIndex)
    {
        Targetable[] all = FindObjectsByType<Targetable>(FindObjectsSortMode.None);
        IReadOnlyList<PartyFrames.Slot> party = PartyFrames.GetDisplayOrder(all, OwnerClientId);
        if (rowIndex < party.Count) CurrentTarget = party[rowIndex].Member;
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
