# World randomizer item tracking

Bug found in the first gameplay smoke test of the packaged 0.16.0 app: with more item categories switched on, generation
failed after all three combined attempts with `FOG_UPSTREAM_INCOMPATIBLE: Couldn't find darkmoon_seance_ring in item lots`,
and the app told the player to try another seed. This note records the cause, the fix and the evidence.

## Why FogMod needs the item

`ItemReader.FindItems` (FogMod `6e6dc78`, `tools/fog-host/vendor/FogMod/ItemReader.cs`) is the only place that looks for a
named item by lot. For each lot listed in `fog.txt` (`LotLocations`, 440 lots; a lot also covers the rows after it until the
first row that does not exist) it reads the finished `ItemLotParam`, and for every entry whose `category:id` equals a key item
of `fog.txt` (`KeyItems`) it records the **area of that lot** as the item's area. The area is part of the logic graph: for
example `Cond: OR anorlondo_gwynevere darkmoon_seance_ring` lets the Darkmoon fog gate depend on where the ring can be picked
up (`Graph.itemAreas`, `GraphChecker`). A required key item (not `randomonly`, not `hard`) that is in none of the lots ends in
`Couldn't find <item> in item lots`. The same item in two annotated areas ends in `Item <item> found in both ...`. FogMod needs
only the **area**, but it can only derive one from an annotated lot; it has no notion of a shop, an NPC gift, an enemy drop or
the starting inventory as a place. Original FogMod therefore only supported item randomizers that move key items between its
annotated lots. Nothing else in FogMod or FogHost looks items up by lot (searched: `KeyItems`, `itemAreas`, `LotLocations`,
`ItemReader`, `FindItems` in `vendor/FogMod` and `src`), so this is one mechanism, not several look-alike bugs.

## What the combined engine did

The combined engine shuffles every item source in one global permutation: world lots, NPC gifts, enemy drops and shop
entries (`randomizeExtractedItemLots`). Only the protected goods (keys 2000-2999, the two protected rings) are fixed; the
Darkmoon Seance Ring (`2:149`) and the Peculiar Doll (`3:384`) are ordinary items for the engine, although FogMod tracks them.
With the real catalog (527 world lots, 20 gifts, 232 enemy drop lots, 391 shop entries) FogMod's annotation covers 490 world
lots, 2 gift lots, 4 drop lots and no shop entry. 200 seeds per configuration, share of seeds where the ring or the doll left
the annotated lots (so FogMod would stop):

| Item categories on (all with World items) | seeds that break Fog |
|---|---|
| World items only | 10% (20/200; an unannotated world lot) |
| + Starting class, + Starting equipment | 10% (they never hold these items) |
| + NPC gifts | 20% |
| + Enemy drops | 67% |
| + Shops | 68% |
| all of them (the failing screenshot) | 85% |

The first minimal option that turns a rare problem into a likely one is **NPC gifts**, and the smallest configuration with the
failure is **World items alone** (it only needs an unannotated world lot). Where the ring went in the failing seeds: another
world lot that FogMod does not annotate, an enemy drop lot, a shop entry, an NPC gift lot. Starting class and starting
equipment are not involved. With three combined attempts an 85% per-attempt failure rate fails 61% of the runs, and the
retries were useless for the player: the engine succeeded each time, Fog failed on the same assumption.

## A second failure of the same class: extra copies

The first end-to-end run after the fix showed that the "item left the annotated lots" case is only half of the contract. FogMod
also stops when a key item is found in two different annotated areas (`Item crest_of_artorias found in both parish_andre and
darkroot`). The game data contains deliberate extra copies: the shops sell a second Crest of Artorias (shop row 1401; FogMod
finds the first in Andre's protected drop lot, outside the shuffle), a second Residence Key (row 1105; the first is in a
protected lot) and two Cast Light (rows 2202, 6504; an item FogMod does not require). With Shops enabled the engine may
shuffle such a shop copy into a world lot FogMod annotates, which creates the second copy. Scanning all 28 key items of
`fog.txt` against the catalog found exactly these three; every other key item has one copy and is covered by the first rule.

## The fix

The combined engine now keeps the tracked items where FogMod can follow them, instead of FogHost guessing afterwards. Every
source of a tracked item gets a role in the shuffle:

| Role | Which sources | Where it may be placed |
|---|---|---|
| annotated | the single copy FogMod must find: the first lot copy of a required key item (the Seance Ring, the Peculiar Doll, ...) | only a lot FogMod annotates |
| shop | every extra copy: the shop copies above, any further copy, and any item FogMod does not require | only a shop, where FogMod does not look |

