namespace SiegeFX.Core.Assets;

/// <summary>
/// Immutable identity and content rules for one authored Dungeon Siege world.
/// A profile describes content; it does not imply that a network session is active.
/// </summary>
public sealed class WorldProfile
{
    public const string KingdomOfEhbId = "KingdomOfEhb";
    public const string UtraeanPeninsulaId = "UtraeanPeninsula";

    public static WorldProfile KingdomOfEhb { get; } = new(
        KingdomOfEhbId,
        "Kingdom of Ehb",
        "World.dsmap",
        "/world/maps/map_world",
        enableMultiplayerAuthoredTriggers: false);

    public static WorldProfile UtraeanPeninsula { get; } = new(
        UtraeanPeninsulaId,
        "Utraean Peninsula",
        "MpWorld.dsmap",
        "/world/maps/multiplayer_world",
        enableMultiplayerAuthoredTriggers: true);

    public static IReadOnlyList<WorldProfile> All { get; } =
        Array.AsReadOnly(new[] { KingdomOfEhb, UtraeanPeninsula });

    public string Id { get; }
    public string DisplayName { get; }
    public string MapFileName { get; }
    public string MapRoot { get; }
    public bool EnableMultiplayerAuthoredTriggers { get; }

    private WorldProfile(
        string id,
        string displayName,
        string mapFileName,
        string mapRoot,
        bool enableMultiplayerAuthoredTriggers)
    {
        Id = id;
        DisplayName = displayName;
        MapFileName = mapFileName;
        MapRoot = NormalizePath(mapRoot).TrimEnd('/');
        EnableMultiplayerAuthoredTriggers = enableMultiplayerAuthoredTriggers;
    }

    public static WorldProfile Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        foreach (var profile in All)
            if (string.Equals(profile.Id, id, StringComparison.Ordinal))
                return profile;
        throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown world profile id.");
    }

    public static WorldProfile? TryFromRegion(string? region)
    {
        if (string.IsNullOrWhiteSpace(region)) return null;
        foreach (var profile in All)
            if (profile.ContainsRegion(region))
                return profile;
        return null;
    }

    public bool ContainsRegion(string? region)
    {
        if (string.IsNullOrWhiteSpace(region)) return false;
        var normalized = NormalizePath(region).TrimEnd('/');
        var prefix = MapRoot + "/regions/";
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        // Authored region identifiers are one direct child of the map's
        // regions directory. Keeping this structural check here prevents a
        // save from escaping its selected world through dot segments or from
        // smuggling another path beneath a valid-looking region prefix.
        var regionName = normalized[prefix.Length..];
        return regionName.Length > 0
            && regionName is not "." and not ".."
            && !regionName.Contains('/');
    }

    public string ResolveMapPath(string mapsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapsDirectory);
        return Path.Combine(mapsDirectory, MapFileName);
    }

    private static string NormalizePath(string path)
    {
        var normalized = path.Trim().Replace('\\', '/');
        if (!normalized.StartsWith('/')) normalized = "/" + normalized;
        while (normalized.Contains("//", StringComparison.Ordinal))
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        return normalized;
    }
}
