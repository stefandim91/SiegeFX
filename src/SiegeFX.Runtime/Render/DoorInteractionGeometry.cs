using System.Numerics;

namespace SiegeFX.Runtime.Render;

/// <summary>Geometry shared by door hover, click, and mirrored-leaf swing.
/// ASP bounds are in the door's local Z-up frame; the model matrix maps that
/// frame to the rendered world pose.</summary>
internal static class DoorInteractionGeometry
{
    public static bool TryPick(Vector3 nearWorld, Vector3 farWorld, Matrix4x4 model,
        Vector3 min, Vector3 max, out float rayT)
    {
        rayT = 0f;
        if (!Matrix4x4.Invert(model, out var inverse)) return false;
        var origin = Vector3.Transform(nearWorld, inverse);
        var delta = Vector3.Transform(farWorld, inverse) - origin;
        // The leaves are only about 0.1 units thick. A small local tolerance
        // makes their visible edge selectable without reaching across a gap.
        min -= new Vector3(0.06f, 0.10f, 0.06f);
        max += new Vector3(0.06f, 0.10f, 0.06f);
        float enter = 0f, exit = 1f;
        for (int axis = 0; axis < 3; axis++)
        {
            float p = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
            float d = axis == 0 ? delta.X : axis == 1 ? delta.Y : delta.Z;
            float lo = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
            float hi = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;
            if (MathF.Abs(d) < 1e-7f)
            {
                if (p < lo || p > hi) return false;
                continue;
            }
            float a = (lo - p) / d, b = (hi - p) / d;
            if (a > b) (a, b) = (b, a);
            enter = MathF.Max(enter, a);
            exit = MathF.Min(exit, b);
            if (enter > exit) return false;
        }
        rayT = enter;
        return true;
    }

    /// <summary>Choose the quarter-turn that sends the free edge away from
    /// the player. Paired left/right door meshes extend from their hinge in
    /// opposite local X directions, so player side alone cannot choose it.</summary>
    public static float SwingSign(Matrix4x4 closedWorld, Vector3 meshMin,
        Vector3 meshMax, Vector3 playerWorld)
    {
        if (!Matrix4x4.Invert(closedWorld, out var inverse)) return 1f;
        float playerSide = Vector3.Transform(playerWorld, inverse).Y >= 0f ? 1f : -1f;
        float freeEdgeSide = meshMin.X + meshMax.X >= 0f ? 1f : -1f;
        return -playerSide * freeEdgeSide;
    }
}
