# Milestone 1 status — offline Utraea Solo Adventure

This is the short tracking list for the draft Milestone 1 PR. The detailed
investigation and validation record is in [MILESTONE_1_PLAN.md](MILESTONE_1_PLAN.md).
The fork started from upstream SiegeFX commit
`7a9cd7435b3baed56d597243def820581ab6f9eb`.

## Implemented

- [x] Select Utraean Peninsula from the normal Single Player frontend and
  start Solo Adventure fully offline at Elddim's authored start.
- [x] Load `MpWorld.dsmap` regular actor, prop, trigger, and terrain content
  without requiring a network session; keep world content rules separate from
  networking state.
- [x] Persist world and adventure identity in saves, route loads to the correct
  map, isolate quicksave/autosave sets, and validate staged saves before replacing
  a previous valid generation.
- [x] Correct Elddim's church carpet texture, roof cutaway, indoor camera,
  interaction picking, and paired-door approach/swing handling.
- [x] Correct floating labels, compass cardinals, and the church-side shop sign
  material lookup.
- [x] Restore authored idle animation for non-player actors and the dog;
  forward PRS animation SFX notes so Zabar's hammer effect plays.
- [x] Anchor Zabar's sparks at the anvil work surface and honor his placed
  `scale_base = 1` instead of the inherited blacksmith template's `0.85`.
- [x] Add headless checks for world launch, offline content, save transactions,
  world profiles, door interaction, actor picking, terrain textures, and NPC
  animation/effect behavior against the user's external game installation.

## Confirmed in normal play by the user

- [x] Utraea launches and Elddim can be entered through the normal frontend.
- [x] The church carpet appears; the roof hides indoors and returns outdoors;
  movement and talk-cursor behavior improved.
- [x] Non-player animations resumed, the compass placement was accepted, and
  Zabar's sparks now appear at the anvil.

## Still to verify or finish

- [ ] Confirm Zabar's corrected tongs/body scale visually after the latest build,
  including a complete hammering cycle and save/reload.
- [ ] Confirm the church door hover cursor, both leaf swing directions, shop
  sign, and floating labels in normal play after their fixes.
- [ ] Confirm Zabar's hammer sound in normal play.
- [ ] Complete the Milestone 1 journey: leave Elddim, cross streamed regions,
  save away from spawn, quit, reload at the same place, and verify inventory.
- [ ] Verify F5/F9 slot isolation across two adventures and recheck Ehb new
  game plus an existing Ehb save.
- [ ] Investigate movement-heavy frame pacing if it remains distracting; the
  earlier 60 FPS reading did not establish smooth animation.

Milestone 1 is **not yet accepted**. Party Adventure, LAN play, exploration
guidance, quest-specific puzzle work, and graphics remastering are later work.
Original Dungeon Siege assets stay in the user's installation and are not
included in the fork.
