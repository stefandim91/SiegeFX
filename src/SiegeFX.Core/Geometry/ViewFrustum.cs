using System.Numerics;

namespace SiegeFX.Core.Geometry;

/// <summary>The six clip planes of a view-projection matrix, for skipping
/// geometry the camera can't see. Built for System.Numerics' row-vector
/// convention (<c>clip = v * viewProj</c>) and its 0..1 clip depth
/// (<see cref="Matrix4x4.CreatePerspectiveFieldOfView"/>). Tests are
/// conservative: something reported outside is fully outside; something near
/// a corner may be reported inside.</summary>
public readonly struct ViewFrustum
{
    // Each plane is (normal, d) with inside meaning dot(normal, p) + d >= 0.
    private readonly Vector4 _left, _right, _bottom, _top, _near, _far;

    public ViewFrustum(Matrix4x4 viewProj)
    {
        var c1 = new Vector4(viewProj.M11, viewProj.M21, viewProj.M31, viewProj.M41);
        var c2 = new Vector4(viewProj.M12, viewProj.M22, viewProj.M32, viewProj.M42);
        var c3 = new Vector4(viewProj.M13, viewProj.M23, viewProj.M33, viewProj.M43);
        var c4 = new Vector4(viewProj.M14, viewProj.M24, viewProj.M34, viewProj.M44);
        _left = Normalize(c4 + c1);
        _right = Normalize(c4 - c1);
        _bottom = Normalize(c4 + c2);
        _top = Normalize(c4 - c2);
        _near = Normalize(c3);
        _far = Normalize(c4 - c3);
    }

    /// <summary>False only when the sphere lies entirely outside one plane.</summary>
    public bool IntersectsSphere(Vector3 center, float radius)
        => Side(_left, center) >= -radius && Side(_right, center) >= -radius
        && Side(_bottom, center) >= -radius && Side(_top, center) >= -radius
        && Side(_near, center) >= -radius && Side(_far, center) >= -radius;

    /// <summary>False only when the box lies entirely outside one plane.</summary>
    public bool IntersectsBox(Vector3 min, Vector3 max)
        => BoxInside(_left, min, max) && BoxInside(_right, min, max)
        && BoxInside(_bottom, min, max) && BoxInside(_top, min, max)
        && BoxInside(_near, min, max) && BoxInside(_far, min, max);

    private static float Side(Vector4 plane, Vector3 p)
        => plane.X * p.X + plane.Y * p.Y + plane.Z * p.Z + plane.W;

    // The box corner farthest along the plane normal decides it.
    private static bool BoxInside(Vector4 plane, Vector3 min, Vector3 max)
        => Side(plane, new Vector3(plane.X >= 0 ? max.X : min.X,
                                   plane.Y >= 0 ? max.Y : min.Y,
                                   plane.Z >= 0 ? max.Z : min.Z)) >= 0f;

    private static Vector4 Normalize(Vector4 plane)
    {
        float len = MathF.Sqrt(plane.X * plane.X + plane.Y * plane.Y + plane.Z * plane.Z);
        return len > 1e-12f ? plane / len : plane;
    }
}
