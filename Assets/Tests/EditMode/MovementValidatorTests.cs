using NUnit.Framework;
using UnityEngine;

public class MovementValidatorTests
{
    private const float Speed = 6f;

    private static MovementValidator NewValidator()
    {
        MovementValidator v = new MovementValidator { WindowSeconds = 0.5f, SpeedTolerance = 1.3f, SlackDistance = 1f, TeleportFactor = 3f };
        v.Reset(Vector3.zero, 0f, Speed);
        return v;
    }

    private static MovementValidator.Verdict Move(MovementValidator v, float x, float t, float speed = Speed, float feetY = 0f, float groundY = float.NaN)
    {
        return v.Check(new Vector3(x, feetY + 1f, 0f), feetY, groundY, t, speed);
    }

    [Test]
    public void RunningAtFullSpeedIsAccepted()
    {
        MovementValidator v = NewValidator();
        Assert.AreEqual(MovementValidator.Verdict.Ok, Move(v, Speed * 0.5f, 0.5f));
        Assert.AreEqual(new Vector3(3f, 1f, 0f), v.LastAcceptedPosition);
    }

    [Test]
    public void ChecksWaitForTheWindowToElapse()
    {
        MovementValidator v = NewValidator();
        // Far too fast, but the window hasn't closed yet.
        Assert.AreEqual(MovementValidator.Verdict.Ok, Move(v, 50f, 0.1f));
        Assert.AreEqual(Vector3.zero, v.LastAcceptedPosition);
    }

    [Test]
    public void ModeratelyTooFastIsRejectedAndSnapsBack()
    {
        MovementValidator v = NewValidator();
        float allowed = Speed * 0.5f * 1.3f + 1f; // 4.9
        Assert.AreEqual(MovementValidator.Verdict.TooFast, Move(v, allowed * 2f, 0.5f));
        Assert.AreEqual(Vector3.zero, v.LastAcceptedPosition);
    }

    [Test]
    public void HugeJumpIsATeleport()
    {
        MovementValidator v = NewValidator();
        Assert.AreEqual(MovementValidator.Verdict.Teleport, Move(v, 100f, 0.5f));
    }

    [Test]
    public void VerticalMovementDoesNotCountAsSpeed()
    {
        MovementValidator v = NewValidator();
        Assert.AreEqual(MovementValidator.Verdict.Ok, Move(v, 0f, 0.5f, feetY: -40f)); // falling
    }

    [Test]
    public void RecentlyReducedSpeedStillAllowsTheOldSpeed()
    {
        MovementValidator v = NewValidator();
        // Window started at speed 6; a slow to 2 applied since must not
        // flag a client that (legitimately) hasn't learned of it yet.
        Assert.AreEqual(MovementValidator.Verdict.Ok, Move(v, 3f, 0.5f, speed: 2f));
        // Next window starts at 2, so full speed is now rejected.
        Assert.AreEqual(MovementValidator.Verdict.TooFast, Move(v, 3f + 6f, 1.0f, speed: 2f));
    }

    [Test]
    public void BelowTerrainIsRejectedImmediately()
    {
        MovementValidator v = NewValidator();
        Assert.AreEqual(MovementValidator.Verdict.BelowGround, Move(v, 0f, 0.1f, feetY: -3f, groundY: 0f));
        Assert.AreEqual(MovementValidator.Verdict.Ok, Move(v, 0f, 0.2f, feetY: -0.5f, groundY: 0f));
    }

    [Test]
    public void NoTerrainSkipsTheGroundCheck()
    {
        MovementValidator v = NewValidator();
        Assert.AreEqual(MovementValidator.Verdict.Ok, Move(v, 0f, 0.1f, feetY: -100f, groundY: float.NaN));
    }
}
