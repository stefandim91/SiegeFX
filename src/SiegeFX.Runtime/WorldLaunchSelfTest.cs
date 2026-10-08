using SiegeFX.Core.Assets;
using SiegeFX.Core.Save;
using SiegeFX.Core.Tank;

namespace SiegeFX.Runtime;

/// <summary>Read-only launch checks against an external legitimate DS1 install.</summary>
internal static class WorldLaunchSelfTest
{
    public static bool Run(string installDirectory)
    {
        try
        {
            var maps = Path.Combine(installDirectory, "Maps");
            foreach (var profile in WorldProfile.All)
            {
                var launch = WorldLaunchPlan.ForNewGame(maps, profile);
                using var map = TankFile.Open(launch.MapTankPath);
                var reader = new TankReader(map);
                var start = WorldStartResolver.Resolve(reader, profile);
                var graph = RegionGraph.Load(reader.ExtractToMemory(launch.RegionPath + "/terrain_nodes/nodes.gas"));
                if (!graph.TryGetNode(start.Position.NodeGuid, out _))
                    throw new InvalidDataException("Launch region does not contain the authored start.");
                if (profile == WorldProfile.UtraeanPeninsula &&
                    (!launch.RegionPath.EndsWith("/town_center", StringComparison.OrdinalIgnoreCase)
                    || start.Position.NodeGuid != 0x4ee0a82e))
                    throw new InvalidDataException("Installed Utraea default is different from the verified Elddim start.");
                var (actors, errors) = RegionObjects.LoadActors(reader, launch.RegionPath,
                    profile.EnableMultiplayerAuthoredTriggers);
                if (errors.Count != 0 || actors.Count == 0)
                    throw new InvalidDataException("Launch actor layer is empty or invalid: " + string.Join("; ", errors));
                var snapshot = new SaveFile { WorldId = profile.Id, RegionPath = launch.RegionPath };
                var restored = WorldLaunchPlan.ForSave(maps, snapshot);
                if (restored != launch) throw new InvalidDataException("Saved world changes the launch tank or coordinate frame.");
                Console.WriteLine($"[selftest-world-launch] {profile.Id}: {launch.RegionPath}, " +
                    $"start node={start.Position.NodeGuid:x8}, slot={start.Position.Id}, actors={actors.Count}, map={Path.GetFileName(launch.MapTankPath)}");
            }
            // A save's displayed location is not its coordinate-frame root.
            var traveled = new SaveFile
            {
                WorldId = WorldProfile.UtraeanPeninsulaId,
                RegionPath = "/world/maps/multiplayer_world/regions/town_center",
                PlayerRegion = "/world/maps/multiplayer_world/regions/another_region"
            };
            if (WorldLaunchPlan.ForSave(maps, traveled).RegionPath != traveled.RegionPath)
                throw new InvalidDataException("Load substituted the player region for the saved coordinate frame.");
            if (WorldLaunchPlan.MatchesCoordinateFrame(traveled, traveled.WorldId, traveled.PlayerRegion))
                throw new InvalidDataException("A different streamed region was accepted as the save coordinate frame.");
            if (!WorldLaunchPlan.MatchesCoordinateFrame(traveled, traveled.WorldId, traveled.RegionPath.ToUpperInvariant() + "/"))
                throw new InvalidDataException("Equivalent normalized frame was refused.");
            Console.WriteLine("[selftest-world-launch] PASS (headless; gameplay/streaming still require a playtest)");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[selftest-world-launch] FAIL: " + ex.Message);
            return false;
        }
    }
}
