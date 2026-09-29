using System.Numerics;

namespace SiegeFX.Runtime.Render;

/// <summary>Pure projection and hit-scoring for an actor's feet-to-head body segment.</summary>
internal static class ScreenActorPick
{
    internal readonly record struct Projection(Vector2 Start, Vector2 End, float Depth);

    /// <summary>
    /// Clips the world-space body segment to the view frustum in homogeneous
    /// clip space before dividing by W. This keeps near-plane and off-screen
    /// endpoints from producing unbounded screen segments and pick radii.
    /// </summary>
    internal static bool TryProjectBody(
        Matrix4x4 viewProjection,
        Vector3 feet,
        Vector3 head,
        Vector2 viewport,
        out Projection projection)
    {
        projection = default;
        if (!(viewport.X > 0f) || !(viewport.Y > 0f)
            || !float.IsFinite(viewport.X) || !float.IsFinite(viewport.Y))
            return false;

        var a = Vector4.Transform(new Vector4(feet, 1f), viewProjection);
        var b = Vector4.Transform(new Vector4(head, 1f), viewProjection);
        if (!IsFinite(a) || !IsFinite(b)) return false;

        float enter = 0f;
        float leave = 1f;
        // System.Numerics' perspective matrix uses Direct3D clip depth:
        // -W <= X,Y <= W and 0 <= Z <= W.
        if (!ClipPlane(a.X + a.W, b.X + b.W, ref enter, ref leave) ||
            !ClipPlane(a.W - a.X, b.W - b.X, ref enter, ref leave) ||
            !ClipPlane(a.Y + a.W, b.Y + b.W, ref enter, ref leave) ||
            !ClipPlane(a.W - a.Y, b.W - b.Y, ref enter, ref leave) ||
            !ClipPlane(a.Z, b.Z, ref enter, ref leave) ||
            !ClipPlane(a.W - a.Z, b.W - b.Z, ref enter, ref leave))
            return false;

        var delta = b - a;
        var clippedA = a + delta * enter;
        var clippedB = a + delta * leave;
        if (clippedA.W <= 1e-5f || clippedB.W <= 1e-5f) return false;

        var ndcA = new Vector2(clippedA.X / clippedA.W, clippedA.Y / clippedA.W);
        var ndcB = new Vector2(clippedB.X / clippedB.W, clippedB.Y / clippedB.W);
        if (!IsFinite(ndcA) || !IsFinite(ndcB)) return false;

        projection = new Projection(
            ToScreen(ndcA, viewport),
            ToScreen(ndcB, viewport),
            (clippedA.W + clippedB.W) * 0.5f);
        return float.IsFinite(projection.Depth);
    }

    internal static bool TryScore(
        Matrix4x4 viewProjection,
        Vector3 feet,
        Vector3 head,
        Vector2 viewport,
        Vector2 cursor,
        float minimumRadius,
        out float score)
    {
        score = float.MaxValue;
        if (!IsFinite(cursor) || !float.IsFinite(minimumRadius) || minimumRadius < 0f ||
            !TryProjectBody(viewProjection, feet, head, viewport, out var body))
            return false;

        var segment = body.End - body.Start;
        float lengthSquared = segment.LengthSquared();
        float t = lengthSquared < 1e-4f
            ? 0f
            : Math.Clamp(Vector2.Dot(cursor - body.Start, segment) / lengthSquared, 0f, 1f);
        float distance = Vector2.Distance(cursor, body.Start + segment * t);
        float radius = MathF.Max(minimumRadius, segment.Length() * 0.30f);
        if (distance > radius) return false;

        score = distance + body.Depth * 0.05f;
        return float.IsFinite(score);
    }

    private static bool ClipPlane(float atStart, float atEnd, ref float enter, ref float leave)
    {
        const float Epsilon = 1e-6f;
        bool startInside = atStart >= -Epsilon;
        bool endInside = atEnd >= -Epsilon;
        if (startInside && endInside) return true;
        if (!startInside && !endInside) return false;

        float denominator = atStart - atEnd;
        if (MathF.Abs(denominator) <= Epsilon) return false;
        float crossing = Math.Clamp(atStart / denominator, 0f, 1f);
        if (!startInside) enter = MathF.Max(enter, crossing);
        else leave = MathF.Min(leave, crossing);
        return enter <= leave + Epsilon;
    }

    private static Vector2 ToScreen(Vector2 ndc, Vector2 viewport) => new(
        (ndc.X * 0.5f + 0.5f) * viewport.X,
        (1f - (ndc.Y * 0.5f + 0.5f)) * viewport.Y);

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool IsFinite(Vector4 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) && float.IsFinite(value.W);
}
