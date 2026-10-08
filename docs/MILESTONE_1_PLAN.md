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

### Elddim church playtest findings — 2026-09-23

The user successfully launched Utraea and entered Elddim's church, but reported
a white carpet, attack-order sounds without swings, inability to leave, and poor
camera movement. This is a failed gameplay acceptance check, not milestone completion.

- Session `session-20260923-090240.log` identifies missing SNO texture
  `b_d_runner-06`. The original 64×128 RAW exists in Objects.dsres; terrain
  subsets previously searched Terrain.dsres alone. `TerrainTextureLoader` now
  supplies fallback resolution to all three terrain load/stream paths, including
  initial loading before the actor resource resolver exists. Resources remain
  external and temporary tank handles are disposed.
- The same log records repeated attack approaches toward enemies 58–205 units
  away followed immediately by abandonment. `ScreenActorPick` clips actor body
  segments before perspective division, preventing off-frustum actors from
  acquiring enormous pixel hit radii near the camera plane. A synthetic test
  reproduces the former hit using log-derived positions and an inferred yaw,
  and checks visible distant, nearby, edge-crossing, and near-plane-crossing
  bodies. Sole-hero attacks beyond the existing approach budget now decline
  before movement/order side effects; selected-companion behavior is preserved.
- Camera collapse remains unresolved. The log shows the eye nearly above the
  hero at minimum distance; new throttled `[camera-bound]` records identify
  blocking node, region, ray, distance and manual/automatic tilt on a retest.
  No camera tuning or node-classification change is claimed. The existing
  Ehb-derived underground heuristic also incorrectly classifies town_center
  from its high occludes_camera fraction; this needs separate evidence-led work.
- Final Release solution build passes with the existing CA1416 warning. All
  eight suites pass (the six integration suites plus `--selftest-screen-actor-pick`
  and `--selftest-terrain-textures <install>`). Texture tests cover the real
  carpet, native texture priority, absent texture, and failed fallback extraction.
  Independent review findings about companion dispatch and extraction failures
  were fixed; re-review found no material blockers. Church navigation and
  rendered appearance still require a new gameplay check; projection tests do
  not establish that every doorway/pathing issue is resolved.

### Church follow-up — 2026-09-23

The user confirmed the carpet is visible in the corrected build and entered
the church. Screenshots exposed two further defects: the talk cursor appeared
over floor away from NPCs, and an open-looking roof exposed trees outside.

- The talk cursor and right-click still used a three-unit ground radius while
  enemy selection used a clipped screen-space body hit. The hover and talk
  paths now share `PickFriendlyActorAtCursor` and `ResolveTalkOptions`, using
  the same visible body test. Interactive hover checks run before the
  ground-plane walkability ray, so a shallow camera angle cannot suppress
  a valid talk hover. A projection regression checks a floor click
  within the old radius but away from the NPC image. Runtime build and
  projection/dialogue checks pass. Gameplay still needs confirmation.
- The roof is an authored separate SNO: town_center node `0x71CF8656`, mesh
  `0xA0010158` (`t_grs01_houses_stone-chapel-a-roof`), positioned near
  `(7.4,13.5,39.5)` with fade keys section 1, level 3. The stock church group
  `town_center_house_3` has four active occupancy producers and controller
  `0x03200164`, which authors `fade_nodes(0xD5A3ACD9,1,3,-1,"out:black")`
  on entry and `in` on leave. The screenshot session records the expected
  roof texture loaded (zero missing terrain subsets), but only reports
  `[fade-grace]` cancelling a pending hide and shows zero hidden nodes while
  the hero stands near `(5.9,5,35.4)` inside the church. The cause was the
  engine's synthesized fade reversal: the controller has transient
  entered/left conditions, so its row becomes false on the very next tick
  even while the player stays inside; `AutoReverseFades` sent `in` and
  cancelled the authored `out:black`. A red synthetic test reproduced this
  exact group/action pattern. The generic fix synthesizes exit reversal only
  for rows with a held spatial condition (`party_member_within_*`,
  `actor_within_*`, or `go_within_*`). The test now passes and verifies all
  six supported sphere/box variants still reverse on exit. The latest
  build also logs generic `[trigger-group] entered/left` transitions for
  offline Utraea to confirm behavior during gameplay. The original map is
  unchanged; the roof still requires a rendered playtest.