- `apps/backend/src/adapters/fogItemRules.ts` reads the rules from the same `fog.txt` FogHost uses (the verified file in the
  prepared world data): all 28 `KeyItems` (25 required) and the 440 `LotLocations` starts.
- The pipeline passes them to the combined engine as the config key `fogTrackedItems` whenever Fog is enabled
  (`apps/backend/src/core/generationPipeline.ts`).
- `randomizeExtractedItemLots` (patch `0011`) repairs the finished global shuffle: a tracked item that violates its role is
  swapped with a donor target that is fine for both items, keeping every other rule (no fixed point, DLC-restricted items,
  consumable souls in finite shops). A lot counts as annotated when it is a start row or follows one without a gap in the
  catalog's lots (a subset of what FogMod walks, so never too permissive). The RNG is used only when a swap is needed, so a
  shuffle that already satisfied the rule is unchanged: every seed that worked before keeps its exact placement. Without Fog
  (`null`) nothing changes.
- The items still move: the ring and the doll are randomized between FogMod's annotated lots, and FogMod's logic follows them
  (as before for the seeds that worked); extra copies are shuffled among the shops. Nothing is duplicated; the rule only swaps.

Item categories that remain incompatible with Fog: **none**. NPC gifts, enemy drops and shops stay fully randomized for every
other item. A model of FogMod's check on the real catalog (the lots FogMod walks, the copies outside the shuffle, "exactly one
copy of a required item, at most one of any") rejected 8% (World items), 24% (+ NPC gifts), 71% (+ enemy drops), 92% (+ shops),
93% (all categories), 99% (progression protection off) and 100% (protected items randomized) of 100 seeds each before the fix
and 0% in every one of those configurations after it.

## Retry classification

`FOG_UPSTREAM_INCOMPATIBLE` was retried with a new combined seed (up to 3 attempts). It is now **structural**: the item shuffle
already honors the rule, so a repeat means the data or configuration cannot work and a new seed would only repeat it
(`decideFogRetry`: `fail`, one attempt). `FOG_SEED_UNSOLVABLE` (another Fog seed may solve the graph) stays retryable. The
FogHost field `retryable` for this code is informational and unchanged.

## What the player sees

The old text ("try another seed") was wrong for this case. `FOG_UPSTREAM_INCOMPATIBLE` now reads "These item and world settings
cannot be combined yet. Your game was not changed. Switch off the world randomizer or some extra item categories (NPC gifts,
enemy drops, shops) and try again." The item name, the lot wording and the code stay in the run log and the manifest. After the
fix the case should not occur; no setting combination is blocked in the UI because none is incompatible.

## Evidence

- **Tests** (`apps/backend/src/adapters/fogTrackedItems.test.mjs`, 13): Fog + World items, + NPC gifts, + Enemy drops, + Shops
  and + all categories on a synthetic catalog with annotated and unannotated lots; the Seance Ring and the Doll randomized away
  from their vanilla lots yet always inside annotated lots and exactly once; the shop copies of a key item and of a
  non-required item stay in shops; no item is duplicated or lost; deterministic reruns; a seed that already satisfied the rule
  keeps its exact placement; gap handling of lot coverage; an unplaceable item fails early with a typed error; `fog.txt`
  parsing. Pipeline tests (`generationPipeline.test.mjs`): the combined engine receives the rules only when Fog is on, and
  `FOG_UPSTREAM_INCOMPATIBLE` ends after one combined attempt with a plain message and the details in the log and manifest.
- **Real catalog model** (100 seeds per configuration): rejected seeds before / after the fix: World items 8% / 0%,
  + NPC gifts 24% / 0%, + enemy drops 71% / 0%, + shops 92% / 0%, all categories 93% / 0%, progression protection off 99% / 0%,
  protected items randomized 100% / 0%.
- **Real format** (Items + regular Enemies + Bosses + Fog, every item category incl. Starting class, Starting equipment, NPC gifts,
  Enemy drops, Guaranteed enemy drops and Shops, current world defaults, FogHost 0.5.0 and the self-contained helpers):
  40 seeds, 40 generated on the first combined attempt (0 combined retries), 0 merge conflicts, the same 15 boss overlay
  events as before, `verify-skips` 0 failures in 40 combined packages, 0 mismatches in events the combined engine edited, 0 sampler
  overflows, 0 changed placements without a scaled row; one full proof (generation, byte-identical rerun, activation, restore).
  The run before the second part of the fix (extra copies) failed 20 of 20 seeds with `Item crest_of_artorias found in both
  parish_andre and darkroot`, which is how that part was found.
- **Packaged app 0.16.1**, driven through its own UI on a clean copy of the game with the same broad configuration:
  Randomize, Apply to game, Restore original game all succeed on the first attempt, with the world data prepared by the app.
