using System.Numerics;
using System.Text;
using SiegeFX.Core.Actors;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Tank;

namespace SiegeFX.Runtime;

/// <summary>Headless regressions for authored open/close prop poses and posed picking.</summary>
internal static class LeverPoseSelfTest
{
    public static bool Run(string? installRoot = null)
    {
        var failures = new List<string>();
        RunCase(failures, "synthetic transition state", CheckSyntheticTransitions);
        RunCase(failures, "synthetic posed ray picking", CheckSyntheticRayPicking);
        RunCase(failures, "non-unit skin weights", CheckNonUnitSkinWeights);

        if (!string.IsNullOrWhiteSpace(installRoot))
            RunCase(failures, "original orchard-cellar levers",
                () => CheckOriginalLevers(installRoot, failures));
        else
            Console.WriteLine("[selftest-lever-pose] original-asset check skipped (no install root)");

        if (failures.Count == 0)
        {
            Console.WriteLine("[selftest-lever-pose] PASS — transition state, posed picking" +
                (string.IsNullOrWhiteSpace(installRoot) ? "" : ", and original lever clips"));
            return true;
        }

        Console.Error.WriteLine($"[selftest-lever-pose] FAIL ({failures.Count})");
        foreach (var failure in failures) Console.Error.WriteLine("  " + failure);
        return false;
    }

    private static void CheckSyntheticTransitions()
    {
        var mesh = CreateSyntheticMesh();
        var open = CreateTranslationClip(Vector3.Zero, new Vector3(3f, 0f, 0f));
        var close = CreateTranslationClip(new Vector3(3f, 0f, 0f), Vector3.Zero);
        var pose = new UsableTransitionPose(mesh, open, close);
        var skinIdentity = pose.SkinMatrices;
        var positionIdentity = pose.Positions;

        Require(!pose.IsOpen && !pose.IsTransitioning, "new pose was not closed and settled");
        Require(Near(pose.ClipTime, close.AnimLength), "new pose did not sample close endpoint");
        Require(Near(pose.Center.X, 0f), "new pose did not use the authored closed endpoint");

        pose.SetOpen(true, 10d);
        Require(pose.IsOpen && pose.IsTransitioning, "open request did not start transition");
        Require(Near(pose.Center.X, 0f), "open transition did not start at clip time zero");
        pose.Update(10.25d);
        Require(Near(pose.Center.X, 0.75f), "open transition sampled the wrong quarter pose");

        // A repeated logical request must not move the transition's clock origin.
        pose.SetOpen(true, 99d);
        pose.Update(10.5d);
        Require(Near(pose.Center.X, 1.5f), "same-state request restarted the open transition");
        pose.Update(12d);
        Require(pose.IsOpen && !pose.IsTransitioning, "open transition did not settle");
        Require(Near(pose.ClipTime, open.AnimLength) && Near(pose.Center.X, 3f),
            "open transition did not clamp to its endpoint");

        pose.SetOpen(false, 20d);
        pose.Update(20.5d);
        Require(!pose.IsOpen && pose.IsTransitioning && Near(pose.Center.X, 1.5f),
            "close transition did not play in the authored direction");
        pose.SetOpen(false, 20.5d, snap: true);
        Require(!pose.IsTransitioning && Near(pose.Center.X, 0f),
            "same-state restore snap did not force the close endpoint");

        pose.SetOpen(true, 30d);
        pose.Update(30.25d);
        pose.SetOpen(false, 30.25d, snap: true);
        Require(!pose.IsOpen && !pose.IsTransitioning && Near(pose.Center.X, 0f),
            "restore snap did not reverse an in-flight transition");
        pose.SetOpen(true, 40d, snap: true);
        pose.SetOpen(true, 41d, snap: true);
        Require(pose.IsOpen && !pose.IsTransitioning && Near(pose.Center.X, 3f),
            "same-state open snap did not retain the open endpoint");

        Require(ReferenceEquals(skinIdentity, pose.SkinMatrices)
            && ReferenceEquals(positionIdentity, pose.Positions),
            "pose replaced one of its reusable arrays");

        var second = new UsableTransitionPose(mesh, open, close);
        Require(!ReferenceEquals(second.SkinMatrices, pose.SkinMatrices)
            && !ReferenceEquals(second.Positions, pose.Positions),
            "two usable instances shared mutable pose arrays");
        Require(!second.IsOpen && Near(second.Center.X, 0f) && Near(pose.Center.X, 3f),
            "one usable instance changed another instance's state");
    }

