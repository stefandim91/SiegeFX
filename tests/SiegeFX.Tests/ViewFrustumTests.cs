using System.Numerics;
using SiegeFX.Core.Geometry;

namespace SiegeFX.Tests;

public class ViewFrustumTests
{
    // Camera at the origin looking down -Z, 60° vertical FOV, 16:9, near 0.5, far 200.
    private static ViewFrustum Frustum() => new(
        Matrix4x4.CreateLookAt(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY)
        * Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3f, 16f / 9f, 0.5f, 200f));

    [Fact]
    public void Things_in_front_are_visible()
    {
        Assert.True(Frustum().IntersectsSphere(new Vector3(0, 0, -20), 1f));
        Assert.True(Frustum().IntersectsBox(new Vector3(-1, -1, -21), new Vector3(1, 1, -19)));
    }

    [Fact]
    public void Things_behind_the_camera_are_culled()
    {
        Assert.False(Frustum().IntersectsSphere(new Vector3(0, 0, 20), 1f));
        Assert.False(Frustum().IntersectsBox(new Vector3(-1, -1, 19), new Vector3(1, 1, 21)));
    }

    [Fact]
    public void Things_far_to_the_side_or_beyond_far_are_culled()
    {
        Assert.False(Frustum().IntersectsSphere(new Vector3(100, 0, -20), 1f));
        Assert.False(Frustum().IntersectsSphere(new Vector3(0, -100, -20), 1f));
        Assert.False(Frustum().IntersectsSphere(new Vector3(0, 0, -300), 1f));
    }

    [Fact]
    public void Something_straddling_an_edge_stays_visible()
    {
        // Centre just outside the left edge at 20 units, radius reaching back in.
        float halfWidth = 20f * MathF.Tan(MathF.PI / 6f) * 16f / 9f;
        Assert.True(Frustum().IntersectsSphere(new Vector3(-halfWidth - 0.5f, 0, -20), 1.5f));
        Assert.True(Frustum().IntersectsBox(new Vector3(-halfWidth - 2f, -1, -21), new Vector3(-halfWidth + 0.5f, 1, -19)));
    }

    [Fact]
    public void A_box_around_the_camera_is_visible()
        => Assert.True(Frustum().IntersectsBox(new Vector3(-5, -5, -5), new Vector3(5, 5, 5)));
}
