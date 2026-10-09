using System.Globalization;
using System.Numerics;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Tank;
using SiegeFX.Runtime.Render;

namespace SiegeFX.Runtime;

internal static class DoorInteractionSelfTest
{
    public static bool Run(string? installRoot = null)
    {
        var failures = new List<string>();
        var leftMin = new Vector3(0f, -0.05f, 0f);
        var leftMax = new Vector3(1.4f, 0.05f, 2.5f);
        var rightMin = new Vector3(-1.4f, -0.05f, 0f);
        var rightMax = new Vector3(0f, 0.05f, 2.5f);
        var world = Matrix4x4.Identity;

        // A click through the visible left leaf must select it, not its
        // mirrored partner or a floor point next to either hinge.
        Check(DoorInteractionGeometry.TryPick(new Vector3(0.7f, -5f, 1.2f),
            new Vector3(0.7f, 5f, 1.2f), world, leftMin, leftMax, out _),
            "visible left leaf did not pick");
        Check(!DoorInteractionGeometry.TryPick(new Vector3(0.7f, -5f, 1.2f),
            new Vector3(0.7f, 5f, 1.2f), world, rightMin, rightMax, out _),
            "adjacent right leaf stole the left-leaf click");
        Check(!DoorInteractionGeometry.TryPick(new Vector3(2.0f, -5f, 1.2f),
            new Vector3(2.0f, 5f, 1.2f), world, leftMin, leftMax, out _),
            "empty space beyond the leaf picked a door");

        // The shipped ornate pair extends to opposite sides of its hinges.
        // Both free edges must end up on the side opposite the player.
        foreach (float side in new[] { -2f, 2f })
        {
            foreach (var (min, max) in new[] { (leftMin, leftMax), (rightMin, rightMax) })
            {
                float sign = DoorInteractionGeometry.SwingSign(world, min, max,
                    new Vector3(0f, side, 0f));
                float edgeX = (min.X + max.X) * 0.5f;
                float edgeY = Vector3.Transform(new Vector3(edgeX, 0f, 0f),
                    Matrix4x4.CreateRotationZ(sign * MathF.PI / 2f)).Y;
                Check(edgeY * side < 0f,
                    $"mirrored leaf swung toward player on local Y={side}");
            }
        }

        if (!string.IsNullOrWhiteSpace(installRoot))
            CheckRealChurchDoors(installRoot, failures);

        if (failures.Count == 0)
        {
            Console.WriteLine("[selftest-door-interaction] PASS — leaf picking and mirrored swings" +
                (installRoot is null ? "" : ", real Elddim pair/use points"));
            return true;
        }
        Console.Error.WriteLine($"[selftest-door-interaction] FAIL ({failures.Count})");
        foreach (var failure in failures) Console.Error.WriteLine("  " + failure);
        return false;

        void Check(bool condition, string message)
        {
            if (!condition) failures.Add(message);
        }
    }

    private static void CheckRealChurchDoors(string installRoot, List<string> failures)
    {
        var path = Directory.Exists(installRoot)
            ? Path.Combine(installRoot, "Maps", "MpWorld.dsmap") : installRoot;
        if (!File.Exists(path)) { failures.Add($"MpWorld.dsmap missing: {path}"); return; }
        using var tank = TankFile.Open(path);
        var reader = new TankReader(tank);
        const string region = "/world/maps/multiplayer_world/regions/town_center";
        var (placements, _) = RegionObjects.LoadPlacements(reader, region,
            "interactive.gas", multiplayerContent: true);
        var right = placements.FirstOrDefault(p => p.Scid == 0x03200BF8);
        var left = placements.FirstOrDefault(p => p.Scid == 0x03200BF9);
        if (right is null || left is null)
        { failures.Add("Elddim ornate double-door placement missing"); return; }
        foreach (var (door, partner) in new[] { (right, left), (left, right) })
        {
            if (!door.TemplateName.StartsWith("door_grs_ornate_", StringComparison.OrdinalIgnoreCase))
                failures.Add($"0x{door.Scid:X8} is not an ornate door");
            var second = ParseScid(TemplateStore.GetNodeAttribute(door.Node,
                "door_basic", "second_door"));
            if (second != partner.Scid)
                failures.Add($"0x{door.Scid:X8} does not link to its other leaf");
            var points = (TemplateStore.GetNodeAttribute(door.Node,
                "placement", "use_point_scids") ?? "").Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParseScid).ToHashSet();
            if (!points.SetEquals(new uint[] { 0x03200BF6, 0x03200BF7 }))
                failures.Add($"0x{door.Scid:X8} lost its two authored use points");
            var range = TemplateStore.GetNodeAttribute(door.Node, "aspect", "use_range");
            if (!float.TryParse(range, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float parsed) || MathF.Abs(parsed - 0.3f) > 1e-4f)
                failures.Add($"0x{door.Scid:X8} lost authored use_range=0.3");
        }
    }

    private static uint ParseScid(string? value)
    {
        var s = value?.Trim().Trim('"') ?? "";
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
            out var scid) ? scid : 0;
    }
}
