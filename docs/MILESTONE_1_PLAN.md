# Milestone 1: audited implementation plan

## Baseline and scope

Audit date: 2026-09-21. Upstream: `codingncaffeine/SiegeFX`.
Exact engine baseline: **`7a9cd7435b3baed56d597243def820581ab6f9eb`**
(`Relicense SiegeFX under GPL-3.0-only`). Verified against `git ls-remote upstream HEAD`.
Fork governance commit: `a3322e22c9155a1750e1be83115754e06d6617d6`.
The original checkout lacked the required documents; it was fast-forwarded to that
already-fetched governance commit before implementation. Working branch:
`codex/utraea-milestone-1`.

Only offline Utraea launch, content-rule separation, and save/reload foundations
are in scope. No Party Adventure, guidance, remaster, or puzzle-specific fixes.

## Findings verified in this checkout

| Brief assumption | Evidence / correction |
| --- | --- |
| Ehb-centric launch | `src/SiegeFX.Runtime/Render/RenderHost.cs`: `FlushDifficultyMenu` calls `LaunchRegionViaRelaunch`, which hard-codes `World.dsmap` and `map_world/regions/fh_r1`. |
| Ehb-centric reload | Same file: `LaunchRegionForLoad` always supplies `World.dsmap`; `LaunchRegionForLoadWithTanks` takes only the region from the save. `PerformLoad` also has a same-region shortcut that must check world identity. |
| No durable save world | `src/SiegeFX.Core/Save/SaveFile.cs`: schema 13 persists `RegionPath` / `PlayerRegion`, but no `WorldId`. `RenderHost.CaptureSave` cannot persist a selected profile yet. |
| Save transaction missing | Partly outdated: `SaveStore.Save` already stages a sibling `.tmp` and uses `File.Replace` / `File.Move`. It lacks flush-to-disk, staged validation, backup retention, and failure tests. |
| Authored starts need new parser | Existing `src/SiegeFX.Core/Assets/StartPositionsStore.cs` has `Load` / `FindDefault`; `RenderHost.LoadPlayActors` and `TrySpawnPlayerWithPicker` use the start node's layout transform. Reuse this. Bootstrap region still needs verification. |
| Multiplayer content tied to network | `src/SiegeFX.Core/Actors/TriggerRuntime.cs`: `EvaluateInstance` skips `!row.SinglePlayer` unless `IsMultiplayerSession`. `RenderHost.MpInRegionInit` / `MpTearDownSession` own that flag. |
| Save slot isolation | `SaveStore.NamedSavePath` creates unique manual paths. Quicksave and authored autosave use distinct but globally shared filenames. Adventure-level isolation is missing. |
| Tests | `src/SiegeFX.Runtime/SaveSelfTest.cs`, `Program.cs --selftest-save`, and `test-all.bat` provide synthetic save coverage; no offline-Utraea acceptance test exists. |

## Implementation sequence

1. **Establish baseline build and save safety.** Build the unchanged solution;
   run `--selftest-save`. Extend `SaveStore.Save` with unique sibling staging,
   durable flush, validation before promotion, and retention of a previous valid
   generation. Add `SaveTransactionSelfTest.Run` in
   `src/SiegeFX.Runtime/SaveTransactionSelfTest.cs`, dispatch through `Program.cs`,
   and register it in `test-all.bat`. Tests: successful backup replacement,
   invalid staged save preserves current slot, failed promotion preserves current
   slot, corrupt current generation does not replace a valid backup, and saving
   an autosave path does not change a manual path. No automatic recovery UI yet.
2. **World definitions and launch.** Add `WorldProfile` in
   `src/SiegeFX.Core/Assets/WorldProfile.cs`: durable ID, display name, tank filename,
   map root, and authored-content rules. Keep networking out of this type.
   Ehb maps to `World.dsmap`; Utraea maps to `MpWorld.dsmap`. Resolve an authored
   start through `StartPositionsStore` and the map's node/region ownership, rather
   than inventing Elddim coordinates. Integrate a world choice into
   `RenderHost.FlushSinglePlayerMenu` / `FlushDifficultyMenu` and existing frontend
   panels in `src/SiegeFX.Runtime/Render/Hud/FrontendScene.cs`,
   `SinglePlayerMenuPanel.cs` and `DifficultyMenuPanel.cs`. Extract a pure launch
   description used by
   `LaunchRegionViaRelaunch` and load relaunches; preserve the generic
   `Program.cs --play-region` path. Test both world launch paths, explicit
   unknown-world rejection, and authored-start selection using synthetic data.
