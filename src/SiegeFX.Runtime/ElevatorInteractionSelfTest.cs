using System.Numerics;
using SiegeFX.Core.Assets;
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
        // A multi-bone control's raw vertices are already in bind space.
        // Applying its bone-0 rotation again tips the button toward the floor.
        var rootRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f);
        var articulated = new AspMesh
        {
            BoneNames = new[] { "Bone01", "Bone02" },
            BindPose = new[]
            {
                new AspMesh.Transform(rootRotation, new Vector3(0, -0.18f, 0)),
                new AspMesh.Transform(Quaternion.Identity, Vector3.Zero),
            },
            SkinWeights = new[] { new Vector4(1, 0, 0, 0) },
            SkinBones = new uint[] { 1 },
        };
        ok &= Check("articulated lever bind-space pose unchanged",
            RenderHost.ComputeRootBindPose(articulated) == Matrix4x4.Identity, true);
        // Utraean transport-hub switches can be attached to the car SNO. Their offset
        // must rotate with that node and then travel unchanged to the other
        // stop; composing in the reverse order leaves it beside the shaft.
        var controlLocal = Matrix4x4.CreateTranslation(1f, 1.13457f, 0f);
        var upperCar = Matrix4x4.CreateRotationY(MathF.PI / 2f) *
                       Matrix4x4.CreateTranslation(10f, 0f, 5f);
        var lowerCar = upperCar * Matrix4x4.CreateTranslation(0f, -12f, 0f);
        var upperControl = RenderHost.ComposeAttachedPropWorld(controlLocal, upperCar);
        var lowerControl = RenderHost.ComposeAttachedPropWorld(controlLocal, lowerCar);
        ok &= Check("control offset follows car orientation",
            Vector3.Distance(upperControl.Translation,
                Vector3.Transform(controlLocal.Translation, upperCar)) < 0.001f, true);
        ok &= Check("control follows car between stops",
            Vector3.Distance(lowerControl.Translation - upperControl.Translation,
                new Vector3(0, -12f, 0)) < 0.001f, true);
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
        Console.WriteLine(ok ? "[elevator-selftest] PASS lever geometry, pose and rider suspension" : "[elevator-selftest] FAIL");
        return ok;
    }
}
