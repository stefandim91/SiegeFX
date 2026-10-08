using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>Finds the region owning the authored default spawn node.</summary>
public static class WorldStartResolver
{
    public sealed record Start(string RegionPath, StartPosition Position);

    public static Start Resolve(TankReader map, WorldProfile profile)
    {
        var (groups, diagnostics) = StartPositionsStore.Load(map, profile.MapRoot + "/info");
        var position = StartPositionsStore.FindDefault(groups)
            ?? throw new InvalidDataException($"No authored start for {profile.DisplayName}: {string.Join("; ", diagnostics)}");
        const string suffix = "/terrain_nodes/nodes.gas";
        string? owner = null;
        foreach (var path in map.ListFiles().Where(p =>
                     p.StartsWith(profile.MapRoot + "/regions/", StringComparison.OrdinalIgnoreCase)
                     && p.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            var graph = RegionGraph.Load(map.ExtractToMemory(path));
            if (!graph.TryGetNode(position.NodeGuid, out _)) continue;
            if (owner is not null)
                throw new InvalidDataException($"Start node {position.NodeGuid:x8} occurs in multiple regions.");
            owner = path[..^suffix.Length];
        }
        return new Start(owner ?? throw new InvalidDataException(
            $"Start node {position.NodeGuid:x8} has no region in {profile.DisplayName}."), position);
    }
}