    private static void CheckSyntheticRayPicking()
    {
        var mesh = CreateSyntheticMesh();
        var pose = new UsableTransitionPose(mesh,
            CreateTranslationClip(Vector3.Zero, new Vector3(3f, 0f, 0f)),
            CreateTranslationClip(new Vector3(3f, 0f, 0f), Vector3.Zero));
        pose.SetOpen(true, 0d, snap: true);

        var world = Matrix4x4.CreateScale(2f, 0.75f, 1.5f)
            * Matrix4x4.CreateRotationY(0.63f)
            * Matrix4x4.CreateRotationX(-0.31f)
            * Matrix4x4.CreateTranslation(8f, -2f, 11f);
        var posedPoint = Vector3.Transform(new Vector3(3f, 0f, 0f), world);
        var wa = Vector3.Transform(pose.Positions[0], world);
        var wb = Vector3.Transform(pose.Positions[1], world);
        var wc = Vector3.Transform(pose.Positions[2], world);
        var normal = Vector3.Normalize(Vector3.Cross(wb - wa, wc - wa));
        var origin = posedPoint - normal * 5f;

        Require(pose.TryRayHit(origin, normal, world, 6f, out var distance),
            "rotated/scaled posed triangle did not pick");
        Require(Near(distance, 5f, 0.001f),
            $"posed hit lost world-distance parameter (expected 5, got {distance})");
        Require(!pose.TryRayHit(origin, normal, world, 4.9f, out _),
            "posed hit ignored max distance");

        var rawPoint = Vector3.Transform(Vector3.Zero, world);
        Require(!pose.TryRayHit(rawPoint - normal * 5f, normal, world, 6f, out _),
            "ray through the raw vertex position hit the translated posed model");
        Require(distance < 5.5f && distance > 4.5f,
            "posed model was not reached before the synthetic clicked floor");
        Require(!(distance < 4.5f),
            "posed model incorrectly won when the clicked floor was in front");

        Require(!pose.TryRayHit(origin, Vector3.Zero, world, 6f, out _),
            "zero-length ray direction was accepted");
        Require(!pose.TryRayHit(origin, normal, new Matrix4x4(), 6f, out _),
            "non-invertible world matrix was accepted");
    }

    private static void CheckNonUnitSkinWeights()
    {
        var mesh = CreateSyntheticMesh(0.5f);
        var pose = new UsableTransitionPose(mesh,
            CreateTranslationClip(Vector3.Zero, new Vector3(3f, 0f, 0f)),
            CreateTranslationClip(new Vector3(3f, 0f, 0f), Vector3.Zero));
        pose.SetOpen(true, 0d, snap: true);
        Require(Near(pose.Center.X, 3f),
            "non-unit weights shifted CPU geometry away from the rendered position");
        Require(pose.TryRayHit(new Vector3(3f, 0f, -3f), Vector3.UnitZ,
                Matrix4x4.Identity, 4f, out var distance) && Near(distance, 3f),
            "non-unit weights made the rendered triangle unpickable");
    }

