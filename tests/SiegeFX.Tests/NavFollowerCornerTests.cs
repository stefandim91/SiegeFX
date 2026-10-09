using System.Numerics;
using SiegeFX.Core.Nav;

namespace SiegeFX.Tests;

public class NavFollowerCornerTests
{
    // Shaped like Elddim's upper green and the slope below it: an upper strip
    // (z 0..1) and a lower strip (z -3..-1), both running east from x = 0, with
    // a gap between them (x > 0, z -1..0). They join only through a west
    // column (x -1..0). Walking from the east end of the upper strip to the
    // east end of the lower one goes west, turns at the corners (0, 0) and
    // (0, -1), and comes back east. Turning early at (0, 0) heads straight
    // into the gap, and the leg between the corners runs exactly along the
    // column's edge.
    private static NavMesh Ledge() => NavTestMeshes.FromCells(
        NavTestMeshes.Rect(0, 0, 5, 0)
            .Concat(NavTestMeshes.Rect(-1, -3, -1, 0))
            .Concat(NavTestMeshes.Rect(0, -3, 5, -2)));

    private static readonly Vector3 Start = new(5.43f, 0f, 0.37f);
    private static readonly Vector3 Goal = new(5.5f, 0f, -2.5f);

    [Fact]
    public void Walker_goes_round_the_ledge_without_stalling()
    {
        var mesh = Ledge();
        var f = new NavFollower(mesh, Start, 4.5f);
        f.SetTarget(Goal);
        Assert.False(f.PathBlocked);

        // About 12.5 units at 4.5 u/s is ~56 ticks of 50 ms; a stall at the
        // corner costs stuck recovery well beyond this budget.
        int ticks = 0;
        while (!f.ReachedGoal && ticks < 70)
        {
            f.Tick(0.05f);
            ticks++;
            Assert.True(mesh.TryFindTriangle(f.Position, out _),
                $"walker left the floor at ({f.Position.X:F2}, {f.Position.Z:F2})");
        }
        Assert.True(f.ReachedGoal,
            $"walker stalled at ({f.Position.X:F2}, {f.Position.Z:F2}) after {ticks} ticks");
    }
}
