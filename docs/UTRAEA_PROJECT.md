# SiegeFX Utraean Peninsula Offline Adventure

## 1. Project purpose

This project extends SiegeFX so the original **Dungeon Siege 1 Utraean Peninsula** multiplayer world can be played as a complete offline adventure.

The project is intentionally **not** a rewrite of Dungeon Siege and is **not** initially a graphical remaster.

The first goal is gameplay compatibility.

Later goals include:

- optional party play
- exploration guidance
- full single-player-style world persistence
- modern optional WASD / Hybrid controls
- developer teleport/debug tooling
- optional backtracking assistance / convenience merchants
- mod layering
- improved resolution and UI scaling
- improved textures
- improved lighting
- improved models
- improved effects
- enhanced audio presentation
- modernized UI

The Kingdom of Ehb single-player campaign is not our primary focus because upstream SiegeFX is already centered on that compatibility target.

---

# 2. Target experience

The eventual New Game flow should conceptually support:

```text
NEW GAME

Kingdom of Ehb
  Original Campaign

Utraean Peninsula
  Solo Adventure
  Party Adventure
```

For this fork, our work is primarily under **Utraean Peninsula**.

---

# 3. Utraean adventure modes

## Solo Adventure

The original multiplayer world, playable fully offline with one player-controlled character.

Goals:

- no server
- no lobby
- no other human players
- no dependency on multiplayer networking
- preserve the original open-world exploration
- preserve original monsters, loot, quests, puzzles, and secrets
- preserve dangerous areas that can be discovered earlier than intended
- do not immediately rebalance the world

## Party Adventure

A later mode using SiegeFX's existing Dungeon Siege party systems.

Potential features:

- recruitable companions
- recruitable characters placed naturally in Utraean towns
- mules
- optional party-aware balancing later

Party Adventure must not block completion of Solo Adventure.

---

# 4. Exploration guidance

We want three player-facing settings.

## Classic

Original-style exploration.

No additional:

- signs
- hints
- quest markers
- map markers
- journal spoilers

## Subtle Hints

Add diegetic world hints such as:

- wooden signposts
- vague route names
- environmental clues
- rumors

The player should still need to explore.

Example:

```text
Eastern Trails →
```

rather than:

```text
Volcanic Caverns — 372 m →
```

## Clear Directions

Designed for players with limited time who still want to see major optional areas.

Use:

- clearer wooden signs
- more frequent route guidance
- explicit destination names where appropriate

Still prefer world-integrated guidance over HUD waypoints.

Some secrets may intentionally remain partially obscured.

For example, the Chicken Level should not necessarily be labeled:

```text
CHICKEN LEVEL →
```

even in Clear mode.

Instead, Clear mode may make the **Trial of Gallus** route and requirements easier to discover.

The exploration setting should be changeable during a playthrough.

---

# 5. Project principles

## 5.1 Generic engine compatibility over quest hacks

Do not write special-case gameplay logic for individual Utraean quest items unless the original game itself uses special-case logic.

Example of what not to do:

```csharp
if (item.Template == "fury_eye")
{
    OpenSecretDoor();
}
```

Preferred approach:

```text
Original map trigger detects matching GO
→ engine evaluates original DS trigger semantics
→ authored world logic opens door
```

We want SiegeFX to understand Dungeon Siege correctly.

This is especially important for:

- Townstones
- Fury's Eye
- Trial of Gallus
- Chicken Level
- pressure plates
- item placement
- item consumption
- secret routes

---

# 6. Known architectural findings

These findings came from auditing SiegeFX's current architecture and should be revalidated against the exact upstream commit used by the fork.

## 6.1 SiegeFX is primarily centered on the solo Ehb campaign

Evidence observed in the current codebase includes:

- launch assumptions around `World.dsmap`
- Farmhouse-centric start flow
- save/load assumptions tied to the Ehb world
- Ehb-focused campaign validation
- simplified trigger semantics that are sufficient for much of the solo campaign but are problematic for Utraean object-placement puzzles

This does not mean SiegeFX cannot support Utraea.

It means our fork has a clear compatibility niche.

---

## 6.2 Utraea exists as `MpWorld.dsmap`

The Utraean Peninsula is the original multiplayer-authored world.

SiegeFX already contains evidence of awareness of:

```text
multiplayer_world
```

and of world-loader scale compatible with `MpWorld`.

The main problem is not rendering a large map.

The main problem is making the frontend, save system, and trigger/content semantics treat Utraea as a first-class offline world.

---

## 6.3 Multiplayer-authored content must be separated from networking

Do not use an active network session as the deciding factor for whether multiplayer-authored Utraean trigger rows execute.

Conceptually we need:

```text
World: Utraean Peninsula
Content rules: multiplayer-authored
Networking: OFF
```

instead of:

```text
multiplayer-authored content
=
active multiplayer session
```

The exact type/enum names are implementation details and should be chosen after inspecting the current architecture.

---

## 6.4 Save files need explicit world identity

A save should persist a durable world identifier such as:

```text
KingdomOfEhb
UtraeanPeninsula
```

A Utraea save must reload:

```text
MpWorld.dsmap
```

and not reconstruct the world using hard-coded Ehb assumptions.

The save format should remain evolvable/versionable.

---

# 7. Dropped-item and Game Object compatibility

This is likely the most important generic engine work after initial offline launch.

## 7.1 Current conceptual problem

SiegeFX has historically modeled dropped items primarily through structures similar to:

```text
LootPile
LootEntry
```

These are useful for:

- rendering
- pickup
- grouping
- throw animation
- loot presentation

But original Dungeon Siege triggers reason about **Game Objects (GOs)**.

A dropped quest item may need to participate in:

```text
go_within_bounding_box
go_within_sphere
```

