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

        RunCase(failures, "church trigger-group fade edges", () =>
            CheckChurchFadeGroup(failures));

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

        RunCase(failures, "static prop texture precedence", () =>
        {
            var templates = new TemplateStore();
            Check(failures, templates.RegisterFromGasText("""
                [t:template,n:sign_base]
                {
                    [aspect] { [textures] { 0 = b_i_glb_sign-barn-03; } }
                }
                [t:template,n:sign_magic]
                {
                    specializes = sign_base;
                    [aspect] { [textures] { 0 = b_i_glb_sign-magicshop-01; } }
                }
                [t:template,n:sign_inherited]
                {
                    specializes = sign_magic;
                }
                [t:template,n:sign_unskinned] { }
                """) == 4, "could not register synthetic sign templates");
            Check(failures, templates.TryGet("sign_inherited", out var inherited),
                "inherited sign template missing");
            if (inherited is null) return;
            var plain = GasDocument.Parse("[t:sign_inherited,n:0x1] { }").Roots.Single();
            var placed = GasDocument.Parse("""
                [t:sign_inherited,n:0x2]
                {
                    [aspect] { [textures] { 0 = b_i_glb_sign-instance; } }
                }
                """).Roots.Single();
            var shorthand = GasDocument.Parse("""
                [t:sign_inherited,n:0x3]
                {
                    aspect:textures:0 = b_i_glb_sign-flat;
                }
                """).Roots.Single();
            Check(failures, StaticPropTextureResolver.AuthoredTexture(templates, inherited, plain)
                == "b_i_glb_sign-magicshop-01", "inherited template sign skin lost");
            Check(failures, StaticPropTextureResolver.AuthoredTexture(templates, inherited, placed)
                == "b_i_glb_sign-instance", "instance sign skin did not override template");
            Check(failures, StaticPropTextureResolver.AuthoredTexture(templates, inherited, shorthand)
                == "b_i_glb_sign-flat", "colon-shorthand instance skin did not override template");
            Check(failures, templates.TryGet("sign_unskinned", out var unskinned)
                && StaticPropTextureResolver.AuthoredTexture(templates, unskinned, plain) is null,
                "unskinned prop did not leave the ASP default available");
        });

        if (!string.IsNullOrWhiteSpace(mapPath))
            RunCase(failures, "real MpWorld regular placements",
                () => CheckRealMap(failures, ResolveMapPath(mapPath),
                    requireResources: Directory.Exists(mapPath)));
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

    private static void CheckChurchFadeGroup(List<string> failures)
    {
        // The shipped Elddim chapel has a volume producer with this group and
        // an enter/leave controller with paired fades. A brief enter pulse
        // must not be mistaken for leaving a held fade volume on the next tick.
        static TriggerRow Row(string gas) => TriggerRow.Parse(GasDocument.Parse(gas).Roots.Single(),
            "church-group-regression", new List<string>());
        var producer = Row("""
            [*]
            {
                occupants_group = town_center_house_3;
                condition* = party_member_within_bounding_box(9.2,2.0,7.1,"on_every_enter");
            }
            """);
        var controller = Row("""
            [*]
            {
                condition* = party_member_left_trigger_group("town_center_house_3","on_every_leave"), group(2);
                condition* = party_member_entered_trigger_group("town_center_house_3","on_every_enter"), group(1);
                action* = fade_nodes(0xD5A3ACD9,1,3,-1,"out:black"), group(1);
                action* = fade_nodes(0xD5A3ACD9,1,3,-1,"in"), group(2);
            }
            """);
        var runtime = new TriggerRuntime { EnableMultiplayerAuthoredTriggers = true };
        runtime.Register(new TriggerInstance(1, 0, Vector3.Zero,
            new TriggerMatrix(new[] { producer }), true, "trigger_fade_nodes_box"));
        runtime.Register(new TriggerInstance(2, 0, Vector3.Zero,
            new TriggerMatrix(new[] { controller }), true, "trigger_fade_nodes_box"));
        var ctx = new ChurchFadeContext();
        runtime.Tick(0.05, ctx);
        ctx.Inside = true;
        runtime.Tick(0.05, ctx);
        Check(failures, ctx.Modes.SequenceEqual(new[] { "out:black" }),
            "church entry did not dispatch one roof hide");
        runtime.Tick(0.05, ctx);
        Check(failures, ctx.Modes.SequenceEqual(new[] { "out:black" }),
            "transient group condition auto-reversed the roof hide while inside");
        ctx.Inside = false;
        runtime.Tick(0.05, ctx);
        Check(failures, ctx.Modes.SequenceEqual(new[] { "out:black", "in" }),
            "church exit did not dispatch exactly one roof reveal");

        // Held-volume fades without an authored reveal still need the legacy
        // synthesized reversal for every spatial condition we evaluate.
        foreach (var condition in new[]
        {
            "party_member_within_bounding_box(2,2,2,\"on_every_enter\")",
            "party_member_within_sphere(2,\"on_every_enter\")",
            "actor_within_bounding_box(2,2,2,\"on_every_enter\")",
            "actor_within_sphere(2,\"on_every_enter\")",
            "go_within_bounding_box(2,2,2,\"on_every_enter\")",
            "go_within_sphere(2,\"on_every_enter\")",
        })
        {
            var held = Row($$"""
                [*]
                {
                    condition* = {{condition}};
                    action* = fade_nodes(0x00000001,1,-1,-1,"out:black");
                }
                """);
            runtime = new TriggerRuntime();
            runtime.Register(new TriggerInstance(3, 0, Vector3.Zero,
                new TriggerMatrix(new[] { held }), true, "trigger_fade_nodes_box"));
            ctx = new ChurchFadeContext { Inside = true };
            runtime.Tick(0.05, ctx);
            ctx.Inside = false;
            runtime.Tick(0.05, ctx);
            Check(failures, ctx.Modes.SequenceEqual(new[] { "out:black", "in" }),
                $"held-volume fade lost its automatic exit reveal for {condition}");
        }
    }

    private static void CheckRealMap(List<string> failures, string mapPath, bool requireResources)
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

        // The chapel sign exposes the shared-mesh material rule in real data:
        // its ASP defaults to a barn/mule skin, while the sign template asks
        // for the magic-shop potion-and-scroll skin.
        const string chapelRegion = "/world/maps/multiplayer_world/regions/town_center";
        var (interactive, _) = RegionObjects.LoadPlacements(
            reader, chapelRegion, "interactive.gas", multiplayerContent: true);
        var sign = interactive.FirstOrDefault(p => p.Scid == 0x03200c55);
        Check(failures, sign is not null && sign.TemplateName == "sign_glb_magicshop_01",
            "Elddim chapel magic-shop sign placement missing");
        var installRoot = Directory.GetParent(Path.GetDirectoryName(mapPath)!)?.FullName;
        var logicPath = installRoot is null ? null : Path.Combine(installRoot, "Resources", "Logic.dsres");
        if (logicPath is null || !File.Exists(logicPath))
        {
            if (requireResources)
                failures.Add($"Logic.dsres not found beside MpWorld.dsmap: {logicPath}");
            else
                Console.WriteLine("[selftest-offline-content] sign-material check skipped " +
                                  "(standalone MpWorld.dsmap path; Logic.dsres unavailable)");
            return;
        }
        if (sign is not null)
        {
            using var logic = TankFile.Open(logicPath);
            var (templates, _) = TemplateStore.LoadFromTank(new TankReader(logic));
            Check(failures, templates.TryGet(sign.TemplateName, out var template),
                "Elddim chapel sign template missing from Logic.dsres");
            if (template is not null)
                Check(failures,
                    StaticPropTextureResolver.AuthoredTexture(templates, template, sign.Node)
                    == "b_i_glb_sign-magicshop-01",
                    "Elddim chapel sign did not resolve its authored potion skin");
        }
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

    private sealed class ChurchFadeContext : TriggerContext
    {
        public bool Inside;
        public List<string> Modes { get; } = new();
        public override bool PartyMemberWithinBox(Vector3 center, Quaternion orientation,
            float halfX, float halfY, float halfZ) => Inside;
        public override bool PartyMemberWithinSphere(Vector3 center, float radius) => Inside;
        public override bool AnyActorWithinBox(Vector3 center, Quaternion orientation,
            float halfX, float halfY, float halfZ, uint exceptScid) => Inside;
        public override bool AnyActorWithinSphere(Vector3 center, float radius, uint exceptScid) => Inside;
        public override bool AnyGoWithinBox(Vector3 center, Quaternion orientation,
            float halfX, float halfY, float halfZ, uint scidFilter, string templateFilter) => Inside;
        public override void FadeNodes(string verb, IReadOnlyList<string> args) => Modes.Add(args[^1]);
    }
}