    private static void CheckOriginalLevers(string installRoot, List<string> failures)
    {
        var root = ResolveInstallRoot(installRoot);
        var mapPath = Path.Combine(root, "Maps", "MpWorld.dsmap");
        var logicPath = Path.Combine(root, "Resources", "Logic.dsres");
        var objectsPath = Path.Combine(root, "Resources", "Objects.dsres");
        foreach (var path in new[] { mapPath, logicPath, objectsPath })
            if (!File.Exists(path)) throw new FileNotFoundException("Required original asset is missing.", path);

        using var map = TankFile.Open(mapPath);
        using var logic = TankFile.Open(logicPath);
        using var objects = TankFile.Open(objectsPath);
        var mapReader = new TankReader(map);
        var logicReader = new TankReader(logic);
        var objectsReader = new TankReader(objects);
        var (templates, _) = TemplateStore.LoadFromTank(logicReader);
        var resolver = new AssetResolver();
        resolver.Add(mapReader, "MpWorld.dsmap");
        resolver.Add(objectsReader, "Objects.dsres");
        resolver.Add(logicReader, "Logic.dsres");
        var spawner = new ActorSpawner(templates, resolver);

        const string region = "/world/maps/multiplayer_world/regions/orchard_cellar";
        var placements = new List<ActorInstance>();
        foreach (var fileName in RegionObjects.PlacementFileNames(mapReader, region))
        {
            var (loaded, _) = RegionObjects.LoadPlacements(
                mapReader, region, fileName, multiplayerContent: true);
            placements.AddRange(loaded);
        }

        var upper = placements.FirstOrDefault(p => p.Scid == 0x08100047);
        var lower = placements.FirstOrDefault(p => p.Scid == 0x08100051);
        Require(upper is not null && lower is not null,
            "orchard-cellar upper/lower lever placements were not found");
        Require(upper!.TemplateName.Equals("lever_glb_01", StringComparison.OrdinalIgnoreCase),
            $"upper lever resolved as {upper.TemplateName}, expected lever_glb_01");
        Require(lower!.TemplateName.Equals("lever_glb_07", StringComparison.OrdinalIgnoreCase),
            $"lower lever resolved as {lower.TemplateName}, expected lever_glb_07");

        var upperResult = LoadOriginalPose(upper, templates, resolver, spawner);
        var lowerResult = LoadOriginalPose(lower, templates, resolver, spawner);
        Require(templates.RegisterFromGasText("""
            [t:template,n:siegefx_test_inherited_switch]
            {
                specializes = lever_glb_01;
                [body] { [chore_dictionary] { [chore_open] { skrit = transition; } } }
            }
            """) == 1,
            "partial switch override template did not register");
        Require(templates.TryGet("siegefx_test_inherited_switch", out var derived),
            "partial switch override template was not found");
        var inherited = spawner.LoadTransitionClip(derived!,
            ActorInstance.CreateSynthetic(derived!.Name, 0, Vector3.Zero, Quaternion.Identity),
            "chore_open");
        Require(inherited is not null,
            "partial chore override hid inherited anim_files entries");
        Require(upperResult.OpenSuffix.Equals("switch-01-close", StringComparison.OrdinalIgnoreCase),
            $"upper chore_open mapped to {upperResult.OpenSuffix}, expected switch-01-close");
        Require(upperResult.CloseSuffix.Equals("switch-01-open", StringComparison.OrdinalIgnoreCase),
            $"upper chore_close mapped to {upperResult.CloseSuffix}, expected switch-01-open");

        var rawCenter = CornerBoundsCenter(upperResult.Mesh);
        var closedCenter = upperResult.Pose.Center;
        Require(MathF.Abs(rawCenter.Z) < 0.3f,
            $"upper raw bound center Z moved away from bind geometry ({rawCenter.Z:0.###})");
        Require(closedCenter.Z is > -1.6f and < -0.4f,
            $"upper authored close pose bound center Z was {closedCenter.Z:0.###}, expected around -1");

        VerifyOriginalEndpoints("upper", upperResult);
        VerifyOriginalEndpoints("lower/model07", lowerResult);
        VerifyOriginalPickBeforeFloor(upper, upperResult.Pose, upperResult.Mesh);
    }

    private static OriginalPose LoadOriginalPose(
        ActorInstance instance,
        TemplateStore templates,
        AssetResolver resolver,
        ActorSpawner spawner)
    {
        Require(templates.TryGet(instance.TemplateName, out var template),
            $"template {instance.TemplateName} was not found");
        var openSuffix = AuthoredTransitionSuffix(templates, template!, "chore_open");
        var closeSuffix = AuthoredTransitionSuffix(templates, template!, "chore_close");
        var open = spawner.LoadTransitionClip(template!, instance, "chore_open");
        var close = spawner.LoadTransitionClip(template!, instance, "chore_close");
        Require(open is not null && close is not null,
            $"{instance.TemplateName} stanceless transition clips did not resolve");

        var modelName = (templates.GetAttribute(template!, "aspect", "model") ?? "")
            .Trim().Trim('"');
        byte[]? meshBytes = null;
        var modelResolved = modelName.Length > 0 && resolver.TryLoadModel(modelName, out meshBytes);
        Require(modelResolved,
            $"{instance.TemplateName} model '{modelName}' did not resolve");
        var mesh = AspMesh.Load(meshBytes!);
        return new OriginalPose(mesh, new UsableTransitionPose(mesh, open!, close!),
            open!, close!, openSuffix, closeSuffix);
    }

