using SiegeFX.Core.Save;
using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>The world tank and coordinate-frame root passed to --play-region.</summary>
public sealed record WorldLaunchPlan(string MapTankPath, string RegionPath)
{
    public static bool MatchesCoordinateFrame(SaveFile save, string? worldId, string rootRegion) =>
        save.WorldId == worldId && string.Equals(
            save.RegionPath.Replace('\\', '/').Trim('/'),
            rootRegion.Replace('\\', '/').Trim('/'), StringComparison.OrdinalIgnoreCase);

    public static WorldLaunchPlan ForNewGame(string mapsDirectory, WorldProfile profile)
    {
        var mapPath = profile.ResolveMapPath(mapsDirectory);
        using var tank = TankFile.Open(mapPath);
        var start = WorldStartResolver.Resolve(new TankReader(tank), profile);
        return new WorldLaunchPlan(mapPath, start.RegionPath);
    }

    public static WorldLaunchPlan ForSave(string mapsDirectory, SaveFile save)
    {
        var profile = WorldProfile.Get(save.WorldId);
        if (!profile.ContainsRegion(save.RegionPath))
            throw new InvalidDataException("Save coordinate frame does not belong to its world.");
        return new WorldLaunchPlan(profile.ResolveMapPath(mapsDirectory), save.RegionPath);
    }
}