- Release solution build and eight focused/regression suites pass after these
  changes, including the red→green church fade test and dialogue/screen pick.

### Elddim visual follow-up — 2026-09-23

The user confirmed in normal play that the carpet is visible, the church roof
disappears fully while inside and returns on exit, movement in/out works, and
the misplaced NPC talk cursor is fixed. This accepts those specific fixes;
Milestone 1's broader save/streaming acceptance remains open.

The next screenshot exposed three presentation issues:

- The created hero name was drawn by an invented top-center banner rather than
  over the character. The banner is removed; the existing world-projected party
  label renders the created hero name over the hero, is on by default, and can
  still be toggled with L. Off-screen label anchors are rejected.
- The chapel sign is placed as `sign_glb_magicshop_01` (SCID `0x03200c55`)
  in the original `town_center/objects/regular/interactive.gas`. Its template
  in `Logic.dsres` overrides the shared sign mesh's barn/mule texture with
  `b_i_glb_sign-magicshop-01` (potion and scroll). Static prop rendering now
  honors placement and inherited template slot-0 texture overrides before the
  embedded mesh texture, with a per-name GL cache. No map data is changed.
- Original `config/compass.gas` specifies radius 54 and cardinal orbit 28;
  the renderer already uses those values. Its cardinal RAWs are 16x16, while
  the renderer had compressed them to 14x14. Cardinal drawing now uses each
  texture's authored dimensions and preserves the original orbit.

An isolated Release build and eight headless checks pass, including a real
`MpWorld.dsmap`/`Logic.dsres` sign-material check. The rendered sign, floating
name and compass still need a fresh in-game check after installing the build.

### Elddim door and compass follow-up — 2026-09-26

The user confirmed the roof, carpet, movement, and talk cursor now behave in
normal play. The compass letters still overlap the dial rim. Its authored
16×16 cardinal images include uneven transparent margins; orbit 28 places
visible letter pixels on the 108-pixel dial's ornament. The renderer now draws
the letters at orbit 20. This needs visual confirmation at the user's UI scale.

The church's two ornate leaves (`0x03200BF8`, `0x03200BF9`) link to each other
through authored `door_basic.second_door` and share two `placement.use_point_scids`.
Their authored `aspect.use_range` is 0.3 around a stand point. Door hover and
left-click now ray-pick a visible closed leaf, show the opening-hand cursor,
walk to its nearest authored point, and open the linked pair from there.
Mirror leaves choose their quarter-turn from their own mesh extension and the
interacting actor's side, so both free edges move away from that actor. The
opening direction is saved per SCID and restored on load. Ground clicks no
longer open a nearby door. Invisible/faded doors are excluded from picking;
the pending order is cancelled on Stop, load, and new world clicks. These are
engine interactions, with no special-case trigger for the church or map edit.

The normal Release solution build and door, save, offline-content,
world-launch, and screen-pick selftests pass. Manual gameplay is still required for cursor/door approach,
both swing directions, save/reload, and the compass appearance. Milestone 1
acceptance remains open.

### NPC animation and compass follow-up — 2026-09-26

The user reported compass letters too far inward at orbit 20 and non-player
actors standing without animation, while the hero still animates in both new
and loaded Utraea games. Compass orbit is now 24 between the two observed
extremes (28 overlaps the rim); visual confirmation is pending.

The gameplay log shows 630 live non-player brains, so the world tick is not
paused. The original `Logic.dsres` reveals two generic animation gaps. Many
actors have a static one-frame `chore_default` and an animated
`chore_fidget`; the original `job_fidget.skrit` requests the latter during
idle, but SiegeFX kept the blender's default slot. The current idle selection
uses the loaded fidget for actors with an authored fidget job, while explicit
attack/cast/death overrides and movement still take priority. A placement
with `actor_auto_fidgets=false` can play an authored `select_fidget` initial
chore once before returning to its static default. This remains an
approximation: random fidget subanimations and the complete original MCP
request/animation-done scheduling are not yet modeled.