    private static void VerifyOriginalEndpoints(string name, OriginalPose original)
    {
        var pose = original.Pose;
        Require(AllFinite(pose.Positions) && IsFinite(pose.Min) && IsFinite(pose.Max),
            $"{name} close endpoint produced non-finite geometry");
        var closed = pose.Positions.ToArray();
        pose.SetOpen(true, 1d, snap: true);
        Require(AllFinite(pose.Positions) && IsFinite(pose.Min) && IsFinite(pose.Max),
            $"{name} open endpoint produced non-finite geometry");
        var open = pose.Positions.ToArray();
        Require(!VectorsEqual(closed, open), $"{name} open and close endpoints were identical");
        pose.SetOpen(false, 2d, snap: true);
        Require(VectorsEqual(closed, pose.Positions),
            $"{name} close endpoint did not restore after open pose");

        var reloaded = new UsableTransitionPose(original.Mesh,
            original.OpenClip, original.CloseClip);
        Require(VectorsEqual(closed, reloaded.Positions),
            $"{name} newly loaded pose did not reproduce the close endpoint");
        reloaded.SetOpen(true, 0d, snap: true);
        Require(VectorsEqual(open, reloaded.Positions),
            $"{name} newly loaded pose did not reproduce the open endpoint");
    }

    private static void VerifyOriginalPickBeforeFloor(
        ActorInstance instance, UsableTransitionPose pose, AspMesh mesh)
    {
        pose.SetOpen(false, 0d, snap: true);
        var world = Matrix4x4.CreateFromQuaternion(instance.Placement.Orientation)
            * Matrix4x4.CreateTranslation(instance.Placement.LocalPosition);
        for (var i = 0; i < mesh.TriangleIndices.Length; i += 3)
        {
            var a = Vector3.Transform(pose.Positions[mesh.TriangleIndices[i]], world);
            var b = Vector3.Transform(pose.Positions[mesh.TriangleIndices[i + 1]], world);
            var c = Vector3.Transform(pose.Positions[mesh.TriangleIndices[i + 2]], world);
            var cross = Vector3.Cross(b - a, c - a);
            if (!IsFinite(cross) || cross.LengthSquared() < 1e-8f) continue;
            var direction = Vector3.Normalize(cross);
            var surface = (a + b + c) / 3f;
            var origin = surface - direction * 3f;
            Require(pose.TryRayHit(origin, direction, world, 4f, out var hitDistance),
                "upper lever's posed model could not be clicked");
            Require(hitDistance < 3.5f,
                "upper lever's posed model did not win before the clicked floor");
            return;
        }
        throw new InvalidDataException("upper lever has no finite non-degenerate posed triangle");
    }

    private static string AuthoredTransitionSuffix(
        TemplateStore templates, Template template, string chore)
    {
        var section = templates.GetSection(template, "body", "chore_dictionary", chore);
        var animFiles = section is null ? null : TemplateStore.FindChild(section, "anim_files");
        var value = animFiles?.Attributes.FirstOrDefault().Value?.Trim().Trim('"')
            ?? section?.Attributes.FirstOrDefault(a => a.Name.StartsWith(
                "anim_files:", StringComparison.OrdinalIgnoreCase)).Value?.Trim().Trim('"');
        Require(!string.IsNullOrWhiteSpace(value),
            $"{template.Name}.{chore} has no authored anim_files entry");
        return value!;
    }

