using SiegeFX.Core.Save;

namespace SiegeFX.Runtime;

/// <summary>
/// Synthetic regression coverage for SaveStore's on-disk transaction. Every
/// case uses its own slot path so an early failure cannot hide later results.
/// No Dungeon Siege data is required.
/// </summary>
internal static class SaveTransactionSelfTest
{
    public static bool Run()
    {
        var failures = new List<string>();
        var root = Path.Combine(
            Path.GetTempPath(),
            $"siegefx_selftest_save_transaction_{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(root);

            RunCase(failures, "successful replacement", () =>
            {
                var caseDir = CaseDirectory(root, "replacement");
                var path = Path.Combine(caseDir, "replacement.save");
                var backupPath = path + ".bak";

                SaveStore.Save(path, MakeSave("previous"));
                var previousBytes = File.ReadAllBytes(path);
                SaveStore.Save(path, MakeSave("current"));

                Check(failures, File.Exists(backupPath),
                    "successful replacement: previous-generation backup was not created");
                if (File.Exists(backupPath))
                {
                    CheckBytes(failures, "successful replacement: backup bytes",
                        previousBytes, File.ReadAllBytes(backupPath));
                    CheckMarker(failures, "successful replacement: backup",
                        SaveStore.Load(backupPath), "previous");
                }
                CheckMarker(failures, "successful replacement: active",
                    SaveStore.Load(path), "current");

                var currentBytes = File.ReadAllBytes(path);
                SaveStore.Save(path, MakeSave("newest"));
                CheckBytes(failures, "successful replacement: rotated backup bytes",
                    currentBytes, File.ReadAllBytes(backupPath));
                CheckMarker(failures, "successful replacement: rotated backup",
                    SaveStore.Load(backupPath), "current");
                CheckMarker(failures, "successful replacement: newest active",
                    SaveStore.Load(path), "newest");
            });

            RunCase(failures, "invalid staged schema", () =>
            {
                var caseDir = CaseDirectory(root, "invalid-schema");
                var path = Path.Combine(caseDir, "invalid-schema.save");
                SaveStore.Save(path, MakeSave("valid-before-invalid"));
                var before = File.ReadAllBytes(path);
                var invalid = MakeSave("invalid");
                invalid.SchemaVersion = SaveFile.CurrentSchemaVersion + 1;

                Exception? error = null;
                try { SaveStore.Save(path, invalid); }
                catch (Exception ex) { error = ex; }

                Check(failures, error is InvalidDataException,
                    "invalid staged schema: Save should throw InvalidDataException " +
                    $"(got {error?.GetType().Name ?? "no exception"})");
                CheckBytes(failures, "invalid staged schema: active bytes",
                    before, File.ReadAllBytes(path));
                CheckMarker(failures, "invalid staged schema: active",
                    SaveStore.Load(path), "valid-before-invalid");
            });

            RunCase(failures, "future current schema", () =>
            {
                var caseDir = CaseDirectory(root, "future-current");
                var path = Path.Combine(caseDir, "future.save");
                SaveStore.Save(path, MakeSave("backup"));
                SaveStore.Save(path, MakeSave("newer-engine"));
                var futureJson = File.ReadAllText(path).Replace(
                    $"\"SchemaVersion\": {SaveFile.CurrentSchemaVersion}",
                    $"\"SchemaVersion\": {SaveFile.CurrentSchemaVersion + 1}");
                File.WriteAllText(path, futureJson);
                var before = File.ReadAllBytes(path);
                var backupBefore = File.ReadAllBytes(path + ".bak");
                bool rejected = false;
                try { SaveStore.Save(path, MakeSave("older-engine")); }
                catch (InvalidDataException) { rejected = true; }
                Check(failures, rejected, "future current schema: overwrite was not refused");
                CheckBytes(failures, "future current schema: active", before, File.ReadAllBytes(path));
                CheckBytes(failures, "future current schema: backup", backupBefore,
                    File.ReadAllBytes(path + ".bak"));

                // A future format may change a known field's JSON type before
                // typed deserialization can reach the schema-version check.
                File.WriteAllText(path,
                    $"{{\"SchemaVersion\":{SaveFile.CurrentSchemaVersion + 1},\"Actors\":{{}}}}");
                before = File.ReadAllBytes(path);
                rejected = false;
                try { SaveStore.Save(path, MakeSave("older-engine")); }
                catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
                { rejected = true; }
                Check(failures, rejected, "future changed shape: overwrite was not refused");
                CheckBytes(failures, "future changed shape: active", before, File.ReadAllBytes(path));
                CheckBytes(failures, "future changed shape: backup", backupBefore,
                    File.ReadAllBytes(path + ".bak"));
            });

            RunCase(failures, "failed promotion", () =>
            {
                var caseDir = CaseDirectory(root, "failed-promotion");
                var path = Path.Combine(caseDir, "locked-target.save");
                SaveStore.Save(path, MakeSave("valid-before-lock"));
                var before = File.ReadAllBytes(path);

                Exception? error = null;
                // Existing content remains readable for validation, but the missing
                // FileShare.Delete permission forces the promotion itself to fail.
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    try { SaveStore.Save(path, MakeSave("must-not-commit")); }
                    catch (Exception ex) { error = ex; }
                }

                Check(failures, error is IOException or UnauthorizedAccessException,
                    "failed promotion: locked target should reject promotion " +
                    $"(got {error?.GetType().Name ?? "no exception"})");
                CheckBytes(failures, "failed promotion: active bytes",
                    before, File.ReadAllBytes(path));
                CheckMarker(failures, "failed promotion: active",
                    SaveStore.Load(path), "valid-before-lock");

                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    Path.GetFileName(path),
                    Path.GetFileName(path + ".bak"),
                };
                var leftovers = Directory.EnumerateFiles(caseDir)
                    .Select(Path.GetFileName)
                    .Where(name => name is not null && !allowed.Contains(name))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                Check(failures, leftovers.Length == 0,
                    "failed promotion: staging file(s) left behind: " +
                    string.Join(", ", leftovers));
            });