The original `dog_mp` template uses a complete `a_c_na_dg_fs0` prefix without
`chore_stances`. The loader previously appended another zero (`fs00`) and
found no clip. It now loads the authored `fs0_dsf` animation; the fix also
applies to other ambient templates with complete-stance prefixes. A headless
test against the user's own data confirms changing bone poses for the Elddim
Krug and blacksmith fidgets, an Ehb Krug, and the dog default loop, plus
opt-out and override behavior. The normal Release solution build and focused
world-launch, offline-content, door, and save checks pass. Normal-play visual
verification is still required.

### Elddim forge follow-up — 2026-09-28

The user confirmed the compass and non-player fidgets now animate, but Zabar's
hammer was silent and appeared to strike empty air. The original Zabar template
receives `we_anim_sfx(1)` during its FS2 fidget and calls the
`blacksmith_hammer` effect, which plays `s_e_env_hammer_anvil`. SiegeFX had
not forwarded PRS SFX notes into the trigger runtime. The generic note bridge
now forwards SFX1–SFX4 with the authored ordinal, including repeated idle
loops. Message rows receive only matching events, and each matching message
dispatches even if the row fired in the prior tick. The real-content idle test
checks Zabar's authored cue, trigger action, effect script and sound asset,
including repeated and nonmatching messages. Audible playback still needs a
normal-game check.

Elddim's town-center data places Zabar beside `stand_glb_anvil` but does not
place the distinct generic `anvil_glb` template there. The stand model itself
has an opaque raised upper section whose UVs map to the metallic patch of its
original texture; Zabar's hammer mesh intersects its world bounds at the
authored impact frame. The screenshot's apparent missing metal
and facing direction remain unresolved visually. Do not add a second anvil or
rotate Zabar without gameplay evidence for the original placement. The placed
`dog_mp` templates have a short pose animation but no authored wandering job,
so stationary location alone is not an AI regression. Milestone 1 gameplay
acceptance remains open.

### Elddim forge spark placement follow-up — 2026-09-28

The user showed the hammer sparks appearing on the ground by nearby flowers.
The original `blacksmith_hammer` effect creates the burst at `#TARGET` with
`offset(0,0,.8)` and plays its sound at `#TARGET_POSITION`. SiegeFX collapsed
both tokens to the actor's placement origin and interpreted that offset in
world +Z, so the sparks bounced at foot height beside the anvil. Actor-owned
trigger effects now carry the live actor's posed body anchor and facing into
the SFX context. `#TARGET_POSITION` stays at the actor's position for audio;
static trigger effects retain their original placement anchor. This is a
generic compatibility approximation: the shipped scripts distinguish object
and position targets, but the exact retail anchor chosen for a bare actor
`#TARGET` is not yet proven.

The real SFX1 note occurs at 0.8333 seconds in Zabar's fidget. At that frame,
the rendered hammer mesh comes within 0.0043 world units of the authored
stand's top triangle. Zabar, the stand, and the hammer attachment therefore
remain at their original transforms. A headless regression checks the real
effect against the stand surface and verifies the sound remains at
`#TARGET_POSITION`. Normal-play visual and audible confirmation remains open.

### Elddim blacksmith scale follow-up — 2026-09-29

The user confirmed the sparks now appear at the anvil, but the tongs in
Zabar's left hand still appeared too low. Zabar's placed instance explicitly
sets `[aspect] scale_base = 1`, overriding the inherited blacksmith template's
`0.85`. `ActorStats.FromTemplate` honored instance `scale_multiplier` but read
`scale_base` only from the template, so the renderer shrank Zabar and both
equipped tools. It now applies instance-first precedence to `scale_base` too.
The placed anvil and stump remain at their authored coordinates.

The original tongs are a rigid shield-hand item. At the hammer impact frame,
the current 0.85-scaled tongs' jaw vertices were about 16–19 cm below the
anvil surface; at Zabar's authored instance scale 1, the gap narrows to about
3.5–7.5 cm. This supports the scale override as the primary cause. Normal-play
confirmation is still needed; do not add a tongs-only offset or change the
workstation placement without further evidence.
