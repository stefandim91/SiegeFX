# Milestone 1 status — offline Utraea Solo Adventure

This is the short tracking list for the Milestone 1 PR. The detailed
investigation and validation record is in [MILESTONE_1_PLAN.md](MILESTONE_1_PLAN.md).
The fork started from upstream SiegeFX commit
`7a9cd7435b3baed56d597243def820581ab6f9eb`.

Scope: Utraea as an offline Solo Adventure, plus world-aware save and load.
Gameplay polish found while playtesting (doors, picking, NPC animation, terrain
and prop textures, compass, elevators and levers) is tracked as separate work.

## Implemented

- [x] Select Utraean Peninsula from the normal Single Player frontend and
  start Solo Adventure fully offline at Elddim's authored start.
- [x] Load `MpWorld.dsmap` regular actor, prop and trigger content without
  requiring a network session; keep world content rules separate from
  networking state.
- [x] Persist world and adventure identity in saves, route loads to the correct
  map, isolate quicksave/autosave sets, and validate staged saves before replacing
  a previous valid generation.
- [x] Add headless checks for world launch, offline content, save transactions
  and world profiles against the user's external game installation.

## Confirmed in normal play by the user

- [x] Utraea launches and Elddim can be entered through the normal frontend.

## Still to verify

- [ ] Complete the Milestone 1 journey: leave Elddim, cross streamed regions,
  save away from spawn, quit, reload at the same place, and verify inventory.
- [ ] Verify F5/F9 slot isolation across two adventures and recheck Ehb new
  game plus an existing Ehb save.

Milestone 1 is **not yet accepted**. Party Adventure, LAN play, exploration
guidance, quest-specific puzzle work, and graphics remastering are later work.
Original Dungeon Siege assets stay in the user's installation and are not
included in the fork.
