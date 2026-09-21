# AGENTS.md — SiegeFX Utraea Fork

## Project focus

This fork of SiegeFX is focused on making the original **Dungeon Siege 1 Utraean Peninsula** (`MpWorld.dsmap`) fully playable offline.

Upstream SiegeFX is already primarily focused on completing the **Kingdom of Ehb** single-player campaign. Avoid duplicating upstream work unless a generic engine change is required for Utraea compatibility.

Our priorities are:

1. Utraean Peninsula offline play.
2. **Solo Adventure** mode.
3. Later, **Party Adventure** mode using SiegeFX's existing party systems.
4. Full compatibility with original Utraean mechanics and secrets.
5. Optional exploration guidance:
   - Classic
   - Subtle Hints
   - Clear Directions
6. Later, visual remastering and mod support.

Do **not** begin graphics-remaster work until the Utraean gameplay foundation is reliable.

---

## Engineering principle

**Do not implement quest-specific workarounds until the corresponding original Dungeon Siege engine behavior has been identified. Prefer generic Dungeon Siege engine semantics.**

Bad:

```csharp
if (item.Template == "fury_eye")
    OpenGallusDoor();
```

Preferred:

```text
Original trigger detects the correct Game Object
→ SiegeFX evaluates the trigger correctly
→ original authored world logic opens the door
```

This principle applies especially to:

- Townstones
- Fury's Eye
- Trial of Gallus
- Chicken Level
- pressure plates
- item consumption
- secret-area triggers
- elevators and transport logic

---

## Architecture rules

Keep these concepts separate:

### World/content rules

Examples:

- Kingdom of Ehb / `World.dsmap`
- Utraean Peninsula / `MpWorld.dsmap`

### Networking

Examples:

- Offline
- LAN
- Online

Utraea is **multiplayer-authored content**, but our primary mode is **offline**.

Do not equate:

```text
multiplayer-authored content == active network session
```

The engine must be able to run Utraean multiplayer-authored triggers with networking disabled.

---

## Known compatibility concerns

These findings should be verified against the current repository before implementation, because upstream may change.

### Launching

Current SiegeFX launch paths have historically contained hard-coded Ehb/Farmhouse assumptions around:

- `World.dsmap`
- `map_world`
- Farmhouse start region

Introduce/retain a clean world-profile abstraction instead of adding more special cases.

### Saves

A save must persist the selected world explicitly.

A Utraea save must reopen against:

```text
MpWorld.dsmap
```

not infer or reconstruct `World.dsmap`.

### Trigger rules

Audit and preserve original Dungeon Siege semantics for:

- multiplayer-authored trigger rows
- compound trigger conditions
- `go_within_bounding_box`
- `go_within_sphere`
- SCID filtering
- template filtering
- Result of Condition
- world-object message routing
- item deletion/consumption

### Dropped items

Dropped items must eventually behave as first-class Dungeon Siege Game Objects where original content expects them to.

Do not treat a visual `LootPile` as the final logical object model if multiple distinct item instances can occupy one pile.

Persistent logical item identity may be required across:

```text
world → inventory → world → trigger/plate → deletion
```

---

## Utraea compatibility targets

The eventual compatibility suite should cover at least:

- Elddim start
- leaving Elddim normally
- region streaming
- Utraean world transitions
- townstone collection
- Utraean Circle
- Tenstone interactions
- Fury's Eye route
- Fury's Eye pressure plates
- Fury's Eye acquisition
- Trial of Gallus book requirement
- hidden Gallus entrance
- three Gallus offerings
- offering consumption
- Gallus platform/elevator sequence
- Chicken Level
- Volcanic Caverns / Hades
- save/reload during critical quest/puzzle states

Do not claim a feature is supported until it has been verified in normal gameplay or by an equivalent reproducible test.

---

## Adventure modes

### Solo Adventure

Goal: preserve the feel of entering the original multiplayer world alone.

Initial philosophy:

- no forced rebalance
- no network session
- one player-controlled character
- original Utraean world and authored mechanics

