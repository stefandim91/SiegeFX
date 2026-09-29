using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Save;

/// <summary>
/// Phase 19a — JSON read/write for <see cref="SaveFile"/>. Atomic write
/// (temp file + replace) so a crashed save doesn't shred the previous
/// one. Pretty-printed because the diff value during dev outweighs the
/// few extra bytes; we can flip to compact later if file size matters.
/// </summary>
public static class SaveStore
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        IncludeFields = false,
    };

    /// <summary>SC-MP-EOS P8 — serialize a SaveFile to bytes for the MP
    /// late-join world snapshot (same JSON shape as an on-disk save; the
    /// host builds it, the joiner deserializes + applies).</summary>
    public static byte[] Serialize(SaveFile data) =>
        System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, Json));

    /// <summary>SC-MP-EOS P8 — inverse of <see cref="Serialize"/>. Returns
    /// null on malformed input (a hostile/garbled snapshot never throws
    /// into the join path).</summary>
    public static SaveFile? Deserialize(byte[] bytes)
    {
        try
        {
            var file = JsonSerializer.Deserialize<SaveFile>(Encoding.UTF8.GetString(bytes), Json);
            if (file is null) return null;
            return ValidateAndMigrate(file, "network:" + Convert.ToHexString(SHA256.HashData(bytes)));
        }
        catch { return null; }
    }

    public static void Save(string path, SaveFile data)
    {
        ArgumentNullException.ThrowIfNull(data);
        path = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // A unique sibling keeps staging on the same volume and prevents one
        // writer from consuming another writer's incomplete temporary file.
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(tmp, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, data, Json);
                stream.Flush(flushToDisk: true);
            }

            // Use the loader's JSON/schema checks before touching the active
            // slot. This does not yet validate gameplay cross-references.
            _ = Load(tmp);
            if (File.Exists(path))
            {
                // Never replace a known-good backup with malformed current
                // data. IO failures (including locks) must abort the save;
                // inability to read a file is not evidence of corruption.
                bool preserveCurrent = true;
                try
                {
                    using var current = File.OpenRead(path);
                    using var document = JsonDocument.Parse(current);
                }
                catch (JsonException) { preserveCurrent = false; }
                // Unsupported versions may belong to a newer engine. Refuse
                // to overwrite them rather than treating them as corrupt.
                // A newer shape can also throw JsonException during typed
                // deserialization, even though its JSON syntax is valid.
                if (preserveCurrent) _ = Load(path);
                File.Replace(tmp, path, preserveCurrent ? path + ".bak" : null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }
        finally
        {
            // After promotion the staging path is already gone. On failure,
            // cleanup must not mask the actual serialization/promotion error.
            try { File.Delete(tmp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static SaveFile Load(string path)
    {
        var json = File.ReadAllText(path);
        var file = JsonSerializer.Deserialize<SaveFile>(json, Json)
                   ?? throw new InvalidDataException($"save '{path}' deserialized to null");
        // Forward migrations.
        //   v1 -> v2 : added PlayerSnapshot.Quests (empty list default).
        //   v2 -> v3 : added QuestSnapshot.KillProgress (default 0).
        //   v3 -> v4 : added PlayerSnapshot.Gold (default 0).
        //   v4 -> v5 : added PlayerSnapshot.HeroName (empty string) +
        //              PlayerSnapshot.Variant (null = stock farmboy).
        //   v5 -> v6 : added SpellbookSnapshot.Placed (empty list default).
        //   v6 -> v7 : added QuestSnapshot.TalkProgress (default 0) for
        //              SC-QUEST-OBJ-A.
        //   v7 -> v8 : added PlayerSnapshot.ConsumedInventoryScids (default
        //              empty list) for SC-WORLD-INVENTORY-CONSUMED.
        //   v8 -> v9 : added QuestSnapshot.PickupProgress (default 0) for
        //              SC-QUEST-OBJ-C.
        // All deserializer-friendly — missing fields just hit their defaults —
        // so the work here is only the version-stamp bump. Pre-v1 shapes still
        // get rejected below. Forgetting to extend this whitelist when bumping
        // CurrentSchemaVersion silently breaks every prior save with an
        // InvalidDataException — caught by the SC-SCROLL-G review pass.
        //   v9 -> v10: added SaveFile.World (ALPHA-2G world state) +
        //              QuestSnapshot.DialogueLog — both default-friendly.
        //   v10 -> v11: added SaveFile.DisplayName + NextTipIndex/TipsDisabled
        //              (handbook progress) — all default-friendly.
        //   v11 -> v12: added SaveFile.HeroName/MapName/ElapsedSeconds/Thumbnail
        //              (Load Game window preview) — all default-friendly.
        //   v12 -> v13: added SaveFile.Party (SC-PARTY-PERSIST companion
        //              roster + bags) — default-friendly empty list.
        //   v13 -> v14: explicit world/save-set/adventure-mode metadata.
        //              Legacy world identity is accepted only when a known
        //              authored map root is present in RegionPath.
        return ValidateAndMigrate(file, Path.GetFullPath(path));
    }

    private static SaveFile ValidateAndMigrate(SaveFile file, string sourceIdentity)
    {
        if (file.SchemaVersion is >= 1 and <= 13)
        {
            var legacyProfile = WorldProfile.TryFromRegion(file.RegionPath)
                ?? throw new InvalidDataException(
                    $"legacy save '{sourceIdentity}' has an unknown world region '{file.RegionPath}'");
            if (!string.IsNullOrWhiteSpace(file.PlayerRegion)
                && !legacyProfile.ContainsRegion(file.PlayerRegion))
                throw new InvalidDataException(
                    $"legacy save '{sourceIdentity}' player region '{file.PlayerRegion}' " +
                    $"does not belong to {legacyProfile.Id}");

            file.WorldId = legacyProfile.Id;
            file.SaveSetId = DeterministicGuid(sourceIdentity).ToString("D");
            file.AdventureMode = legacyProfile == WorldProfile.KingdomOfEhb
                ? "OriginalCampaign"
                : "SoloAdventure";
            file.SchemaVersion = SaveFile.CurrentSchemaVersion;
        }

        if (file.SchemaVersion != SaveFile.CurrentSchemaVersion)
            throw new InvalidDataException(
                $"save '{sourceIdentity}' schema v{file.SchemaVersion} != runtime v{SaveFile.CurrentSchemaVersion}");

        WorldProfile profile;
        try { profile = WorldProfile.Get(file.WorldId); }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException(
                $"save '{sourceIdentity}' has unknown world id '{file.WorldId}'", ex);
        }

        if (!Guid.TryParse(file.SaveSetId, out var saveSetId))
            throw new InvalidDataException(
                $"save '{sourceIdentity}' has invalid save-set id '{file.SaveSetId}'");
        file.SaveSetId = saveSetId.ToString("D");

        var expectedMode = profile == WorldProfile.KingdomOfEhb
            ? "OriginalCampaign"
            : "SoloAdventure";
        if (!string.Equals(file.AdventureMode, expectedMode, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"save '{sourceIdentity}' mode '{file.AdventureMode}' is invalid for {profile.Id}");

        ValidateRegion(sourceIdentity, profile, nameof(file.RegionPath), file.RegionPath, required: true);
        ValidateRegion(sourceIdentity, profile, nameof(file.PlayerRegion), file.PlayerRegion, required: false);
        return file;
    }

    private static void ValidateRegion(
        string sourceIdentity,
        WorldProfile profile,
        string field,
        string? region,
        bool required)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            if (required)
                throw new InvalidDataException(
                    $"save '{sourceIdentity}' is missing required {field}");
            return;
        }
        if (!profile.ContainsRegion(region))
            throw new InvalidDataException(
                $"save '{sourceIdentity}' {field} '{region}' does not belong to {profile.Id}");
    }

    private static Guid DeterministicGuid(string identity)
    {
        var normalized = identity.Trim().Replace('\\', '/').ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>Default per-user save directory. Uses LocalApplicationData
    /// so saves survive uninstalls of the dev build but stay out of the
    /// roaming profile (the data isn't worth syncing). Same scheme on
    /// Linux/Mac via the .NET cross-platform special-folder mapping.</summary>
    public static string DefaultSaveDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SiegeFX", "Saves");

    /// <summary>Quicksave path under <see cref="DefaultSaveDirectory"/>.
    /// One slot for now; the F5/F9 wiring in 19c overwrites it on each
    /// save and reads it on each load.</summary>
    public static string QuicksavePath(string? saveSetId = null)
        => Path.Combine(DefaultSaveDirectory(), SlotFileName("quicksave", saveSetId));

    /// <summary>Autosave path under <see cref="DefaultSaveDirectory"/>. Passing
    /// a save-set id isolates automatic slots belonging to different adventures.</summary>
    public static string AutoSavePath(string? saveSetId = null)
        => Path.Combine(DefaultSaveDirectory(), SlotFileName("autosave", saveSetId));

    private static string SlotFileName(string kind, string? saveSetId)
    {
        if (saveSetId is null) return kind + ".save";
        if (!Guid.TryParse(saveSetId, out var id))
            throw new ArgumentException("Save-set id must be a valid Guid.", nameof(saveSetId));
        return $"{kind}-{id:D}.save";
    }

    /// <summary>One row in the Save/Load Game window's list. <see cref="Path"/>
    /// is the file to load/delete; <see cref="DisplayName"/> is the player-typed
    /// label (or file stem for pre-v11 / quicksaves). The v12 preview fields
    /// (<see cref="HeroName"/>, <see cref="MapName"/>, <see cref="ElapsedSeconds"/>,
    /// <see cref="Thumbnail"/>) feed the Load window's HERO/MAP/ELAPSED info box
    /// and screenshot; they're empty/null on older saves.</summary>
    public readonly record struct SaveSlot(
        string Path, string DisplayName, DateTime SavedAt, string RegionPath, bool IsQuicksave,
        string HeroName, string MapName, double ElapsedSeconds, byte[]? Thumbnail);

    // Lightweight header — System.Text.Json ignores unknown members by default,
    // so this reads the metadata fields without materializing the (potentially
    // large) actor/loot/world payload of every file in the list. No migration
    // runs here; we only read fields, which are all present-or-defaulted.
    private sealed class SaveHeader
    {
        public int SchemaVersion { get; set; }
        public DateTime SavedAt { get; set; }
        public string DisplayName { get; set; } = "";
        public string RegionPath { get; set; } = "";
        public string HeroName { get; set; } = "";
        public string MapName { get; set; } = "";
        public double ElapsedSeconds { get; set; }
        public byte[]? Thumbnail { get; set; }
    }

    /// <summary>Turn a player-typed save label into a safe, unique on-disk
    /// path. Multiple saves may share a display name ("woot" ×3 in DS1's own
    /// window), so the stem carries a timestamp; a counter suffix guarantees
    /// uniqueness if two saves land in the same second.</summary>
    public static string NamedSavePath(string displayName, DateTime now)
    {
        var stem = SanitizeStem(displayName);
        if (stem.Length == 0) stem = "save";
        var dir = DefaultSaveDirectory();
        var baseName = $"{stem}-{now:yyyyMMdd_HHmmss}";
        var path = Path.Combine(dir, baseName + ".save");
        int n = 1;
        while (File.Exists(path))
            path = Path.Combine(dir, $"{baseName}_{n++}.save");
        return path;
    }

    /// <summary>Sanitize a display name into a filename stem: strip anything
    /// outside [A-Za-z0-9-_ ], collapse whitespace to underscores, cap length.
    /// Purely cosmetic — the real label rides in <see cref="SaveFile.DisplayName"/>
    /// so the stem never needs to round-trip back to the shown text.</summary>
    private static string SanitizeStem(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name.Trim())
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_') sb.Append(c);
            else if (char.IsWhiteSpace(c)) sb.Append('_');
            // else drop
            if (sb.Length >= 40) break;
        }
        return sb.ToString();
    }

    /// <summary>Enumerate every save in <see cref="DefaultSaveDirectory"/>,
    /// newest first. Unreadable / partially-written files are skipped rather
    /// than throwing so one corrupt slot can't blank the whole list. The
    /// quicksave is included and flagged.</summary>
    public static IReadOnlyList<SaveSlot> ListSaves()
    {
        var dir = DefaultSaveDirectory();
        if (!Directory.Exists(dir)) return System.Array.Empty<SaveSlot>();
        var slots = new List<SaveSlot>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.save"))
        {
            SaveHeader? h = null;
            try { h = JsonSerializer.Deserialize<SaveHeader>(File.ReadAllText(path), Json); }
            catch { /* skip unreadable */ }
            if (h is null) continue;
            bool isQuick = IsSaveSetSlot(Path.GetFileName(path), "quicksave");
            var label = !string.IsNullOrWhiteSpace(h.DisplayName)
                ? h.DisplayName
                : isQuick ? "Quicksave" : Path.GetFileNameWithoutExtension(path);
            slots.Add(new SaveSlot(path, label, h.SavedAt, h.RegionPath, isQuick,
                                   h.HeroName, h.MapName, h.ElapsedSeconds, h.Thumbnail));
        }
        slots.Sort((a, b) => b.SavedAt.CompareTo(a.SavedAt));
        return slots;
    }

    private static bool IsSaveSetSlot(string fileName, string kind)
    {
        if (string.Equals(fileName, kind + ".save", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!fileName.StartsWith(kind + "-", StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(".save", StringComparison.OrdinalIgnoreCase))
            return false;
        var idText = fileName[(kind.Length + 1)..^5];
        return Guid.TryParseExact(idText, "D", out _);
    }

    /// <summary>Delete a save file. Swallows a missing-file race; any other IO
    /// error propagates so the caller can surface it.</summary>
    public static void Delete(string path)
    {
        try { File.Delete(path); }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }
}
