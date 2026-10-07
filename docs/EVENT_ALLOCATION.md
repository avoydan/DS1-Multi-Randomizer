# Event ID allocation (randomizer-authored EMEVD events)

DsrDataTool (`patch-enemies`) adds events to the map event files (`event/mXX_XX_XX_XX.emevd.dcx`): activation, passive, combat,
contained-combat, static-bridge, visibility and disable events for placed enemies, and one fixed event (the Bed of Chaos floor).
This document says who owns which ID, how the ranges are chosen and which invariants the tool enforces. It exists because of the
failure `Randomizer event ID is already used: 11419980`.

## Namespaces

| Owner | IDs | Notes |
|---|---|---|
| Vanilla game | whatever the file already holds | never touched except by the documented edits of existing events |
| DS1TR custom window (per map) | `10_000_000 + mapNumber * 100_000 + 19_900` and the next `customEventCount - 1` IDs, moved up past used / reserved IDs | one consecutive window per event file, created by `FindFreeCustomEventRange` |
| DS1TR fixed event | the Bed of Chaos floor event, preferred `11_419_980` (map `m14_01_00_00` only) | reserved **before** the window is chosen |
| FogMod / FogHost | its own IDs inside the Fog merge; the merge reports a conflict (`FOG_MERGE_CONFLICT`) instead of overwriting | no overlap with the DS1TR windows was observed in any sweep |

Event files are per map, so the same ID in two maps is not a collision.

## How the window is built

1. `customEventCount` = placements that need an event of any kind in that map: passive + 2 x deferred (activation + watchdog) +
   contained + static bridge + visible Priscilla + disabled + force-combat (+ 1 for the Quelaag fire keeper).
2. The preferred base is `10_000_000 + mapNumber * 100_000 + 19_900`.
3. IDs that must not be used are collected: every event ID that already exists in the file **and every fixed ID this tool will add later**
   (`reserved`). The Bed of Chaos floor event ID is chosen first (`FindFreeCustomEventRange(emevd, 11_419_980, 1)`) when the map is
   `m14_01_00_00` and a placement preserves the floor.
4. `FindFreeCustomEventRange` returns the first range of `customEventCount` consecutive IDs at or after the preferred base that contains
   no used and no reserved ID. Registration (`RegisterCustomEvent`) still refuses an ID that exists (no silent overwrite).
5. After the events are registered the tool checks `IDs used <= IDs reserved` and throws otherwise. With `DS1TR_AUDIT_EVENTS=1` it prints
   `audit-events <map> count=<reserved> consumed=<used> base=<first ID> floor=<floor event or ->` for every map.

## Root cause of 11419980

The floor event sits at `11_419_980`, inside the first 100 IDs of the Lost Izalith window (`11_419_900` ... `11_419_999`). The window was
sized and placed before the floor event was added, and `FindFreeCustomEventRange` only looks at events that exist at that moment, so it
could not see the floor event. As long as Lost Izalith needed 80 or fewer custom events the window ended before `11_419_980`. With
Type replacement enabled and Placement anywhere a whole kind of enemy is replaced by a model that needs a combat-activation event, so
Lost Izalith (70 Bounding Demon slots and many more) needed 96 events: the 81st custom event tried to take `11_419_980` and registration
refused it. The reproduction (`golden-v1-boss-combined` with sampling-v1, Type replacement enabled, Placement anywhere, bosses on) showed
94 force-combat placements plus 3 sanitized boss placements in that map.

It was one instance of a general weakness (events added later with a fixed ID are invisible to the range search), but it is the only
one: every other event creation in the tool takes its ID from the window (`nextCustomEventId++`), and the floor event is the only fixed ID.

## What changed and what did not

* A window of up to 80 events in `m14_01_00_00`, and every window in every other map, is chosen exactly as before: the reserved ID does
  not intersect it, so the base and every event ID are identical. Audit runs show `consumed == count` in every map of every run, so
  the window size equals the number of events that are really written.
* A window that previously ran into the floor event (more than 80 events in `m14_01_00_00` with the floor preserved) now starts at
  `11_419_981`. Those runs used to fail in `patch-enemies`; `DATA_TOOL_FAILED` is a final result (one attempt, no reseeding), so no
  package and no Seed Code of such a world ever existed. No existing world changes: the golden fixtures, the lock hashes and the sampling
  pins are identical with the fixed tool.
* No generation profile change is needed (see `docs/GENERATION_COMPATIBILITY.md`): the change only turns a failure into a world.

## Rules for new events

* Every event a feature adds takes its ID from the window and is counted in `customEventCount`. If a feature needs a fixed ID, it must be
  added to the reserved set before the window is chosen.
* A feature that can add many events (for example one per placed enemy) must be measured: the audit prints the largest window per map.
  Part E sweeps record it (`maxEvents`, `maxEventsMap`).
* The window may grow past 100 IDs. It is protected against existing events and the reserved IDs, not against events added by another
  tool later; a Fog merge that finds an overlap fails with `FOG_MERGE_CONFLICT`, it never overwrites.

## Roaming bosses (Part E1)

A boss in a regular enemy slot adds exactly one custom event (the same wake-up event every portable boss gets, `forceCombatActivation`) inside the
map's custom event window. It edits no vanilla event and adds nothing to the Fog boss overlay (15 events, unchanged). The planner limits a map to
10 roaming bosses, which bounds the growth of the window; `DS1TR_AUDIT_EVENTS=1` reports reserved against consumed IDs and the matrix
(`placementMatrix.mjs roaming-e1`) records the highest count per map. A slot without an entity id gets one from `16099000 + index` (the range every
combat event uses).

## A regular enemy as the body of a boss encounter (Part E2)

Adds **no event and no event ID**. The slot keeps its boss entity ID, so the vanilla events of the arena keep working; the data tool applies to the event files exactly the portable-boss edits Part D
already applies for the same slots (proven by diffing the EMEVD of an E2 package against a Part D package: the Asylum Demon intro `11810310` and every event naming the entity are identical, the boss-name
rewrite is the only absent edit). Nothing is added to the custom event window, to the Fog boss overlay or to the reserved IDs. A body in the Taurus Demon or Capra Demon arena uses the wake-up
event every portable boss gets (inside the window, as in Part D). `tools/smoke-verify` checks, for the quick world, that event IDs are unique and that no condition, flag-write or event-initializer
instruction of an event naming the entity was removed (the model-specific forced animation `2003[18]` aimed at the encounter's own entity excepted: the Asylum Demon drop in `11810310` and the Taurus Demon entrance in `11015382`, both Part D's sanitization).

## Hostile NPCs (Part F1)

A hostile NPC in a live regular slot adds exactly one custom event (the wake-up every portable boss gets, `forceCombatActivation`) inside the map's custom event window, assigned deterministically like every other custom event (`16099000 + slot index` is the entity id of a slot that has none). It edits no vanilla event and adds nothing to the Fog boss overlay. A dummy part gets no event. The planner limits a map to **12** hostile NPCs
(an uncapped run measured up to 117 in one map under strict curve and 73 under Type replacement, which would have been that many events); a Type replacement group counts every slot of the group. The Darkroot Basin (`m12_00_00_01`), which has no event file of its own (the data tool writes no event for a part there), never holds one.
The cap is a DS1TR limit, not a legacy rule; the window measurements are in `ENEMY_PLACEMENT_MODEL.md` 10.9.

