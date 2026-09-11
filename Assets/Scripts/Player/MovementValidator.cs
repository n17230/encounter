using UnityEngine;

// Server-side sanity checks on an owner-authoritative transform, the way
// MMOs police client-driven movement: instead of simulating the player,
// the server watches the replicated position and rejects what a legitimate
// client could not have done. Pure C# (time and terrain height passed in)
// so the rules are unit-testable.
//
// Checks are windowed rather than per-tick because NetworkTransform state
// arrives in bursts at the network tick rate, so a single server frame can
// legitimately see several frames' worth of movement land at once.
public class MovementValidator
{
    public enum Verdict { Ok, TooFast, Teleport, BelowGround }

    public float WindowSeconds = 0.5f;
    public float SpeedTolerance = 1.3f;
    public float SlackDistance = 1f;
    public float TeleportFactor = 3f;
    public float BelowGroundMargin = 1f;

    private bool hasWindowStart;
    private Vector3 windowStartPosition;
    private float windowStartTime;
    private float windowStartMaxSpeed;

    public Vector3 LastAcceptedPosition => windowStartPosition;

    public void Reset(Vector3 position, float now, float maxSpeed)
    {
        hasWindowStart = true;
        windowStartPosition = position;
        windowStartTime = now;
        windowStartMaxSpeed = maxSpeed;
    }

    // feetY: the character's lowest point; groundY: terrain height there
    // (float.NaN when there is no terrain to compare against). maxSpeed is
    // the server's current run speed for this character - the max of the
    // window's start and end values is used so a slow that the client hasn't
    // learned about yet doesn't trip the check.
    public Verdict Check(Vector3 position, float feetY, float groundY, float now, float maxSpeed)
    {
        if (!hasWindowStart)
        {
            Reset(position, now, maxSpeed);
            return Verdict.Ok;
        }

        if (!float.IsNaN(groundY) && feetY < groundY - BelowGroundMargin)
        {
            Reset(windowStartPosition, now, maxSpeed);
            return Verdict.BelowGround;
        }

        float elapsed = now - windowStartTime;
        if (elapsed < WindowSeconds) return Verdict.Ok;

        Vector3 delta = position - windowStartPosition;
        delta.y = 0f;
        float allowed = Mathf.Max(maxSpeed, windowStartMaxSpeed) * elapsed * SpeedTolerance + SlackDistance;
        float distance = delta.magnitude;

        if (distance > allowed * TeleportFactor)
        {
            Reset(windowStartPosition, now, maxSpeed);
            return Verdict.Teleport;
        }
        if (distance > allowed)
        {
            Reset(windowStartPosition, now, maxSpeed);
            return Verdict.TooFast;
        }

        Reset(position, now, maxSpeed);
        return Verdict.Ok;
    }
}