    private static AspMesh CreateSyntheticMesh(float weight = 1f)
    {
        var corners = new[]
        {
            new AspMesh.Corner(0, Vector3.UnitZ, 0xFFFFFFFF, Vector2.Zero),
            new AspMesh.Corner(1, Vector3.UnitZ, 0xFFFFFFFF, Vector2.UnitX),
            new AspMesh.Corner(2, Vector3.UnitZ, 0xFFFFFFFF, Vector2.UnitY),
        };
        return new AspMesh
        {
            MeshName = "lever-pose-selftest",
            BoneNames = new[] { "Bone01" },
            BoneParents = new[] { -1 },
            BindPose = new[] { new AspMesh.Transform(Quaternion.Identity, Vector3.Zero) },
            BindPoseAlt = new[] { new AspMesh.Transform(Quaternion.Identity, Vector3.Zero) },
            InverseBindMatrices = new[] { Matrix4x4.Identity },
            Positions = new[]
            {
                new Vector3(-1f, -1f, 0f),
                new Vector3(1f, -1f, 0f),
                new Vector3(0f, 1f, 0f),
            },
            Corners = corners,
            TriangleIndices = new[] { 0, 1, 2 },
            SkinWeights = Enumerable.Repeat(new Vector4(weight, 0f, 0f, 0f), corners.Length).ToArray(),
            SkinBones = new uint[corners.Length],
        };
    }

    private static PrsAnimation CreateTranslationClip(Vector3 start, Vector3 end)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(0x4D494E41u); // ANIM
            writer.Write(3u);
            writer.Write(8u);          // padded "Bone01\0"
            writer.Write(1u);
            writer.Write(1f);
            WriteVector(writer, Vector3.Zero);
            WriteQuaternion(writer, Quaternion.Identity);
            WriteQuaternion(writer, Quaternion.Identity);
            writer.Write(0f);
            writer.Write(Encoding.ASCII.GetBytes("Bone01"));
            writer.Write((byte)0);
            writer.Write((byte)0);

            writer.Write(0x54534C4Bu); // KLST
            writer.Write(3u);
            writer.Write(0u);          // bone index
            writer.Write(0u);          // name offset
            writer.Write(1u);          // rotation keys
            writer.Write(2u);          // position keys
            writer.Write(0f);
            WriteQuaternion(writer, Quaternion.Identity);
            writer.Write(0f);
            WriteVector(writer, start);
            writer.Write(1f);
            WriteVector(writer, end);
            writer.Write(0x444E4541u); // AEND
        }
        return PrsAnimation.Load(stream.ToArray());
    }

    private static void WriteVector(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static void WriteQuaternion(BinaryWriter writer, Quaternion value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
        writer.Write(value.W);
    }

    private static Vector3 CornerBoundsCenter(AspMesh mesh)
    {
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        foreach (var corner in mesh.Corners)
        {
            var position = mesh.Positions[corner.VertexIndex];
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }
        return min + (max - min) * 0.5f;
    }

    private static string ResolveInstallRoot(string installRoot)
    {
        if (Directory.Exists(installRoot)) return installRoot;
        var fullPath = Path.GetFullPath(installRoot);
        if (File.Exists(fullPath)
            && Path.GetFileName(fullPath).Equals("MpWorld.dsmap", StringComparison.OrdinalIgnoreCase))
            return Directory.GetParent(Path.GetDirectoryName(fullPath)!)?.FullName
                ?? throw new DirectoryNotFoundException("Could not resolve install root from MpWorld.dsmap.");
        return installRoot;
    }

    private static bool VectorsEqual(IReadOnlyList<Vector3> a, IReadOnlyList<Vector3> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (Vector3.DistanceSquared(a[i], b[i]) > 1e-8f) return false;
        return true;
    }

    private static bool AllFinite(IEnumerable<Vector3> values) => values.All(IsFinite);

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool Near(float actual, float expected, float epsilon = 0.0001f)
        => MathF.Abs(actual - expected) <= epsilon;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private static void RunCase(List<string> failures, string name, Action action)
    {
        try { action(); }
        catch (Exception ex) { failures.Add($"{name}: {ex.Message}"); }
    }

    private sealed record OriginalPose(
        AspMesh Mesh,
        UsableTransitionPose Pose,
        PrsAnimation OpenClip,
        PrsAnimation CloseClip,
        string OpenSuffix,
        string CloseSuffix);
}