with filters such as:

```text
SCID
template
```

A visual loot pile is not necessarily the same thing as a logical GO.

One pile can contain multiple distinct items.

---

## 7.2 Required direction

Eventually introduce or preserve a logical object identity for individual world items.

Conceptually:

```text
WorldItemInstance

InstanceId
AuthoredScid?
TemplateName
State
Location
```

where Location can move through:

```text
World
Inventory
Equipped
Vendor
Deleted
```

The logical identity should survive:

```text
world → inventory → world
```

where original game semantics require identity preservation.

---

## 7.3 World GO registry

A likely architectural direction is a generic registry that can query trigger-eligible world objects by:

- runtime instance ID
- authored SCID
- template
- position/volume
- object kind

Possible conceptual kinds:

```text
Actor
Item
InteractiveProp
OtherGO
```

Do not lock the design to these names before inspecting current abstractions.

The key requirement is that trigger code should not need to know whether a GO is rendered as an actor, a loot pile, or another presentation type.

---

# 8. Trigger compatibility targets

Audit and eventually implement original Dungeon Siege semantics for the following.

## 8.1 `go_within_bounding_box`

Must be able to detect appropriate GOs, not only actors.

Filters may include:

- SCID
- template

Dropped items must be detectable when original content expects this.

## 8.2 `go_within_sphere`

Needs the same GO-level semantics.

Do not silently reduce it to "actors within sphere" if the original command supports GO filters.

## 8.3 Template filtering

Audit:

- case behavior
- multiple template names
- exact original semantics

## 8.4 Result of Condition

Original DS triggers can retain the actual object(s) that satisfied a condition.

This matters because subsequent trigger actions may need to act on those same objects.

A boolean-only API loses important information.

Conceptually:

```text
ConditionEvaluation
  Satisfied
  ResultObjects
```

rather than only:

```text
bool
```

## 8.5 Compound conditions

Audit whether current row/condition evaluation preserves original AND/OR/group behavior.

This is important for secrets such as:

```text
player is near entrance
AND
player has required item
```

A naive "any condition true" interpretation may reveal secrets incorrectly.

## 8.6 World-object messaging

Audit original semantics for sending messages to:

- fixed SCID
- Result of Condition
- multiple results

Generic object deletion/consumption may be required.

## 8.7 Item deletion / consumption

The Trial of Gallus offerings appear to be consumed/removed when correctly placed.

Do not special-case the known items.

Implement the original generic mechanism if the authored map uses one, such as a world-object deletion message.

---

# 9. Compatibility targets and why they matter

## Elddim

Must support:

- correct offline launch
- authored starting location
- leaving the town normally
- normal world streaming

## Townstones / Utraean Circle

Likely exercises:

- item pickup
- item identity
- dropped-item GO detection
- template matching
- persistent placement
- save/load while items are placed

## Tenstone

Likely another physical item-placement interaction.

## Fury's Eye

The shrine/puzzle is especially valuable as a regression test because arbitrary dropped objects are used to hold pressure plates.

Expected generic behavior:

```text
drop item on plate
→ plate condition becomes true
→ player walks away
→ plate remains active because item remains
→ pick item up
→ plate becomes false
```

## Trial of Gallus

Likely exercises:

- inventory checks
- compound trigger conditions
- secret entrance
- item placement
- Result of Condition
- item consumption/deletion
- elevators/platforms
- final secret-world transition

## Chicken Level

Do not declare support merely because the destination can be teleported to.

The goal is to reach it through normal authored gameplay.

## Volcanic Caverns / Hades

Must be reachable through normal world exploration and original transition logic.

---

# 10. Save-system specification

Utraea should behave as a true offline adventure, not like the original multiplayer session model.

The original multiplayer game does not persist complete world changes between sessions. Our default **Solo Adventure** and **Party Adventure** modes should instead support a full adventure save containing the character/party plus the persistent Utraean world state.

The save system should be designed as an explicit, versioned subsystem rather than as a thin extension of the original multiplayer character save.

## 10.1 Save ownership and adventure identity

Each new Utraean playthrough creates an **Adventure Save Set** with its own stable identifier.

Conceptually:

```text
AdventureSaveSet
  SaveSetId
  WorldId
  AdventureMode
  Character/Party identity
  Manual slots
  Autosaves
  Quicksave
```

A save set belongs to one playthrough. Manual saves and autosaves from different Utraean adventures must not share world state.

For Utraea:

```text
WorldId = UtraeanPeninsula
Map = MpWorld.dsmap
```

The save must persist this identity explicitly. Do not infer the world only from a region path or display name.

A Party Adventure save set also owns the current recruited-party composition and any party-specific state.

---

## 10.2 Save slots

Initial player-facing slot model:

```text
20 Manual Save Slots
5 Rotating Autosave Slots
1 Quicksave Slot
```

The exact UI presentation may change, but these logical categories should remain separate.

### Manual slots

Manual saves:

- are created only by explicit player action;
- are never overwritten by autosave;
- can be renamed by the player;
- display useful summary metadata;
- may be overwritten only after explicit confirmation;
- should preserve a previous valid generation internally for recovery.

Suggested save-card metadata:

```text
Save name
Character / party name
Adventure mode
World
Current region / friendly location
Difficulty
Play time
Character level / main skill summary
Timestamp
Exploration Guidance setting
Engine/save schema version
Optional screenshot thumbnail
```

The screenshot thumbnail is convenience metadata and must never be required to load the save.

### Autosaves

Maintain **5 rotating autosaves** per Adventure Save Set.

Autosaves should rotate oldest-first and must never overwrite manual slots.

Suggested autosave triggers:

- after first successful arrival in a major town;
- after a major quest milestone;
- after completing an important secret/puzzle sequence;
- immediately before an irreversible or high-risk world transition where safe;
- after major party-composition changes in Party Adventure;
- periodic safe autosave after approximately 10 minutes of meaningful play;
- optional save-on-exit if the game is in a safe state.

Do not autosave merely because the player crossed every small region boundary.

Avoid creating excessive disk churn while traversing the stitched world.

### Quicksave

Provide one optional quicksave slot.

Quicksave:

- is explicitly requested by the player;
- may overwrite the previous quicksave without an additional confirmation;
- must still use the same atomic/validated save transaction as manual saves;
- should not be allowed to create a structurally invalid snapshot.

A quickload action may be added later.

---

## 10.3 When saving is allowed

The player should generally be able to save freely, but the engine must avoid snapshotting transient world state that cannot be restored safely.

Manual save requests should be accepted immediately when the world is in a stable state.

If the world is temporarily unsafe to serialize, the game should **queue the save request** and execute it as soon as a safe point is reached rather than silently discarding it.

Examples of temporarily unsafe states may include:

- world/map load in progress;
- region streaming transaction in progress;
- save/load already in progress;
- critical scripted transition halfway through mutation;
- an elevator/platform transform being reconstructed;
- an inventory/world-item transfer halfway through commit;
- a trigger transaction whose actions have not finished applying.

Combat by itself should **not automatically forbid saving** unless testing proves that active-combat state cannot be restored safely.

The long-term goal is a robust snapshot system, not a traditional RPG rule that arbitrarily disables saving during combat.

The UI should distinguish:

```text
Saved
Save queued — waiting for safe world state
Save failed
```

Never present a successful-save message until the transaction has been committed and validated.

---

## 10.4 What a Full Adventure Save persists

A normal Utraea save should persist at least:

### Adventure/world identity

- SaveSetId
- save schema version
- `WorldId`
- `MpWorld.dsmap` identity
- Adventure Mode: Solo / Party
- difficulty
- relevant content-ruleset flags
- enabled fork-specific gameplay settings

### Player and party

- character identity
- stats
- skills
- health/mana
- inventory
- equipment
- gold
- spell books / spell state
- current party members
- recruit state
- mule state where applicable
- party formation/state that is meaningful to gameplay

### Location

- current region
- exact world position
- facing/orientation where required
- current stitched-world context
- safe fallback spawn for recovery

### Quest and trigger state

- quest state
- trigger state
- trigger variables/counters
- secrets already activated
- doors/elevators/platforms whose persistent state matters
- Townstone/Utraean Circle progress
- Fury's Eye puzzle state
- Trial of Gallus state
- Chicken Level access state

### World objects

Persist world-item identity where original Dungeon Siege semantics require it.

This includes:

- physically placed Townstones
- quest items dropped into the world
- ordinary objects holding down pressure plates
- Gallus offerings before consumption
- relevant dropped items
- consumed/deleted persistent objects
- runtime-spawned persistent objects
- object template identity
- authored SCID where relevant
- stable runtime instance identity where needed

Do not reconstruct a quest-critical placed object as an anonymous loot pile if doing so loses the identity required by original trigger logic.

### Fork-specific world overlays

Where appropriate:

- Exploration Guidance setting
- spawned guidance-sign overlay state
- Backtracking Assistance setting
- fork-specific merchant state
- other data-driven Utraea overlays

Prefer deriving deterministic overlay objects from settings rather than serializing redundant presentation state when possible.

### Non-authoritative/derived state

Do **not** treat reconstructable runtime caches as authoritative save data.

Examples that should normally be rebuilt:

- render caches
- GPU state
- navigation caches that can be regenerated
- temporary UI state
- transient audio emitters
- debug overlays

---

## 10.5 Save file structure and atomic writes

Use a versioned save format with an explicit manifest.

A conceptual save package:

```text
save/
  manifest
  player-party snapshot
  world snapshot
  quest-trigger snapshot
  persistent-world-objects snapshot
  fork-settings snapshot
  optional screenshot
```

The exact on-disk representation may be JSON, binary, archive-based, or mixed; choose it after inspecting current SiegeFX save architecture.

Requirements:

- explicit `SaveSchemaVersion`;
- explicit `WorldId`;
- explicit Adventure Mode;
- engine/build metadata for diagnostics;
- per-section integrity/checksum data where practical;
- ability to distinguish authoritative state from optional/derived metadata.

Saving must be transactional.

Preferred flow:

```text
1. Build snapshot in memory.
2. Write to a temporary/staging save.
3. Flush/close the staged data.
4. Validate required sections and integrity metadata.
5. Preserve the previous valid slot generation as backup.
6. Atomically promote the staged save to the active slot.
7. Only then report "Saved".
```

A crash or power loss during steps 1-4 must not destroy the previously valid save.

Each manual slot and quicksave should retain at least **one previous valid generation** internally when practical.

Autosaves already provide additional rolling recovery points.

---

## 10.6 Save schema versioning and migration

Every save must contain an explicit schema version.

Example:

```text
SaveSchemaVersion = 1
```

When the format changes:

```text
old schema
  → migration step(s)
  → current in-memory representation
  → optional re-save in current format
```

Migration code must be:

- deterministic;
- tested with representative old saves;
- non-destructive;
- able to report unsupported migrations clearly.

Never modify the only copy of an old save in place before a successful migration has been validated.

When possible, keep migration steps incremental:

```text
v1 → v2
v2 → v3
v3 → v4
```

rather than maintaining many direct historical conversions.

The project should maintain save-format regression fixtures once stable fixture data can be created without redistributing copyrighted Dungeon Siege assets.

---

## 10.7 Importing classic Dungeon Siege multiplayer character saves

Support for importing a classic multiplayer character is desirable because players may already have characters developed in the original Utraean multiplayer mode.