3. **World-aware saves.** Add explicit `WorldId`, adventure identity and diagnostic
   build metadata to `SaveFile`; increment its schema and update `SaveStore.Load`.
   Preserve old Ehb saves. Legacy direct-region saves may already be Utraea:
   do not blindly classify every pre-profile save as Ehb; constrain migration to
   recognized legacy map roots and reject ambiguous cases. Wire
   `RenderHost.CaptureSave`, `ApplySave`, `PerformLoad`, `LaunchRegionForLoad`, and
   `LaunchRegionForLoadWithTanks` to validate/resolve the saved world. Isolate new
   adventure autosave/quicksave paths from manual saves and other adventures;
   update `SaveStore.ListSaves` accordingly. Extend `SaveSelfTest` for identity,
   location roundtrip, legacy migration, unknown schema/world, and launch-plan
   selection. Keep full 20/5/1 slot UI and legacy character import deferred.
4. **Content rules independent of transport.** Audit authored `single_player`
   rows first, then introduce explicit content eligibility in `TriggerRuntime`;
   supply it from the active world profile in `RenderHost`, preserving existing
   network-play behavior. Add a synthetic matrix test proving a multiplayer-authored
   row dispatches offline under Utraea rules, is skipped under Ehb solo rules,
   and survives network teardown without changing the world's rules. Leave
   compound conditions, item identity and Result of Condition for Milestone 2.
5. **Acceptance.** Independent read-only review of consequential diffs; lead
   inspection and fixes; focused plus relevant save/trigger/network regressions.
   Playtest Ehb new/load and Utraea select, authored spawn, exit, stream, save,
   quit, reload at the same position with networking off. Record evidence before
   checking any milestone criterion complete.

## Real-data prerequisites and rebase risks

Need the user's external `Maps/MpWorld.dsmap` and matching resources to verify
map-root spelling, default start group, lowest-id start slot, node-to-region
ownership, spawn/nav legality, and the actual use of `single_player=false` rows.
Inspect only legitimately owned data; commit neither tanks nor extracted assets.
Synthetic tests cannot establish Elddim gameplay or streaming acceptance.

Highest merge risk: the large `RenderHost.cs` launch/save/network paths,
`SaveFile` schema number/migration whitelist, and monolithic `test-all.bat`.
Keep core profile and transaction helpers small; avoid broad host refactors.

## First-session progress

- Audit and plan complete; upstream baseline verified.
- Initial build failed with `NETSDK1045`: installed SDK 10.0.401 cannot target
  the repository's .NET 11 frameworks. Installed checksum-verified official SDK
  `11.0.100-rc.1.26425.128` under ignored `.codex/dotnet11`; targets stay intact.
- Baseline `dotnet build SiegeFX.sln -c Release` succeeds with that SDK: zero
  errors, one existing `CA1416` warning in `WgcRecorder.cs:122`. Existing
  `--selftest-save` passes against schema 13 before transaction changes.
- User supplied `D:\Games\steamapps\common\Dungeon Siege 1`; verified external
  `Maps/MpWorld.dsmap` (95,147,964 bytes) and `Resources/Logic.dsres` exist.
  Authored-start inspection is in progress.
- Implemented first slice: `SaveStore.Save` uses unique sibling staging, explicit
  flush, loader validation before promotion, and `.bak` retention for readable
  current generations. Syntactically malformed current JSON does not replace a
  valid backup. Unsupported versions and valid JSON with incompatible shapes
  abort rather than discarding potential newer-engine data.
- Added `--selftest-save-transaction` and included it in `test-all.bat` option 39.
  Tests cover backup creation/rotation, invalid staged schema, future current
  schema (including a changed field type), Windows promotion-lock failure,
  staging cleanup, malformed-current backup preservation and path isolation.
- Red tests reproduced missing backup and unsupported snapshots replacing valid
  saves before implementation. Independent review caught unsupported-schema
  preservation, including deserialization failures preceding schema checks;
  both cases were addressed with regression tests.
- Final validation on 2026-09-22: solution Release build passes (same existing
  `CA1416` warning); `--selftest-save`, `--selftest-save-transaction`, and
  `--selftest-net` all exit 0; `git diff --check` passes. The sandbox prevents
  optional session-log creation under LocalAppData; test execution is unaffected.
