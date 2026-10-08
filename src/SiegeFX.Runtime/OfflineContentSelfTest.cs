using System.Numerics;
using SiegeFX.Core.Actors;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Tank;

namespace SiegeFX.Runtime;

/// <summary>
/// Regression coverage for the distinction between authored multiplayer
/// content and an active network session. Synthetic checks require no game
/// data; an optional MpWorld.dsmap path adds a read-only integration check of
/// Utraea's <c>objects/regular</c> placement layout.
/// </summary>
internal static class OfflineContentSelfTest
{
    public static bool Run(string? mapPath = null)
    {
        var failures = new List<string>();

        RunCase(failures, "multiplayer-authored trigger eligibility", () =>
        {
            Check(failures, FireMultiplayerAuthoredRow(enableContent: false, networked: false) == 0,
                "Ehb/offline: single_player=false row fired without multiplayer content or networking");
            Check(failures, FireMultiplayerAuthoredRow(enableContent: true, networked: false) == 1,
                "Utraea/offline: multiplayer-authored row did not fire");
            Check(failures, FireMultiplayerAuthoredRow(enableContent: false, networked: true) == 1,
                "networked session: historical multiplayer-authored row behavior regressed");
        });

        RunCase(failures, "placement content filters", () =>
        {
            var doc = GasDocument.Parse("""
                [t:sp_actor,n:0x00000001]
                {
                    [common] { b is_multi_player = false; }
                }
                [t:mp_actor,n:0x00000002]
                {
                    [common] { b is_single_player = false; }
                }
                [t:shared_actor,n:0x00000003]
                {
                    [common] { }
                }
                """);
            var sp = doc.Roots.Single(n => n.Header.StartsWith("t:sp_actor,", StringComparison.Ordinal));
            var mp = doc.Roots.Single(n => n.Header.StartsWith("t:mp_actor,", StringComparison.Ordinal));
            var shared = doc.Roots.Single(n => n.Header.StartsWith("t:shared_actor,", StringComparison.Ordinal));

            Check(failures, RegionObjects.IsPlacementEnabled(sp, multiplayerContent: false),
                "Ehb content excluded its single-player placement");
            Check(failures, !RegionObjects.IsPlacementEnabled(mp, multiplayerContent: false),
                "Ehb content included its multiplayer-only placement twin");
            Check(failures, !RegionObjects.IsPlacementEnabled(sp, multiplayerContent: true),
                "multiplayer-authored content included its single-player-only placement twin");
            Check(failures, RegionObjects.IsPlacementEnabled(mp, multiplayerContent: true),
                "multiplayer-authored content excluded its multiplayer placement");
            Check(failures,
                RegionObjects.IsPlacementEnabled(shared, multiplayerContent: false)
                && RegionObjects.IsPlacementEnabled(shared, multiplayerContent: true),
                "unflagged placement was not shared by both content modes");
        });

        RunCase(failures, "regular placement path precedence", () =>
        {
            var paths = RegionObjects.CandidatePlacementPaths(
                "/world/maps/multiplayer_world/regions/town", "actor.gas");
            Check(failures, paths.Count == 2,
                $"expected two placement candidates, got {paths.Count}");
            if (paths.Count >= 2)
            {
                Check(failures,
                    paths[0] == "/world/maps/multiplayer_world/regions/town/objects/actor.gas",
                    $"flat placement path lost precedence: {paths[0]}");
                Check(failures,
                    paths[1] == "/world/maps/multiplayer_world/regions/town/objects/regular/actor.gas",
                    $"regular placement fallback was wrong: {paths[1]}");
            }
        });

        if (!string.IsNullOrWhiteSpace(mapPath))
            RunCase(failures, "real MpWorld regular placements",
                () => CheckRealMap(failures, ResolveMapPath(mapPath)));
        else
            Console.WriteLine("[selftest-offline-content] real-map check skipped (no MpWorld.dsmap path)");

        if (failures.Count == 0)
        {
            Console.WriteLine("[selftest-offline-content] OK — offline multiplayer-authored triggers, " +
                              "placement filters, and regular-path fallback passed");
            return true;
        }

        Console.Error.WriteLine($"[selftest-offline-content] FAIL ({failures.Count}):");
        foreach (var failure in failures) Console.Error.WriteLine("  " + failure);
        return false;
    }