This import is a **migration into a new Full Adventure Save Set**, not an attempt to continue the original multiplayer session state.

The importer must operate read-only on the original save.

Conceptual flow:

```text
Classic multiplayer character save
  ↓
Read-only parser/importer
  ↓
Migration preview
  ↓
Create NEW Utraea Adventure Save Set
  ↓
Initialize fresh persistent MpWorld state
  ↓
Place imported character at canonical safe Utraea start
```

Import what can be mapped confidently, such as:

- character name
- appearance where available
- attributes
- Melee / Ranged / Nature Magic / Combat Magic skill progression
- inventory
- equipment
- gold
- spells/books where supported

Do not invent world progress that the classic multiplayer save never stored.

World state should initially be a fresh Utraean world unless reliable legacy data proves otherwise.

Default migration spawn:

```text
Elddim / authored canonical Utraea start
```

If the legacy character format reliably stores a valid Utraean town/start location, preserving that may be considered later.

### Quest-item reconciliation

Legacy characters may carry unusual or quest-critical items.

The importer must not blindly create duplicate world-critical objects.

Before final implementation, inspect the real legacy character format and original Utraean item semantics.

Possible policy:

```text
normal items
  → import normally

recognized unique quest item
  → import with preserved template/identity where safe
  → reconcile corresponding authored world instance

unknown/ambiguous critical item
  → warn in migration preview
  → do not silently discard or duplicate
```

Do not special-case Gallus/Townstone items until the original identity semantics have been established.

### Migration preview

Before creating the new save, show/report:

- source character
- imported stats/skills
- imported inventory/equipment count
- destination Adventure Mode
- destination world/start
- warnings
- unsupported fields/items

The original multiplayer character file must remain untouched.

Record migration provenance in the new save metadata for diagnostics:

```text
ImportedFrom = ClassicDungeonSiegeMultiplayer
ImporterVersion = ...
SourceFingerprint = ...
```

Do not store or redistribute the original save file inside the project repository.

---

## 10.8 Recovery from corrupted, incomplete, or partial saves

Recovery should prioritize **never silently fabricating quest/world state**.

### Validation on load

Before loading a save:

1. validate manifest;
2. validate schema version;
3. validate required sections;
4. validate checksums/integrity metadata where available;
5. validate `WorldId` and map compatibility;
6. validate critical cross-references such as persistent item IDs;
7. only then construct live gameplay state.

### Recovery order

If the active save generation is invalid:

```text
1. Try the slot's previous valid generation.
2. Offer the newest valid autosave.
3. Offer another valid manual save from the same Adventure Save Set.
4. Attempt limited section-level recovery only when the missing data is safely reconstructable.
5. Offer explicit Character Rescue only as a last resort.
```

Never silently roll the player backward without telling them which recovery point was used.

### Safe section-level reconstruction

Some missing/corrupt data can be regenerated safely.

Examples:

```text
corrupt screenshot
→ ignore/regenerate

corrupt UI metadata
→ reset to defaults

corrupt derived navigation/render cache
→ rebuild

corrupt deterministic guidance-sign overlay
→ respawn from current settings
```

### Authoritative world-state corruption

If authoritative data is corrupt, such as:

- quest state
- trigger state
- persistent world objects
- Townstone placement
- Gallus offering state
- deleted/consumed unique objects

do **not** silently reset that section to defaults.

Prefer rollback to a known-good generation/autosave.

Mixing a newer character snapshot with an older world snapshot should not happen automatically because it can create:

- duplicated quest items;
- missing quest items;
- completed quests with unopened world paths;
- consumed Gallus items reappearing;
- Townstones both in inventory and on the Circle.

### Character Rescue mode

As a last-resort explicit recovery tool, allow the player to salvage the character/party portion of a damaged adventure into a **new fresh Utraea world**.

Conceptually:

```text
Damaged Adventure Save
  ↓
recover character/party data only
  ↓
create new Adventure Save Set
  ↓
fresh MpWorld
  ↓
spawn at safe canonical location
```

This must be clearly labeled as recovery, not normal load.

The UI should warn that world/quest progress may be lost.

The damaged original save must be preserved for future repair attempts.

### Recovery diagnostics

When a load fails or recovery is used, write a human-readable diagnostic report containing:

- slot/save ID
- schema version
- world ID
- failed section
- integrity error
- recovery path chosen
- engine build/version
- timestamp

Do not include original proprietary game assets in diagnostics.

---

## 10.9 Autosave safety rules

Autosave must never make the situation worse.

Rules:

- never overwrite all known-good autosaves in a single session;
- do not rotate a good autosave out until the new autosave has committed successfully;
- do not autosave continuously during a known-broken/recovery state;
- after loading through recovery, preserve the recovered source before producing new autosaves;
- if a migration/import is incomplete, do not create normal autosaves until the new Adventure Save Set has been successfully committed.

Consider reserving the newest valid pre-migration/pre-recovery save until the player has successfully played and saved afterward.

---

## 10.10 Save/load UX

The Load screen should clearly distinguish:

```text
Manual
Autosave
Quicksave
Recovered / Backup
Imported Character
```

A save entry should indicate incompatibility or recovery status instead of simply disappearing from the list.

Possible states:

```text
Ready
Older save version — migration required
Recovered from backup
Missing optional data
Corrupted — recovery available
Unsupported version
Missing required game data/mod
```

If currently enabled mods/remaster content are required to interpret persistent objects correctly, the loader should warn before opening the save.

Do not block loading merely because optional graphical/audio enhancements differ.

---

## 10.11 Save-system developer and test requirements

Developer tools should support:

- forcing a manual save;
- forcing an autosave;
- listing save generations;
- validating a save without loading it;
- intentionally corrupting/removing a test section;
- testing backup fallback;
- testing schema migration;
- testing classic multiplayer character import;
- dumping save metadata/world-object references for diagnostics.

Required automated scenarios should eventually include:

### Manual vs autosave isolation

```text
create Manual Slot 1
→ trigger several autosaves
→ Manual Slot 1 remains unchanged
```

### Atomic-write failure

```text
valid save exists
→ simulate failure during new write
→ previous save remains loadable
```

### Utraea world identity

```text
save in Utraea
→ quit
→ load
→ MpWorld.dsmap is selected
→ exact Utraean region/location restored
```

### Placed quest item

```text
place Townstone / trigger-relevant item
→ save
→ load
→ same logical item exists at same placement
→ trigger evaluates correctly
```

### Pressure plate

```text
drop ordinary item on Fury-style pressure plate
→ save
→ load
→ item remains
→ plate remains active
```

### Consumed Gallus offering

```text
offering consumed
→ save
→ load
→ offering remains consumed
→ it does not duplicate in inventory/world
```

### Partial corruption

```text
damage optional screenshot/metadata
→ save still loads

damage authoritative world-state section
→ loader refuses silent reset
→ previous generation / autosave recovery is offered
```

### Classic multiplayer import

```text
legacy character fixture
→ import preview
→ create new Utraea Adventure Save Set
→ character progression preserved
→ world starts from defined fresh baseline
→ legacy source remains unchanged
```

---

## 10.12 Milestone implementation boundary

For **Milestone 1**, implement the minimum robust foundation required to prove full Utraea persistence:

- explicit `WorldId`;
- Utraea save/reload against `MpWorld.dsmap`;
- one or more manual save slots through the existing UI/path;
- transactional/atomic save replacement where practical in the current architecture;
- enough player/world state to resume at the correct Utraean location;
- versioned save metadata;
- no regression to Kingdom of Ehb saves.

The complete slot UI, classic multiplayer import, sophisticated section-level recovery, thumbnails, quicksave, and full corruption tooling may follow incrementally.

However, Milestone 1 must avoid architectural choices that make those later requirements difficult or impossible.

The default long-term save philosophy is:

```text
Full Adventure Save
  character + party + persistent relevant world state
```

An optional **Classic Multiplayer Save Style** may be considered later for players who intentionally want the original multiplayer reset behavior, but it is not required for the first Utraea release and must never be the default.

---

# 11. Mods and asset layering

Later, preserve compatibility with original DS1 content and community mods where practical.

Preferred conceptual precedence:

```text
User overrides
Remaster overrides
Enabled community mods
Official/original content
```

Do not modify the user's Steam installation in-place unless absolutely necessary.

Prefer external override/mod layers.

---

# 12. Visual quality and remaster strategy

Visual work should be split into two phases.

## 12.1 Early visual baseline

After Milestone 1 proves that Utraea launches and saves correctly, an early **visual baseline pass** is allowed before the deeper compatibility work is complete.

The goal is to make development and testing pleasant without committing to a major renderer rewrite.

Target improvements:

- native 1080p / 1440p / 4K support
- correct aspect-ratio handling
- sharp, scalable UI
- readable fonts at modern resolutions
- improved texture filtering / anisotropic filtering
- practical anti-aliasing / MSAA settings
- sensible draw distance
- modern camera zoom/rotation limits where compatible
- elimination of obvious low-resolution presentation artifacts

This should continue to use the original Dungeon Siege assets.

Conceptual presentation presets may eventually be:

```text
Original
  closest practical approximation of original presentation

Enhanced
  original assets + modern resolution/filtering/AA/camera/UI improvements

Remastered
  replacement textures/models/materials and deeper renderer upgrades
```

All visual enhancements should remain optional where practical.

## 12.2 Deep visual remaster — later

Do not begin the deep remaster until Utraean gameplay compatibility is stable.

Later goals may include:

- higher-resolution textures
- runtime texture override layers
- improved lighting
- normal maps
- roughness/material maps
- better shadows
- improved water
- better particles
- higher-detail props
- higher-detail characters
- modern model import paths
- improved vegetation
- improved environmental effects
- modernized UI
- scalable UI and text
- better fog and atmosphere

Original gameplay mechanics should remain the default.

The preferred philosophy is:

> Make Dungeon Siege look like the game players remember, without redesigning the game itself.

---

# 13. Milestone 1 — Offline Utraea foundation

## Objective

Make Utraea a first-class offline world that can launch, save, reload, and execute multiplayer-authored content without requiring a network session.

## Required work

1. Build current upstream SiegeFX successfully.
2. Record the exact upstream commit used as the fork baseline.
3. Identify every relevant hard-coded Ehb/Farmhouse assumption.
4. Introduce a clean world-profile/world-definition abstraction.
5. Represent at least:
   - Kingdom of Ehb / `World.dsmap`
   - Utraean Peninsula / `MpWorld.dsmap`
6. Launch Utraea through normal frontend/game flow.
7. Use Utraea's authored start location where possible.
8. Separate multiplayer-authored content rules from network-session state.
9. Persist explicit world identity in save data.
10. Reload a Utraea save against the correct `.dsmap`.
11. Add automated tests around these behaviors where practical.
12. Keep changes easy to merge/rebase with upstream.

## Non-goals

Do not yet implement:

- Party Adventure
- guidance signs
- graphical remastering
- new models
- Utraea rebalancing
- large-scale GO-registry refactor unless launch/save work proves it immediately necessary

---

# 14. Milestone 1 acceptance criteria

Milestone 1 is complete only when:

- the project builds cleanly;
- Kingdom of Ehb launch still works;
- Utraean Peninsula can be selected/launched offline;
- the player appears at the correct authored Utraean start or a clearly documented temporary equivalent;
- Utraean multiplayer-authored trigger rows can execute without active networking;
- saving in Utraea records the world identity and versioned save metadata;
- quitting and loading the save reopens `MpWorld.dsmap` and restores the correct Utraean location;
- autosave/manual-save work does not overwrite unrelated manual slots;
- an interrupted/failed save cannot destroy the last known-good slot generation where the implemented storage architecture permits atomic replacement;
- focused tests pass;
- existing relevant regression tests still pass;
- no original Dungeon Siege assets are added to the repository.

---

# 15. Milestone 2 — Game Object / trigger semantics

Begin only after Milestone 1 is stable.

## Objectives

Build generic DS-compatible world-object semantics needed by Utraean puzzles.

Investigate and implement:

- dropped-item trigger visibility
- individual logical item identity
- SCID preservation where required
- `go_within_bounding_box`
- `go_within_sphere`
- template filters
- compound conditions
- Result of Condition
- world-object message routing
- generic deletion/consumption
- save/load of placed items

## Regression scenarios

Create minimal tests for cases such as:

### Pressure plate

```text
drop ordinary item on plate
→ plate active
→ move player away
→ plate remains active
→ pick item up
→ plate inactive
```

### Template-specific plate

```text
pile contains:
- health potion
- fury_eye

query:
template = fury_eye

expected:
only Fury's Eye satisfies condition
```

### Consumption

```text
condition finds Fury's Eye
→ action targets Result of Condition
→ generic delete message
→ Fury's Eye disappears
→ unrelated item remains
```

### Save/load

```text
place quest item on trigger
→ save
→ quit
→ reload
→ same logical object remains on trigger
→ trigger state evaluates correctly
```

---

# 16. Milestone 3 — Utraean compatibility pass

Run the real world through a permanent compatibility checklist.

Suggested matrix:

```text
[ ] Elddim spawn
[ ] Elddim exit
[ ] region streaming
[ ] townstone pickup
[ ] all townstones obtainable
[ ] Utraean Circle accepts stones
[ ] Circle completion
[ ] Tenstone interaction
[ ] Fury's Eye route
[ ] Fury pressure plates
[ ] Fury's Eye obtained normally
[ ] Gallus book requirement
[ ] hidden Gallus entrance
[ ] three Gallus offerings
[ ] offerings consumed
[ ] Gallus platform/elevator sequence
[ ] Chicken Level reached normally
[ ] Volcanic Caverns / Hades reached normally
[ ] save/reload at critical states
```

Record exact evidence for each checked item.

---

# 17. Milestone 4 — Party Adventure

Only after Milestone 3 is reliable.

Start conservatively.

Goals:

- allow existing SiegeFX party mechanics in Utraea;
- place recruits without rewriting the original world unnecessarily;
- preserve Solo Adventure unchanged;
- preserve original world balance initially.

Possible later additions:

- recruit distribution by town
- mules
- optional party scaling

---

# 18. Milestone 5 — Exploration guidance

Implement the three settings:

```text
Classic
Subtle Hints
Clear Directions
```

Prefer a data-driven overlay system.

Example conceptual data:

```text
Volcanic Caverns
  Classic:
    no additions

  Subtle:
    vague forest-route sign
    cryptic environmental warning

  Clear:
    explicit Eastern Island sign
    explicit Volcanic Caverns direction near later junction
```

Guidance should be switchable without starting a new game.

Do not expose every secret equally.

The Chicken Level should retain mystery.

---

# 19. Input modernization — optional WASD / Hybrid controls

Add optional modern movement without removing classic point-and-click controls.

Player-facing movement modes:

```text
Classic Point & Click
WASD / Keyboard
Hybrid
```

## Classic Point & Click

Preserve original Dungeon Siege-style click-to-move behavior.

## WASD / Keyboard

Use camera-relative movement:

```text
W = forward relative to camera
S = backward
A = left
D = right
```

Movement must still pass through SiegeFX navigation, collision, and legal-movement systems.

Do **not** implement WASD by directly moving the character transform in a way that bypasses:

- pathfinding constraints
- walls/doors
- terrain legality
- collision
- original movement speed/rules

## Hybrid

Recommended enhanced mode.

Allow simultaneous use of:

- camera-relative WASD movement
- mouse selection
- mouse attacks/interactions
- click-to-move for distant destinations
- party selection and formation commands

In Party Adventure, WASD should primarily control the current leader while the existing party AI/pathfinding keeps followers with the formation.

All bindings should eventually be configurable.

Potential later bindings may include:

```text
WASD        move
Q / E       camera rotate
Mouse wheel zoom
1-8         party selection
F           select party
Space       pause
```

Exact bindings are implementation details and should follow current SiegeFX input architecture.

---

# 20. Developer tools and rapid testing

Utraea is too large to require replaying from Elddim for every test.

Developer tooling should be added early enough to support Milestone 2 and Milestone 3 efficiently.

## 20.1 Direct region launch

Preserve/extend SiegeFX's existing direct-region development workflow so Utraean regions can be launched without normal campaign travel.

Conceptually:

```text
--world utraea --play-region <region>
```

The exact CLI syntax should follow the existing SiegeFX architecture.

## 20.2 Named developer travel anchors

Add data-driven named anchors for important test locations.

Examples:

```text
townstone_circle
fury_pressure_plate_1
fury_pressure_plate_2
gallus_hidden_entrance
gallus_offerings
chicken_level_entry
volcanic_caverns_route
endless_dunes_pyramid_1
endless_dunes_pyramid_2
```

Each anchor should resolve:

- world
- region
- position
- orientation

Named anchors are preferable to raw coordinates for repeatable testing.

## 20.3 Developer travel UI

A development-only interface may expose:

