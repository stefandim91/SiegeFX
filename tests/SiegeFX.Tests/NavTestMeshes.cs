using System.Numerics;
using SiegeFX.Core.Nav;

namespace SiegeFX.Tests;

/// <summary>Small flat nav meshes made of unit cells. Neighbouring cells share
/// vertices, so they come out edge-connected like welded game floor.</summary>
internal static class NavTestMeshes
{
    public static NavMesh FromCells(IEnumerable<(int X, int Z)> cells)
    {
        var verts = new List<Vector3>();
        var index = new Dictionary<(int, int), int>();
        var tris = new List<int>();
        int V(int x, int z)
        {
            if (!index.TryGetValue((x, z), out var i))
            {
                i = verts.Count;
                verts.Add(new Vector3(x, 0f, z));
                index[(x, z)] = i;
            }
            return i;
        }
        foreach (var (x, z) in cells)
        {
            int a = V(x, z), b = V(x + 1, z), c = V(x + 1, z + 1), d = V(x, z + 1);
            tris.AddRange(new[] { a, b, c, a, c, d });
        }
        return NavMesh.FromTriangles(verts.ToArray(), tris.ToArray());
    }

    public static IEnumerable<(int X, int Z)> Rect(int x0, int z0, int x1, int z1)
    {
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
                yield return (x, z);
    }
}
