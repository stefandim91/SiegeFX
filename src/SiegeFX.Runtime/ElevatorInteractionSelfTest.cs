using System.Numerics;
using SiegeFX.Runtime.Render;

namespace SiegeFX.Runtime;

/// <summary>Headless regression for lever selection against the clicked floor.</summary>
internal static class ElevatorInteractionSelfTest
{
    public static bool Run()
    {
        var origin = new Vector3(0, 5, 0);
        var ray = Vector3.UnitZ;
        bool Check(string name, bool actual, bool expected)
        {
            if (actual == expected) return true;
            Console.Error.WriteLine($"[elevator-selftest] FAIL {name}: expected {expected}, got {actual}");
            return false;
        }

        bool ok = true;
        ok &= Check("lever before floor", RenderHost.LeverRayHitBeforeFloor(
            origin, ray, 10f, new Vector3(0.2f, 5, 6), out _), true);
        ok &= Check("lever behind clicked floor", RenderHost.LeverRayHitBeforeFloor(
            origin, ray, 5f, new Vector3(0.2f, 5, 6), out _), false);
        var slant = Vector3.Normalize(new Vector3(0, -1, 1));
        ok &= Check("oblique hit on floor lever", RenderHost.LeverRayHitBeforeFloor(
            origin, slant, MathF.Sqrt(50), new Vector3(0, 0, 5.5f), out _), true);
        ok &= Check("nearby floor click misses lever", RenderHost.LeverRayHitBeforeFloor(
            origin, ray, 5f, new Vector3(1.2f, 5, 4), out _), false);
        ok &= Check("lever outside range", RenderHost.LeverRayHitBeforeFloor(
            origin, ray, 130f, new Vector3(0, 5, 121), out _), false);
        // Suspension must reject a new order before Replan touches the stale
        // car mesh. This path deliberately needs no mesh instance.
        var rideStart = new Vector3(4, 10, 4);
        var rider = new SiegeFX.Core.Nav.NavFollower(null!, rideStart, 4f);
        rider.SetMovementSuspended(true);
        var carried = rideStart + new Vector3(0, -2, 0);
        rider.Teleport(carried);
        rider.SetTarget(new Vector3(4, -20, 4));
        ok &= Check("ride ignores move order", rider.Position == carried && rider.ReachedGoal, true);
        rider.SetMovementSuspended(false);
        ok &= Check("movement resumes at arrival", rider.MovementSuspended, false);
        Console.WriteLine(ok ? "[elevator-selftest] PASS lever geometry and rider suspension" : "[elevator-selftest] FAIL");
        return ok;
    }
}