- Limits: validation is JSON/schema-level, not full authoritative-section or
  cross-reference integrity. No power-loss/process-kill simulation or automatic
  backup recovery UI. The path-isolation test does not establish adventure-set
  isolation; that remains step 3. Some Windows replacement failures can leave
  the old generation at `.bak`, requiring manual recovery for now.
- Full Milestone 1 acceptance remains pending.

## Integration status — 2026-09-23

Implementation is integrated in the working branch and the normal Release output;
Milestone 1 acceptance still requires the gameplay checks below.

- `WorldProfile`, `WorldStartResolver.Resolve`, and `WorldLaunchPlan` now select
  the map and resolve its authored start. `RenderHostWorld` and
  `Hud/WorldSelectDialog` add Single Player → New Game → world selection, followed
  by the existing character creator and difficulty screen. Offline relaunches
  clear inherited networking variables.
- The user's map resolves Elddim to `multiplayer_world/regions/town_center`,
  start slot 1, node `0x4EE0A82E`, local position
  `(-2.733674, 0, -0.836062)`. Elddim is the first default group in authored order;
  Grescal is also marked default. Existing `FindDefault` ordering is retained.
- `RegionObjects`, `LogicGizmoStore`, and `ElevatorStore` support the map's
  `objects/regular` placement layer and content eligibility flags. Flat files
  take precedence if both layouts exist; original behavior for coexistence is
  not established. Veteran/elite content tiers remain deferred and are not
  equated with the existing gameplay difficulty setting.
- `TriggerRuntime.EnableMultiplayerAuthoredTriggers` permits authored rows while
  offline and survives session teardown. This does not establish full puzzle,
  compound-condition, dropped-item, or Result-of-Condition compatibility.
- Save schema 14 persists world, adventure/save-set identity, mode and engine
  version. Known legacy roots migrate deterministically without rewriting the
  originals. Quicksaves/autosaves are isolated by adventure. Loading selects
  the saved map and coordinate-frame root; another adventure or coordinate
  frame relaunches. Legacy identity uses source path, so moving/copying legacy
  files changes their migrated set and separate legacy slots are not grouped.
  Unregistered custom maps cannot currently create profile-backed saves.
- Independent review identified an unsafe streamed-neighbor coordinate-frame
  shortcut; routing and `ApplySave` now require the same root, with regression
  coverage. Follow-up independent review reported no material blockers.
- Final `dotnet build SiegeFX.sln -c Release --no-restore` succeeds: zero errors,
  the existing `WgcRecorder.cs` CA1416 warning only. The normal runtime DLL under
  `src/SiegeFX.Runtime/bin/Release/net11.0-windows10.0.22621.0` is updated.
- All six checks exit 0: `--selftest-save`, `--selftest-save-transaction`,
  `--selftest-world-profile`, `--selftest-offline-content`,
  `--selftest-world-launch`, and `--selftest-net`. Real-data checks use the user's
  external installation: Ehb start has 179 actors, Elddim 205; Utraea regular
  layers load 9,669 actors and 3,565 special placements. Tests cover migration,
  saved-world routing, coordinate-frame mismatch, slot isolation, transactional
  failures, and offline trigger eligibility after network teardown.
- `test-all.bat` option 39 includes the save/world/content/launch checks.
  `FrontendShotHost` renders the new selection dialog for reproducible visual
  inspection; the 800×600 capture has been inspected for clipping and overlap.
  Generated captures/builds remain ignored; original game assets stay external.

### Remaining gameplay acceptance

1. Launch the updated build, choose Single Player → New Game → Utraean Peninsula
   (Solo Adventure), create a hero, and select difficulty.
2. Confirm one hero spawns in Elddim with no lobby or network connection.
3. Walk out normally and cross into adjoining terrain; verify navigation and
   streaming without developer teleportation.
4. Make a named manual save away from spawn, quit, restart, and load it. Confirm
   the same Utraean location, hero state and inventory.
5. Check F5/F9, retain the manual slot, and start a second adventure to verify
   its quicksave does not replace the first adventure's checkpoint.
6. Recheck Ehb New Game and loading an existing Ehb save with this build.

The user passed the prior Ehb/save-safety playtest; that is not evidence for the
new Utraea integration. Full Milestone 1 acceptance remains pending these checks.