```text
World
Region
Named Destination
Guidance Mode
Teleport
```

A simple F10-style developer panel is acceptable if it fits SiegeFX's current UI architecture.

## 20.4 Developer inventory / state helpers

Developer-only commands may:

- give a specific item/template
- remove an item
- set or inspect quest/world state
- spawn required Gallus test items
- inspect active triggers
- inspect nearby Game Objects
- inspect SCID/template identity
- inspect Result-of-Condition objects

Examples:

```text
dev give fury_eye
dev teleport gallus_offerings
```

These must be development/testing facilities only.

They must not replace normal gameplay logic.

## 20.5 Trigger / Game Object inspector

A later high-value debug tool should show, for a selected/nearby trigger:

- trigger ID
- condition type
- filters
- current true/false state
- matching GOs
- SCIDs/templates
- Result-of-Condition contents
- outgoing messages/actions

This will be especially useful for:

- Fury pressure plates
- Townstone Circle
- Gallus offerings
- hidden entrances
- custom guidance overlays

## 20.6 Manual + automated verification

Automated tests should confirm that the correct objects/settings are active.

Manual teleport-based checks should verify visual placement and usability.

Example:

```text
Guidance = Classic
→ extra sign absent

Guidance = Subtle Hints
→ subtle sign present

Guidance = Clear Directions
→ clear-direction sign present
```

Automated tests cannot catch a sign being buried in terrain, facing the wrong direction, or blocked by scenery, so visual inspection remains necessary.

Developer tools should be disabled or hidden in normal release builds, for example behind a development build or `--dev` flag.

---

# 21. Backtracking assistance and convenience merchants

Add an optional convenience system for players who want to experience major secrets without repeating long fetch/backtracking sequences.

This is separate from **Exploration Guidance**.

Possible player-facing setting:

```text
Backtracking Assistance

Off / Classic
Secret-item merchants
```

The default should preserve original behavior.

## 21.1 Trial of Gallus convenience merchant

A lore-friendly merchant may be placed near the Trial of Gallus route and offer missing required items such as:

- Fury's Eye
- starting knife
- Trial of Gallus book

Design principles:

- sell only items the player is missing where practical
- avoid unlimited duplicate quest items
- keep the merchant diegetic rather than labeling it as a debug/convenience vendor
- do not automatically complete Gallus
- do not bypass original trigger logic

The correct implementation should be:

```text
merchant supplies a normal compatible item
→ item enters normal inventory/world flow
→ original Gallus trigger logic accepts it
```

not:

```text
merchant purchase
→ hard-coded "complete Gallus" shortcut
```

Before implementation, inspect the real `MpWorld.dsmap` / Gallus logic to determine whether the authored puzzle matches items by:

- template
- SCID
- other identity/state

If original logic requires a specific authored identity, solve that generically rather than adding a Gallus-specific exception.

The same backtracking-assistance framework may later support other Utraean cases where a required item is easily lost or requires excessive replay.

---

# 22. Audio enhancement and soundtrack preservation

Audio enhancement is a later remaster goal, but it should preserve the identity of the original Dungeon Siege soundtrack and sound design.

## 22.1 Music philosophy

Keep the original Jeremy Soule compositions and original dynamic music behavior.

Do not replace the soundtrack merely to sound "modern."

Preferred goal:

> Make Dungeon Siege sound like players remember it sounding.

Preserve:

- compositions
- track identities
- mood/context behavior
- voices
- recognizable combat/event sounds

Improve where practical:

- playback fidelity
- resampling quality
- mixer headroom
- transitions/crossfades
- spatial placement
- environmental reverb
- ambient depth
- occlusion
- optional headphone spatialization

## 22.2 Original audio audit

Before changing music assets, inspect the user's legitimate `Sound.dsres` data and determine:

- codec
- sample rate
- channel count
- bitrate where applicable
- spectral cutoff
- clipping
- loudness
- dynamic range
- whether multiple in-game segments form one commonly named soundtrack track

Do not assume a YouTube upload or wiki OGG is a better source merely because its bitrate/sample rate is higher.

A wiki/online version is genuinely higher quality only if analysis shows real musical information absent from the original game file, not just:

- upsampling
- transcoding
- EQ
- artificial stereo widening
- higher nominal bitrate

Useful evidence includes:

- coherent spectral information beyond the original codec cutoff
- cleaner transients
- fewer lossy-codec artifacts
- genuinely different source dynamics/stereo detail
- credible source provenance

The wiki soundtrack can be used as a reference/index, not automatically as a replacement source.

## 22.3 Environmental audio

Where original Dungeon Siege authoring provides room/mood/environment information, reinterpret it using modern audio where practical.

Potential enhancements:

- cave/dungeon reverb
- open-forest ambience
- large-cavern decay
- volcanic low-frequency ambience
- indoor/outdoor transitions
- occlusion/low-pass filtering behind walls
- distance filtering
- ambient sound zones

## 22.4 Optional headphone spatialization

HRTF/headphone spatial audio may be offered as an optional setting.

Do not force it because it can alter perceived tonal balance.

## 22.5 Audio settings

Potential future settings:

```text
Music
Effects
Ambient
Voices

Audio Presentation
  Original
  Enhanced

Environmental Reverb
  On / Off

Headphone Spatial Audio
  On / Off

Occlusion
  On / Off

Music Source
  Original
  Enhanced Local
```

Any higher-quality local soundtrack source must remain external to the repository and respect copyright/licensing.

---

# 23. Utraean world analysis and Endless Dunes

Add a development-time world-analysis capability for understanding the topology of `MpWorld.dsmap`.

This is particularly useful for the late-game desert / Endless Dunes area around Grescal, which can be extremely difficult to map by normal play and may use wraparound/stitched topology that makes ordinary width/height intuition misleading.