### Party Adventure

Add only after the original offline Utraean flow is reliable.

Use existing SiegeFX party systems where possible.

Possible later additions:

- recruitable companions
- mules
- recruit placement in Utraean towns
- optional party-aware balancing

Do not rebalance the world preemptively.

---

## Exploration guidance

The player-facing modes are:

### Classic

- no added guidance
- no added signs
- no added hints
- closest to the original exploration experience

### Subtle Hints

- lore-friendly wooden signs
- environmental hints
- general route guidance
- no HUD waypoint system

### Clear Directions

- enough physical signposting that a time-limited player can intentionally reach major optional areas
- still prefer diegetic world guidance over floating HUD markers
- some secrets, especially the Chicken Level, may retain mystery even here

Guidance content should be implemented as an overlay/additional data layer where practical rather than destructively editing the original `MpWorld.dsmap`.

The setting should be changeable during a playthrough.

---

## Upstream strategy

Keep the fork easy to rebase/merge from upstream SiegeFX.

Prefer contributing generic engine fixes upstream when practical, especially fixes for:

- Game Object semantics
- trigger evaluation
- Result of Condition
- item identity
- generic world-object messages
- save/load correctness

Keep fork-specific opinionated features separate:

- Solo Adventure UX
- Party Adventure
- recruit placement
- exploration-guidance overlays
- custom wooden signs
- remastered assets

---

## Agent workflow

The lead model is responsible for architecture, integration, and final review.

Delegate bounded work to cheaper subagents when useful, especially:

- repository searches
- call-chain tracing
- test creation
- documentation verification
- mechanical refactors
- narrowly scoped implementation

For consequential changes:

1. Map the current implementation.
2. Reproduce or encode the problem with a failing test where practical.
3. Determine original Dungeon Siege semantics.
4. Propose the smallest generic engine fix.
5. Delegate bounded implementation if useful.
6. Review the diff manually.
7. Run focused tests.
8. Run an independent review.
9. Address findings.
10. Run regression tests before integration.

Do not accept a subagent's claim that work is complete without inspecting its changes and validation evidence.

Prefer short-lived branches/worktrees for independent tasks.

---

## Copyright / original game data

Do not redistribute original Dungeon Siege assets.

The user's legally owned Dungeon Siege installation should remain an external runtime data source.

Do not rely on leaked proprietary source code.

Use clean-room engine work and the legally owned game data.

---

## Current milestone

Read `docs/UTRAEA_PROJECT.md` for the current milestone, detailed plan, known findings, and acceptance criteria.

---

## Subagent roles

Delegated work uses three declared roles. These are project contracts and hold regardless of
which agent harness you run. If your tooling supports named custom roles, declare them under
these names; otherwise reproduce the contract explicitly when delegating.

### explorer

Read-only. Repository mapping, symbol searches, call-chain tracing, test discovery,
documentation verification. Reach for it before spending lead-engineer context on mechanical
exploration. It must not modify the workspace.

### implementation

Workspace-write, scoped to a task the lead has already bounded and approved. Use it only once
the architecture and task boundaries are settled. It does not own architecture and does not
widen its own scope.

### reviewer

Read-only and genuinely independent: a fresh context that has not seen the change being
reviewed argued for. Use it for consequential engine changes and milestone acceptance. Its job
is to challenge the change, not to ratify it — an approach is not correct merely because the
lead planned it.

The lead keeps architecture, integration, and final review; see **Agent workflow** above.

### Harness bindings

How you wire these roles up — which harness, which models, which reasoning efforts — is your
own choice and is not tracked here. Harness directories (`.codex/`, `.claude/`, `.cursor/`, and
so on) are gitignored, so orchestrate however you prefer. The contracts above are what your
setup has to satisfy; the sandboxing in particular is not optional, since `explorer` and
`reviewer` are only useful if they genuinely cannot write.

Keeping bindings out of the repository also keeps this document independent of whichever models
happen to be current.
