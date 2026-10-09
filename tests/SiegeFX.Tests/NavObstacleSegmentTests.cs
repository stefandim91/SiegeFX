using System.Numerics;
using SiegeFX.Core.Nav;

namespace SiegeFX.Tests;

public class NavObstacleSegmentTests
{
    // A 6×3 corridor with a closed door leaf across it at x = 3.
    private static NavMesh Corridor() => NavTestMeshes.FromCells(NavTestMeshes.Rect(0, 0, 5, 2));

    private static readonly Vector3 LeafA = new(3f, 0f, 0f);
    private static readonly Vector3 LeafB = new(3f, 0f, 3f);

    private static int Tri(NavMesh mesh, float x, float z)
    {
        Assert.True(mesh.TryFindTriangle(new Vector3(x, 0f, z), out var t));
        return t;
    }

    [Fact]
    public void Leaf_seals_the_doorway()
    {
        var mesh = Corridor();
        Assert.True(mesh.MarkObstacleSegment(LeafA, LeafB, 0.15f, 0f, 2.5f, "door") > 0);
        var path = new List<int>();
        Assert.False(NavPathfinder.TryFindPath(mesh, Tri(mesh, 0.5f, 1.5f), Tri(mesh, 5.5f, 1.5f), path));
    }

    [Fact]
    public void Floor_in_front_of_the_leaf_stays_walkable()
    {
        var mesh = Corridor();
        mesh.MarkObstacleSegment(LeafA, LeafB, 0.15f, 0f, 2.5f, "door");
        // One cell back from the leaf on either side: the step / porch case.
        Assert.False(mesh.IsBlocked(Tri(mesh, 1.5f, 1.5f)));
        Assert.False(mesh.IsBlocked(Tri(mesh, 4.5f, 1.5f)));
        var path = new List<int>();
        Assert.True(NavPathfinder.TryFindPath(mesh, Tri(mesh, 0.5f, 0.5f), Tri(mesh, 1.5f, 2.5f), path));
    }

    [Fact]
    public void Only_triangles_the_leaf_crosses_or_touches_are_blocked()
    {
        var mesh = Corridor();
        mesh.MarkObstacleSegment(LeafA, LeafB, 0.15f, 0f, 2.5f, "door");
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            var c = mesh.Centroids[t];
            bool touchesLeaf = c.X > 2f && c.X < 4f;
            Assert.Equal(touchesLeaf, mesh.IsBlocked(t));
        }
    }

    [Fact]
    public void Leaf_on_another_floor_blocks_nothing()
    {
        var mesh = Corridor();
        // A closed door 10 units overhead must not seal the floor below it.
        Assert.Equal(0, mesh.MarkObstacleSegment(LeafA + new Vector3(0, 10, 0),
            LeafB + new Vector3(0, 10, 0), 0.15f, 10f, 12.5f, "door"));
    }
}