The analyzer should eventually be able to report/export:

- region/node count
- region adjacency
- stitch connections
- wraparound connections
- world-space bounding dimensions
- estimated walkable area where practical
- distances between important landmarks
- location of Lost Pyramids
- routes toward Volcanic Caverns / Hades
- inaccessible or unused authored areas if discoverable

A debug visualization/export is desirable.

Example outputs:

```text
Utraean Peninsula
  Grescal
  Mesa Desert
  Endless Dunes
    Lost Pyramid A
    Lost Pyramid B
    wrap/stitch connections
```

This tool is for analysis and sign-placement planning, not for changing the original world topology.

Classic mode should preserve the unusual desert behavior.

Subtle/Clear guidance may later make the desert easier to navigate without removing the underlying topology.

---

# 24. Development environment and original game data

Primary runtime/test development should happen on a native Windows machine with the user's legitimately owned Dungeon Siege installation available.

Recommended layout:

```text
Windows
  Dungeon Siege Steam installation
  SiegeFX fork
  .NET SDK
  Git
  your agent harness of choice
```

Prefer native Windows for the main build/run/test loop.

WSL may still be used for unrelated tooling, but it should not be required for the SiegeFX runtime workflow.

Reasons include:

- original game installation is on Windows
- graphical runtime testing is Windows-centric
- Windows-specific tooling may be involved
- direct access to Steam game data is simpler
- avoiding cross-filesystem overhead/friction

Keep the repository separate from the original game installation.

Do not commit or redistribute original Dungeon Siege assets.

The Mac may still be used for:

- planning
- documentation
- Git/GitHub work
- code reading
- non-runtime tasks

but the Windows machine should be considered the primary playtest/runtime environment.

---

# 25. Future milestone ordering

After the existing Milestones 1-5, the recommended order is:

## Milestone 6 — Developer tooling

Minimum useful developer travel/debug support:

- Utraea direct-region launch
- named travel anchors
- developer item/state helpers
- trigger/GO inspection foundations

Some of this may be pulled earlier if required to make Milestones 2-3 practical.

## Milestone 7 — Input modernization

Implement:

- Classic Point & Click
- WASD / Keyboard
- Hybrid

Preserve classic controls and make modern controls optional.

## Milestone 8 — Backtracking assistance

Implement the optional convenience-merchant framework, beginning with Trial of Gallus if the real authored logic supports it cleanly.

## Milestone 9 — Audio enhancement

Audit original audio, modernize playback/environment processing, and preserve original soundtrack identity.

## Milestone 10 — Deep visual remaster

Only after core Utraea gameplay is reliable:

- replacement textures
- materials
- models
- lighting
- particles
- water
- vegetation
- broader renderer work

---

# 26. Agent orchestration

The project uses a deliberate lead/worker/reviewer hierarchy. `AGENTS.md` states the binding
contract for each role under **Subagent roles**; this section is the supporting detail.

Which harness and which models you use to satisfy these roles is your own choice — see
**Harness bindings** in `AGENTS.md`.

## Lead engineer / planner / integrator

Use a high-capability model at high reasoning effort.

Responsibilities:

- architecture
- project planning
- task decomposition
- deciding generic engine semantics vs local workarounds
- assigning bounded tasks to subagents
- reviewing implementation output
- integration
- regression analysis
- milestone acceptance
- maintaining this document and `AGENTS.md`

The lead should keep final authority for architecture and integration.

## Explorer

Use a fast, inexpensive model at medium reasoning effort, with read-only access.

Responsibilities:

- symbol searches
- call-chain tracing
- repository mapping
- locating tests
- identifying hard-coded assumptions
- documentation verification
- bounded read-heavy investigation

The explorer should return exact files/symbols and clearly separate proven behavior from assumptions.

## Implementation worker

Use a strong coding model at high reasoning effort, with workspace-write access, for approved
bounded tasks.

Responsibilities:

- substantive C# implementation
- tests
- save/load work
- trigger-system changes
- world-profile implementation
- focused refactors
- other bounded engine changes approved by the lead

The implementation worker does not own final architecture or milestone acceptance.

## Independent reviewer

Use a high-capability model at high reasoning effort, with read-only access, in a context that
has not already argued for the change.

Use this reviewer for:

- consequential engine changes
- architecture-sensitive changes
- trigger/Game Object semantics
- save-format changes
- networking/content-rule separation
- milestone completion reviews

The reviewer should challenge the implementation independently rather than assuming the lead's design was correct.

For routine low-risk changes, lead review may be sufficient without spawning a separate reviewer.

## Review flow

Consequential work should follow:

```text
lead plans
→ explorer investigates where useful
→ implementation worker implements
→ independent reviewer reviews
→ lead accepts / rejects / fixes
→ regression tests
→ integration
```

Do not accept a subagent's claim that work is complete without inspecting the changes and validation evidence.

When your harness does not support named-role spawning, reproduce the role explicitly when delegating. Concrete model bindings are per-contributor and live in your own gitignored harness directory, not in this repository.

---

# 27. First-session instructions

Before editing code:

1. Read this file and the root `AGENTS.md`.
2. Inspect the exact current repository state.
3. Record the upstream commit.
4. Revalidate the known architectural findings.
5. Identify the exact files/symbols involved in Milestone 1.
6. Propose the smallest architecture needed for world profiles.
7. List tests to add.
8. Identify likely upstream-conflict/rebase risks.
9. List any facts that require inspecting the user's real `MpWorld.dsmap`.
10. Only then begin implementation.

Do not expand scope into Party Adventure, guidance signage, deep remaster work, audio remastering, or unrelated features during Milestone 1. A small visual-baseline pass may begin only after Milestone 1 is stable.