    private static int FireMultiplayerAuthoredRow(bool enableContent, bool networked)
    {
        var rowNode = GasDocument.Parse("""
            [*]
            {
                b single_player = false;
                condition* = party_member_within_sphere(5.0, "on_first_enter");
                action* = mood_change("offline_content_selftest");
            }
            """).Roots.Single();
        var diagnostics = new List<string>();
        var row = TriggerRow.Parse(rowNode, "offline-content-selftest", diagnostics);
        if (diagnostics.Count != 0)
            throw new InvalidDataException(string.Join("; ", diagnostics));

        var runtime = new TriggerRuntime
        {
            EnableMultiplayerAuthoredTriggers = enableContent,
            IsMultiplayerSession = networked,
        };
        runtime.Register(new TriggerInstance(
            scid: 1, nodeGuid: 0, position: Vector3.Zero,
            matrix: new TriggerMatrix(new[] { row }), startActive: true));
        var context = new SatisfiedVolumeContext();
        runtime.Tick(1.0 / 20.0, context);
        int initialChanges = context.MoodChanges;
        if (enableContent)
        {
            runtime.IsMultiplayerSession = true;
            runtime.IsMultiplayerSession = false;
            runtime.Register(new TriggerInstance(2, 0, Vector3.Zero,
                new TriggerMatrix(new[] { row }), startActive: true));
            runtime.Tick(1.0 / 20.0, context);
            if (context.MoodChanges != initialChanges + 1)
                throw new InvalidDataException("Network teardown changed multiplayer-authored content eligibility.");
        }
        return initialChanges;
    }


    private static void CheckRealMap(List<string> failures, string mapPath)
    {
        if (!File.Exists(mapPath))
        {
            failures.Add($"real map not found: {mapPath}");
            return;
        }

        using var mapTank = TankFile.Open(mapPath);
        var reader = new TankReader(mapTank);
        var files = reader.ListFiles().ToArray();
        int actorCount = LoadRegularLayer(reader, files, "actor.gas");
        int specialCount = LoadRegularLayer(reader, files, "special.gas");

        Check(failures, actorCount > 0,
            "MpWorld regular actor layers resolved but produced no enabled placements");
        Check(failures, specialCount > 0,
            "MpWorld regular special layers resolved but produced no enabled placements");
        Console.WriteLine($"[selftest-offline-content] MpWorld regular placements: " +
                          $"actors={actorCount}, special={specialCount}");
    }

    private static int LoadRegularLayer(TankReader reader, IReadOnlyList<string> files, string fileName)
    {
        const string marker = "/objects/regular/";
        var suffix = marker + fileName;
        var regions = files
            .Where(path => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .Select(path => path[..^suffix.Length])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        int count = 0;
        foreach (var region in regions)
        {
            var (placements, _) = RegionObjects.LoadPlacements(
                reader, region, fileName, multiplayerContent: true);
            count += placements.Count;
        }
        return count;
    }

    private static string ResolveMapPath(string path) => Directory.Exists(path)
        ? Path.Combine(path, "Maps", "MpWorld.dsmap")
        : path;

    private static void RunCase(List<string> failures, string name, Action test)
    {
        try { test(); }
        catch (Exception ex)
        {
            failures.Add($"{name}: threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Check(List<string> failures, bool condition, string message)
    {
        if (!condition) failures.Add(message);
    }

    private sealed class SatisfiedVolumeContext : TriggerContext
    {
        public int MoodChanges { get; private set; }

        public override bool PartyMemberWithinSphere(Vector3 center, float radius) => true;
        public override void ChangeMood(string moodName) => MoodChanges++;
    }

}
