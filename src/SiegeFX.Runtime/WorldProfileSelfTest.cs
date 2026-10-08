using System.Text.Json;
using SiegeFX.Core.Actors;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Save;

namespace SiegeFX.Runtime;

/// <summary>Synthetic Milestone 1 coverage for authored-world selection and
/// save identity. Uses no Dungeon Siege data.</summary>
internal static class WorldProfileSelfTest
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static bool Run()
    {
        var failures = new List<string>();
        var root = Path.Combine(Path.GetTempPath(), $"siegefx_selftest_world_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            CatalogCases(failures, root);
            ModernSaveCases(failures, root);
            LegacyMigrationCases(failures, root);
            SlotIsolationCases(failures);
        }
        catch (Exception ex)
        {
            failures.Add($"self-test setup: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (Exception ex)
            {
                failures.Add($"temporary-directory cleanup: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (failures.Count == 0)
        {
            Console.WriteLine("[selftest-world-profile] OK — world catalog, save validation, " +
                              "legacy migration, location round-trip, and slot isolation passed");
            return true;
        }

        Console.Error.WriteLine($"[selftest-world-profile] FAIL ({failures.Count}):");
        foreach (var failure in failures) Console.Error.WriteLine("  " + failure);
        return false;
    }

    private static void CatalogCases(List<string> failures, string root)
    {
        var ehb = WorldProfile.Get("KingdomOfEhb");
        var utraea = WorldProfile.Get("UtraeanPeninsula");
        Check(failures, ehb.MapFileName == "World.dsmap", "Ehb map file mismatch");
        Check(failures, utraea.MapFileName == "MpWorld.dsmap", "Utraea map file mismatch");
        Check(failures, !ehb.EnableMultiplayerAuthoredTriggers,
            "Ehb unexpectedly enables multiplayer-authored triggers");
        Check(failures, utraea.EnableMultiplayerAuthoredTriggers,
            "Utraea did not enable multiplayer-authored triggers");
        Check(failures,
            ReferenceEquals(WorldProfile.TryFromRegion(
                "world\\maps\\multiplayer_world\\regions\\town_center"), utraea),
            "normalized Utraea region did not select Utraea");
        Check(failures,
            WorldProfile.TryFromRegion("/world/maps/map_worldish/regions/fh_r1") is null,
            "map-root prefix collision selected a world");
        Check(failures,
            WorldProfile.TryFromRegion("/world/maps/map_world/regions/../multiplayer_world") is null,
            "dot-segment region selected a world");
        Check(failures,
            WorldProfile.TryFromRegion("/world/maps/map_world/regions/fh_r1/objects") is null,
            "nested path beneath a region selected a world");
        Check(failures,
            utraea.ResolveMapPath(root) == Path.Combine(root, "MpWorld.dsmap"),
            "Utraea map path resolution mismatch");

        try
        {
            _ = WorldProfile.Get("unknown");
            failures.Add("unknown world id was accepted");
        }
        catch (ArgumentOutOfRangeException) { }
    }

    private static void ModernSaveCases(List<string> failures, string root)
    {
        var path = Path.Combine(root, "utraea-roundtrip.save");
        var location = new Vec3(314.1f, -5.9f, -306.9f);
        var save = MakeCurrent(
            WorldProfile.UtraeanPeninsula,
            "SoloAdventure",
            "/world/maps/multiplayer_world/regions/town_center");
        save.PlayerRegion = "/world/maps/multiplayer_world/regions/town_center";
        save.Actors.Add(new ActorSnapshot { Scid = 42, TemplateName = "hero", Position = location });
        SaveStore.Save(path, save);
        var loaded = SaveStore.Load(path);
        Check(failures, loaded.WorldId == WorldProfile.UtraeanPeninsulaId,
            "round-trip lost Utraea world id");
        Check(failures, loaded.Actors.Count == 1 && loaded.Actors[0].Position.Equals(location),
            "round-trip lost actor location");

        var mismatch = MakeCurrent(
            WorldProfile.UtraeanPeninsula,
            "SoloAdventure",
            "/world/maps/map_world/regions/fh_r1");
        WriteRaw(Path.Combine(root, "mismatch.save"), mismatch);
        ExpectInvalid(failures, Path.Combine(root, "mismatch.save"),
            "modern world/region mismatch was accepted");

        var unknown = MakeCurrent(
            WorldProfile.KingdomOfEhb,
            "OriginalCampaign",
            "/world/maps/map_world/regions/fh_r1");
        unknown.WorldId = "UnknownWorld";
        WriteRaw(Path.Combine(root, "unknown-world.save"), unknown);
        ExpectInvalid(failures, Path.Combine(root, "unknown-world.save"),
            "modern unknown world id was accepted");

        var missingRegion = MakeCurrent(
            WorldProfile.KingdomOfEhb,
            "OriginalCampaign",
            "");
        WriteRaw(Path.Combine(root, "missing-region.save"), missingRegion);
        ExpectInvalid(failures, Path.Combine(root, "missing-region.save"),
            "modern save without RegionPath was accepted");

        var playerMismatch = MakeCurrent(
            WorldProfile.KingdomOfEhb,
            "OriginalCampaign",
            "/world/maps/map_world/regions/fh_r1");
        playerMismatch.PlayerRegion = "/world/maps/multiplayer_world/regions/town_center";
        WriteRaw(Path.Combine(root, "player-region-mismatch.save"), playerMismatch);
        ExpectInvalid(failures, Path.Combine(root, "player-region-mismatch.save"),
            "modern PlayerRegion/world mismatch was accepted");
    }

    private static void LegacyMigrationCases(List<string> failures, string root)
    {
        var ehbPath = Path.Combine(root, "legacy-ehb.save");
        WriteLegacy(ehbPath, "/world/maps/map_world/regions/fh_r1");
        var ehb = SaveStore.Load(ehbPath);
        Check(failures, ehb.WorldId == WorldProfile.KingdomOfEhbId,
            "v13 Ehb migration selected the wrong world");
        Check(failures, ehb.AdventureMode == "OriginalCampaign",
            "v13 Ehb migration selected the wrong mode");
        Check(failures, Guid.TryParse(ehb.SaveSetId, out _),
            "v13 Ehb migration did not create a save-set id");
        Check(failures, SaveStore.Load(ehbPath).SaveSetId == ehb.SaveSetId,
            "v13 migration save-set id was not stable for the slot path");

        var utraeaPath = Path.Combine(root, "legacy-utraea.save");
        WriteLegacy(path: utraeaPath,
            region: "world/maps/multiplayer_world/regions/town_center");
        var utraea = SaveStore.Load(path: utraeaPath);
        Check(failures, utraea.WorldId == WorldProfile.UtraeanPeninsulaId,
            "v13 Utraea migration selected the wrong world");
        Check(failures, utraea.AdventureMode == "SoloAdventure",
            "v13 Utraea migration selected the wrong mode");

        var unknownPath = Path.Combine(root, "legacy-unknown.save");
        WriteLegacy(unknownPath, "/world/maps/community_world/regions/start");
        ExpectInvalid(failures, unknownPath, "legacy unknown map root was accepted");
    }

    private static void SlotIsolationCases(List<string> failures)
    {
        var first = Guid.NewGuid().ToString("D");
        var second = Guid.NewGuid().ToString("D");
        Check(failures, SaveStore.QuicksavePath(first) != SaveStore.QuicksavePath(second),
            "different save sets shared a quicksave path");
        Check(failures, SaveStore.AutoSavePath(first) != SaveStore.AutoSavePath(second),
            "different save sets shared an autosave path");
        Check(failures, Path.GetFileName(SaveStore.QuicksavePath(first)) == $"quicksave-{first}.save",
            "quicksave did not use a GUID suffix");
        Check(failures, Path.GetFileName(SaveStore.AutoSavePath(first)) == $"autosave-{first}.save",
            "autosave did not use a GUID suffix");
        try
        {
            _ = SaveStore.QuicksavePath("not-a-guid");
            failures.Add("invalid quicksave save-set id was accepted");
        }
        catch (ArgumentException) { }
    }

    private static SaveFile MakeCurrent(
        WorldProfile profile,
        string mode,
        string region) => new()
    {
        SchemaVersion = SaveFile.CurrentSchemaVersion,
        WorldId = profile.Id,
        SaveSetId = "8bb9bbae-f283-45c2-9a46-cf86d2b28f4b",
        AdventureMode = mode,
        EngineVersion = "selftest",
        RegionPath = region,
        Player = new PlayerSnapshot { HeroName = "Self Test" },
    };

    private static void WriteLegacy(string path, string region)
    {
        var save = new SaveFile
        {
            SchemaVersion = 13,
            RegionPath = region,
            PlayerRegion = region,
        };
        WriteRaw(path, save);
    }

    private static void WriteRaw(string path, SaveFile save) =>
        File.WriteAllText(path, JsonSerializer.Serialize(save, Json));

    private static void ExpectInvalid(List<string> failures, string path, string message)
    {
        try { _ = SaveStore.Load(path); failures.Add(message); }
        catch (InvalidDataException) { }
    }

    private static void Check(List<string> failures, bool condition, string message)
    {
        if (!condition) failures.Add(message);
    }
}