            RunCase(failures, "corrupt current generation", () =>
            {
                var caseDir = CaseDirectory(root, "corrupt-current");
                var path = Path.Combine(caseDir, "corrupt-current.save");
                var seedPath = Path.Combine(caseDir, "corrupt-backup-seed.save");
                var backupPath = path + ".bak";

                SaveStore.Save(seedPath, MakeSave("known-good-backup"));
                File.Copy(seedPath, backupPath);
                var backupBefore = File.ReadAllBytes(backupPath);
                File.WriteAllText(path, "{ this is not valid save JSON");

                SaveStore.Save(path, MakeSave("replacement-after-corruption"));

                CheckBytes(failures, "corrupt current generation: backup bytes",
                    backupBefore, File.ReadAllBytes(backupPath));
                CheckMarker(failures, "corrupt current generation: backup",
                    SaveStore.Load(backupPath), "known-good-backup");
                CheckMarker(failures, "corrupt current generation: active",
                    SaveStore.Load(path), "replacement-after-corruption");
            });

            RunCase(failures, "manual/autosave isolation", () =>
            {
                var caseDir = CaseDirectory(root, "slot-isolation");
                var manualPath = Path.Combine(caseDir, "manual.save");
                var manualBackupPath = manualPath + ".bak";
                var autosavePath = Path.Combine(caseDir, "autosave.save");

                SaveStore.Save(manualPath, MakeSave("manual"));
                File.Copy(manualPath, manualBackupPath);
                var manualBefore = File.ReadAllBytes(manualPath);
                var manualBackupBefore = File.ReadAllBytes(manualBackupPath);

                SaveStore.Save(autosavePath, MakeSave("autosave-one"));
                SaveStore.Save(autosavePath, MakeSave("autosave-two"));

                CheckBytes(failures, "manual/autosave isolation: manual active bytes",
                    manualBefore, File.ReadAllBytes(manualPath));
                CheckBytes(failures, "manual/autosave isolation: manual backup bytes",
                    manualBackupBefore, File.ReadAllBytes(manualBackupPath));
                CheckMarker(failures, "manual/autosave isolation: manual active",
                    SaveStore.Load(manualPath), "manual");
                CheckMarker(failures, "manual/autosave isolation: autosave active",
                    SaveStore.Load(autosavePath), "autosave-two");
            });
        }
        catch (Exception ex)
        {
            failures.Add($"self-test setup: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch (Exception ex)
            {
                failures.Add($"temporary-directory cleanup: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (failures.Count == 0)
        {
            Console.WriteLine("[selftest-save-transaction] OK — staged validation, backup, " +
                              "failed-promotion cleanup, and slot isolation passed");
            return true;
        }

        Console.Error.WriteLine($"[selftest-save-transaction] FAIL ({failures.Count}):");
        foreach (var failure in failures) Console.Error.WriteLine("  " + failure);
        return false;
    }

    private static SaveFile MakeSave(string marker) => new()
    {
        SchemaVersion = SaveFile.CurrentSchemaVersion,
        WorldId = "KingdomOfEhb",
        SaveSetId = "43ebdaa9-2d6b-43b9-a73e-7a573cd4e147",
        AdventureMode = "OriginalCampaign",
        EngineVersion = "selftest",
        SavedAt = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc),
        DisplayName = marker,
        RegionPath = "/world/maps/map_world/regions/selftest",
        PlayerRegion = "/world/maps/map_world/regions/selftest",
        Player = new PlayerSnapshot { HeroName = marker },
    };

    private static string CaseDirectory(string root, string name)
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void RunCase(List<string> failures, string name, Action test)
    {
        try { test(); }
        catch (Exception ex)
        {
            failures.Add($"{name}: threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void CheckMarker(
        List<string> failures,
        string subject,
        SaveFile actual,
        string expected)
    {
        Check(failures, actual.DisplayName == expected,
            $"{subject}: expected marker '{expected}', got '{actual.DisplayName}'");
        Check(failures, actual.RegionPath == "/world/maps/map_world/regions/selftest",
            $"{subject}: region was not the synthetic Ehb self-test region");
    }

    private static void CheckBytes(
        List<string> failures,
        string subject,
        byte[] expected,
        byte[] actual)
    {
        Check(failures, expected.AsSpan().SequenceEqual(actual),
            $"{subject}: file contents changed");
    }

    private static void Check(List<string> failures, bool condition, string message)
    {
        if (!condition) failures.Add(message);
    }
}
