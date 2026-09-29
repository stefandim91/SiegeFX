using System.Numerics;
using SiegeFX.Runtime.Render;

namespace SiegeFX.Runtime;

internal static class ScreenActorPickSelfTest
{
    public static bool Run()
    {
        try
        {
            var viewport = new Vector2(1920f, 1080f);

            // Regression from the Elddim church trace. At an almost-overhead
            // angle, both endpoints are far beyond one side of the frustum,
            // but the old post-divide segment and its 30% radius cover the
            // centre of the screen because the head is close to the eye plane.
            var camera = new Camera
            {
                Position = new Vector3(10.2f, 7.8f, 28.6f),
                Yaw = MathF.Atan2(39.4f, 39.8f),
                Pitch = MathF.Asin(-0.999f)
            };
            var vp = camera.GetViewProjection(viewport.X / viewport.Y);
            var loggedFeet = new Vector3(50f, 5.6f, 68f);
            var loggedHead = loggedFeet + new Vector3(0f, 1.8f, 0f);
            var centre = viewport * 0.5f;
            Require(LegacyWouldHit(vp, loggedFeet, loggedHead, viewport, centre, 26f),
                "regression setup no longer reproduces the old inflated-radius hit");
            Require(!ScreenActorPick.TryScore(vp, loggedFeet, loggedHead, viewport, centre, 26f, out _),
                "wholly off-frustum logged actor remained pickable");

            // A distant actor that is genuinely visible remains selectable.
            camera = new Camera { Position = new Vector3(0f, 2f, 10f) };
            vp = camera.GetViewProjection(viewport.X / viewport.Y);
            var distantFeet = new Vector3(0f, 0f, -100f);
            Require(ScreenActorPick.TryProjectBody(vp, distantFeet,
                distantFeet + new Vector3(0f, 1.8f, 0f), viewport, out var distant),
                "visible distant actor was rejected");
            Require(ScreenActorPick.TryScore(vp, distantFeet,
                distantFeet + new Vector3(0f, 1.8f, 0f), viewport,
                (distant.Start + distant.End) * 0.5f, 26f, out _),
                "visible distant actor could not be picked");

            // Feet behind the near plane and head in front: clipping retains
            // the visible portion rather than rejecting the whole body.
            camera = new Camera
            {
                Position = Vector3.Zero,
                Pitch = MathF.PI / 4f
            };
            vp = camera.GetViewProjection(viewport.X / viewport.Y);
            var crossingFeet = new Vector3(0f, 0f, -0.071f);
            var crossingHead = crossingFeet + new Vector3(0f, 1.8f, 0f);
            Require(ScreenActorPick.TryProjectBody(vp, crossingFeet, crossingHead, viewport, out var crossing),
                "near-plane-crossing body was rejected");
            Require(ScreenActorPick.TryScore(vp, crossingFeet, crossingHead, viewport,
                (crossing.Start + crossing.End) * 0.5f, 26f, out _),
                "visible part of near-plane-crossing body was not pickable");

            // A body crossing a viewport edge is clipped to that edge while
            // its visible portion remains pickable.
            camera = new Camera
            {
                Position = Vector3.Zero,
                Pitch = -MathF.PI / 4f
            };
            vp = camera.GetViewProjection(viewport.X / viewport.Y);
            var edgeFeet = new Vector3(1.5f, -2f, -3f);
            var edgeHead = edgeFeet + new Vector3(0f, 1.8f, 0f);
            Require(ScreenActorPick.TryProjectBody(vp, edgeFeet, edgeHead, viewport, out var edge),
                "partially visible body was rejected");
            Require(OnViewportEdge(edge.Start, viewport) || OnViewportEdge(edge.End, viewport),
                "partially visible body was not clipped to a viewport edge");
            Require(ScreenActorPick.TryScore(vp, edgeFeet, edgeHead, viewport,
                (edge.Start + edge.End) * 0.5f, 26f, out _),
                "visible part of edge-crossing body was not pickable");

            // Ordinary nearby selection remains unchanged.
            camera = new Camera { Position = new Vector3(0f, 2f, 8f) };
            vp = camera.GetViewProjection(viewport.X / viewport.Y);
            var nearbyFeet = Vector3.Zero;
            Require(ScreenActorPick.TryProjectBody(vp, nearbyFeet,
                nearbyFeet + new Vector3(0f, 1.8f, 0f), viewport, out var nearby),
                "nearby actor was rejected");
            Require(ScreenActorPick.TryScore(vp, nearbyFeet,
                nearbyFeet + new Vector3(0f, 1.8f, 0f), viewport,
                (nearby.Start + nearby.End) * 0.5f, 26f, out _),
                "nearby actor could not be picked");

            // A floor point inside the former three-unit talk radius must
            // not acquire the NPC's cursor when it is away from their body.
            camera = new Camera { Position = new Vector3(0f, 4f, 10f), Pitch = -0.25f };
            vp = camera.GetViewProjection(viewport.X / viewport.Y);
            var floor = new Vector3(2f, 0f, 0f);
            var clip = Vector4.Transform(new Vector4(floor, 1f), vp);
            var floorCursor = new Vector2((clip.X / clip.W + 1f) * 0.5f * viewport.X,
                (1f - clip.Y / clip.W) * 0.5f * viewport.Y);
            Require(Vector3.Distance(floor, Vector3.Zero) < 3f,
                "talk regression must be inside the old ground radius");
            Require(!ScreenActorPick.TryScore(vp, Vector3.Zero, new Vector3(0f, 1.8f, 0f),
                viewport, floorCursor, 26f, out _), "floor beside NPC acquired its body hit");
            Console.WriteLine("[selftest-screen-actor-pick] PASS");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[selftest-screen-actor-pick] FAIL: " + ex.Message);
            return false;
        }
    }

    private static bool LegacyWouldHit(
        Matrix4x4 viewProjection,
        Vector3 feet,
        Vector3 head,
        Vector2 viewport,
        Vector2 cursor,
        float minimumRadius)
    {
        bool Project(Vector3 world, out Vector2 screen)
        {
            var clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
            screen = default;
            if (clip.W <= 1e-4f) return false;
            screen = new Vector2(
                (clip.X / clip.W * 0.5f + 0.5f) * viewport.X,
                (1f - (clip.Y / clip.W * 0.5f + 0.5f)) * viewport.Y);
            return true;
        }

        if (!Project(feet, out var start) || !Project(head, out var end)) return false;
        var segment = end - start;
        float lengthSquared = segment.LengthSquared();
        float t = lengthSquared < 1e-4f
            ? 0f
            : Math.Clamp(Vector2.Dot(cursor - start, segment) / lengthSquared, 0f, 1f);
        float distance = Vector2.Distance(cursor, start + segment * t);
        float radius = MathF.Max(minimumRadius, segment.Length() * 0.30f);
        return distance <= radius;
    }

    private static bool OnViewportEdge(Vector2 point, Vector2 viewport) =>
        MathF.Abs(point.X) < 0.05f || MathF.Abs(point.X - viewport.X) < 0.05f ||
        MathF.Abs(point.Y) < 0.05f || MathF.Abs(point.Y - viewport.Y) < 0.05f;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
