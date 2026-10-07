# Enemy placement model

Design for the next legacy-parity group: Replacement chance, Boss / Normal enemy / NPC replacement
modes, Boss chance, Placement fit, Difficulty mode and strictness, Type replacement, and the options that
depend on the same machinery (Unique bosses, Roaming boss souls, Respawning roaming bosses, Hostile NPCs,
Custom enemy pool).

**Recommendation in short.** Add a second, explicit placement algorithm (`sampling-v1`) beside the existing
count-preserving permutation, which stays frozen as `permutation-v1` for every old preset. The new core
decides per slot (chance, category, fit, difficulty, exclusions), draws with replacement from global pools like
the legacy tool, enforces the 30-model map limit by construction, and falls back in a fixed bounded order.
Implement it in small batches, selection-only options first and the event-touching ones (bosses in normal
slots, NPCs) last.

Status: **design, the Part 0 infrastructure (10.1), Part A (10.2) and Part B difficulty (10.3), Part C1 Type replacement (10.4) and Part C2 custom enemy pool (10.5) and Part D boss sampling for single-body bosses (10.6).** `sampling-v1` runs for regular enemy slots
only and is opt-in; `permutation-v1` stays the default. **Part A is complete: the whole replacement-chance range works under a
non-worsening model budget (10.2).** One scratch prototype
(`engines/combined/prototypes/enemy-placement-sampler.mjs`) validates the proposed core against a real
catalog; its figures are quoted in section 11 and it is not part of any build, test run or package.

Legacy sources are the pinned Enemy Randomizer revision `83f008d` (`randomizer_rng.py`, `randomizer.py`,
`event_tools.py`, `NpcParam.py`, `dist/enemyRandomizerData/replacement_ref/*.txt`,
`eventTemplates.dkscript`). They have no license; they were **read** to reconstruct behavior and are not
copied. Every table in section 5 that the legacy tool curates by hand (sizes, difficulty classes, NPC and
source lists, the Easy Asylum lists) must be authored independently for DS1TR (section 12).

---

## 1. Legacy behavior reconstruction

### 1.1 Data model

| Concept | Legacy representation |
|---|---|
| Destination slot | One row of `original_enemies_ref/<map>.txt` (model + index, size class 0-5), in MSB order. Eligible only if its model is in `valid_replacements.txt` (125 models: type 0 normal 87, type 1 boss 31, type 2 NPC 5, type 3 2; difficulty class 0-7) and no special case skips it. Type can be overridden in code (Taurus becomes a boss, Pinwheel main/clones, Demon Ruins Capras normal, Blacksmith Giant and Eingyi NPC). |
| Source | One row of `valid_new.txt` (123 models; 98 usable, 24 ignored, 1 DLC-only): type 0/1/2, size class, difficulty class, one or more (AI, NpcParam) variants. Pools are pre-built per type x size (<= size) x difficulty. A custom pool is just another file with different "ignored" flags. |
| Per-map state | `uniqueIndices`, `uniqueNormals[size]`, `uniqueBosses[size]` (the new types introduced in this map so far), `typeReplaceMap` (original model -> chosen new type), reset for every map. |
| Limit | `MAX_UNIQUE` = 30 new types per map, 50 for Blighttown on Remastered (24 on PTDE). Counts **new** types only; original models stay in the MSB model list. |
| RNG | Python's global `random`, seeded once from the seed string, shared by everything and consumed in map order, slot order. |

### 1.2 Per-slot pipeline (`randomize()`)

For every eligible slot, in order:

1. **Replace chance.** `randint(1,100) <= replaceChance`, drawn for every eligible slot first, whatever its
   type and whatever its category mode is. If it fails the slot is "not replaced" (sentinel -2).
2. **Max size.** `fit == 0`: the slot's own size class; otherwise 5. (An NPC target gets 5 unless
   `fit == 2`, which gives it its own size back. A Mimic in the DLC map always uses its own size.)
3. **Category by slot type and mode.**
   - Normal slot, `enemyMode`: 0 none, 1 bosses only, 2 normals only, 3 both (boss with probability
     "boss chance: normal slots", else normal).
   - Boss slot, `bossMode`: same four values; "both" uses "boss chance: boss slots".
   - NPC slot, `npcMode`: same four values, "both" uses the normal-slot chance. NPCs always use difficulty
     mode 2 (fully random). Andre (`c2640`) additionally becomes Gwyndolin with 40% probability when the
     mode allows bosses.
4. **Candidate list.** Size-filtered pool of the category (`validSizeNormal[maxSize]` / `validSizeBosses`).
   In the curve modes (1 and 4) `getDifficultyList` first draws a class around the slot's own class (offset
   table by strictness; classes outside 0-7 are skipped and their mass falls back to the own class) and uses
   that class's list, falling back to the own class's list, and to "no candidate" if that is empty.
5. **Per-map type cap.** If fewer than `MAX_UNIQUE` new types exist in the map the draw is from the global
   list (step 4). **Once the cap is reached the draw is from the types already used in the map, filtered by
   size only: the difficulty filter lapses for the rest of the map.** Boss targets are exempt from the cap.
6. **Draw with rejection.** Uniform choice over *types* (not weighted by how often a type occurs in the
   game). A candidate is rejected and removed from the list when `isCombinationInvalid` says so, until the
   list is empty (`-4`). Rejections: hard-coded pairs that clip or soft-lock (Gwyndolin, Capra, Iron Golem,
   Stray Demon, Pinwheel main, Ceaseless Discharge, Tree...), Gwyn nerf (probabilistic, consuming RNG per
   evaluation), the 65% rejection of the hostile Merchant, prevent-same (`newID in oldID`), and type
   replacement injectivity (a new type already chosen for another original type in this map).
7. **Unique bosses** (`attemptUniqueBosses`): when a boss replaces a boss, already used bosses are rejected
   until 22 have been used (then the list resets) or only one candidate remains.
8. **Easy Asylum** (difficulty modes 3 and 4): twelve hard-coded Asylum slots take a random pick from
   hard-coded index lists, overriding the draw.
9. **Type replacement** (`typeReplacement` 0 or 2): if the original model of a non-boss slot already has an
   entry in the map's `typeReplaceMap` the result is that entry. Steps 2-8 still run and consume RNG, and
   their result is then overwritten, so fit, pair rejections and Easy Asylum are **bypassed**. Mode 2
   ("except roaming bosses") does not store or reuse entries whose new type is a boss.
10. **Commit.** New type enters the per-map sets (only when the target is not a boss); variant (AI/param)
    drawn uniformly; a boss placed in a normal slot gets `param + 50` (a "with souls" NpcParam row built by
    `applyBossSouls`); idle animation (T-pose off), position and tail, scripted item lot, respawn-event and
    allegiance decorations are applied.
11. **Failure.** `-6` (empty difficulty list) and `-4` (all rejected) leave the slot vanilla and log it. The
    vanilla model is not counted against the cap.

### 1.3 The options in scope

Config-string position, GUI label, default (`RestoreDefaultSettings`
`1/-/2/-/0/-/1/-/0/-/3/-/100/-/10/-/90/-/1/-/1/-/0/-/50/-/1/-/1/-/1/-/1/-/0/-/0/-/0/-/1`):

| # | Option | Values (legacy integer: meaning) | Default | What it changes |
|---|---|---|---|---|
| 0 | Boss replacement | 0 don't, 1 only with bosses, 2 only with normal enemies, 3 either | **1** | Category for boss slots (step 3). |
| 1 | Normal enemy replacement | same | **2** | Category for normal slots. |
| 2 | NPC replacement | same | **0** | Category for NPC slots; also hostile/size rules above. |
| 4 | Placement | 0 only where they fit, 1 anywhere, 2 anywhere except NPCs | **0** | Max size class (step 2). |
| 5 | Difficulty mode | 1 curve, 2 fully random, 3 random + easy Asylum, 4 curve + easy Asylum | **3** | Curve: class draw (step 4). Modes 3 and 4: Easy Asylum override (step 8). |
| 6 | Replacement chance | slider 0-100 | **100** | Step 1. |
| 7 | Boss chance, normal slots | slider 0-100 | **10** | "Both" mode for normal and NPC slots. |
| 8 | Boss chance, boss slots | slider 0-100 | **90** | "Both" mode for boss slots. |
| 10 | Difficulty strictness | 0 strict, 1 a bit loose, 2 very loose | **1** | Offset table of `getDifficultyList` (curve modes only). |
| 14 | Type replacement | 0 enabled, 1 disabled, 2 enabled except roaming bosses | **1** | Step 9, plus injectivity in step 6. |
| 17 | Unique bosses | 0 off, 1 on | **0** | Step 7. |
| 12 | Roaming boss souls | slider 0-100 | **50** | `applyBossSouls`: adds an NpcParam row (id + 50) per boss with `getSoul` = percent of the original; used for bosses placed in normal slots. |
| 18 | Respawning roaming bosses | 0 respawn, 1 stay dead | **0** | `AddRespawnEventInit` per such entity (EMEVD) when set to 1. |
| 19 | Hostile NPCs | 0 off, 1 on | **0** | Adds the four NPC models (Merchant, Andre, Vamos, Gough) to the **normal** source pool and sets their allegiance by event. |
| - | Custom enemy pool | named config | Default | Replaces `valid_new.txt` by a per-model enable list. |

Options already implemented in DS1TR (Prevent same, Gwyn nerf, Pinwheel Chaos, mimics, mosquitoes, second
Gargoyle) are the same pipeline's steps 6 and 2 and special cases; they are covered in section 8.

### 1.4 Properties that matter for the design

- **No conservation.** Sources are drawn from a catalog with replacement. The number of Hollows after a
  run is unrelated to the number before. Sampling is uniform over types, so rare types are as likely as
  common ones.
- **Draw count is data dependent.** Each rejection, each Gwyn evaluation, each variant pick and the
  animation pick consume draws from one shared stream. Toggling any option shifts every later draw. Output
  for a seed is therefore not reproducible except by running the same code; only the *distribution* is a
  meaningful target.
- **Order dependent state.** The per-map type cap is first come, first served in MSB order, so early slots
  get the full global pool and late slots the used set.
- **Quirks that need a decision, not a copy:**
  - The difficulty filter lapses once the cap is reached (step 5).
  - Type replacement stores the sentinel of an unreplaced first slot (`-2`, `-1`) in the map, so every
    later slot of that original type in the map is also left alone, and in "except roaming bosses" mode
    the sentinel indexes the source list from the end. The first effect is a coherent rule ("a type is
    replaced or left alone as a whole"); the second is undefined behavior.
  - Reused type entries bypass fit, pair rejections and Easy Asylum.
  - `random.randint(0,100)` has 101 outcomes against percentages that sum to 100, a one-in-101 skew.
- **Ambiguous in the pinned source:** whether "boss" and "normal" pools are meant to include variants
  flagged ignored under custom pools (the code path exists, the intent is not stated); how Eingyi and the
  Fair Lady (type 3) behave beyond the type override; the actual in-game consequence of a model list that
  keeps unused originals. These are listed in section 13, not guessed.

---

## 2. Current engine model

Vendored combined core (`engines/combined/vendor/src/core/generator.js`), data from `DsrDataTool scan`
(schema 17: 2,192 enemy slots, 35 boss slots, 600 archetypes, 18 maps).

### 2.1 Classification and pools

- **Boss:** `effectiveBossSlots` (catalog boss slots minus non-encounter slots, plus extra boss models and
  slots, plus the Bed of Chaos core). Planned by `randomizeExtractedBosses`.
- **Linked and dragon units** (Priscilla, painted and valley undead dragons, Hellkite, Titanite Demons,
  drakes, hydras, Bell Gargoyles, Sanctuary Guardian, Centipede Demon, Four Kings, Bed of Chaos, Mass of
  Souls, Anor Londo Gargoyles): `buildDragonPlan` / `buildMassOfSoulsPlan`. Whole units swap with whole
  units (`shuffledWithoutFixedPoints` over units), with disabled bodies and synthetic parts where the
  counts differ. These slots are *reserved*: they never enter the regular pool.
- **Regular enemy:** a slot in maps `m10`-`m18` that is not a boss, not reserved, has `teamType` 0, a model
  starting with `c`, positive NpcParam, think-param and HP, and is not a dragon body, linked part or
  helper model (and, since the parity batches, not a Mimic with Replace mimics off, not one of the six
  respawning mosquitoes with that option off). 1,703 slots of 86 models on the real catalog.
- **NPC:** friendly and neutral slots (`teamType` 1, 2 and 7: 262 on the real catalog, plus 10 with team -1) are never touched.
- **Special entities:** passive-until-attacked slots (Asylum, New Londo, Parish bonfire), the New Londo
  ghost anchors, the Darkroot butterfly path, the static bridge dragon, Taurus and other staged bosses are
  decorations of a placement keyed by slot or source model (`enemyPlacement` extras), not part of the
  assignment.

### 2.2 The assignment

1. `shuffledWithoutFixedPoints(rng, slots)`: one global shuffle of all regular slots (32 attempts, then a
   rotation). Every slot receives some *other slot's occupant*: a **bijection** over the regular slots.
2. `limitUniqueEnemyModelsByMap` repairs maps whose final model set exceeds `MAX_ENEMY_MODELS_PER_MAP`
   (30) by swapping sources across maps (up to 200 passes, throws if it cannot). The count includes the
   **fixed** models of reserved and special slots, because `DsrDataTool` prunes unused models from the MSB
   model list ("can corrupt the spatial/physics update when a dense map is loaded").
3. Optional passes, each from its own stream: `separateSameModelSources` (Prevent same, <= 8 passes, one swap
   per offending slot per pass, throws `PREVENT_SAME_ENEMY_*`), Pinwheel Chaos clone sources drawn *with
   repetition*, the Gwyn nerf (boss archetype excluded from the boss candidates with the legacy probability),
   Second Gargoyle (independent boss draw for one body).
4. Placements carry `scaledNpcParamId = 9,100,000 + index`; `scaling` per placement (the Pinwheel clones
   override it to `vanilla`).

### 2.3 RNG and determinism

`createStream(seed, name, version)` derives a 32-bit state from `sha256(version \0 seed \0 name)` and runs a
small deterministic generator. Streams in use: `enemies`, `dragons`, `linked-bosses`, `bosses`, and the
option streams (`prevent-same-enemy`, `pinwheel-chaos`, `gwyn-nerf`, `second-gargoyle`, `soul-items`,
`armor-sets`, `starting-*`, `items`, `progression`). `version` is `RANDOMIZER_VERSION` (0.5.66); changing it
reshuffles every stream, which is why it is frozen. The product seed reaches the core as a 32-character
derived seed per combined attempt (`combinedAttempt`), so a key-item retry re-runs the whole core with a new
seed.

### 2.4 Statistics on the real catalog

- Regular slots 1,703; 86 original models; 54 models occur >= 10 times; the most common (`c2500`) 145 times
  (8.5%). The permutation preserves exactly this multiset.
- Vanilla distinct regular models per map: 3 to 17.
- The per-slot file contains no size or difficulty class and no NPC/boss "mode"; the engine behaves like
  "placement fit: anywhere" and "difficulty: fully random" with conservation.

### 2.5 Assumptions that partial or probabilistic replacement breaks

| Assumption | Where it is used | Broken by |
|---|---|---|
| Sources = targets (bijection) | the shuffle, fixed-point avoidance, the limiter's swap repair, Prevent same repair | chance < 100 (only a subset participates), any mode that sends a boss into a normal slot or the reverse (the displaced enemy has nowhere to go), duplicates by design |
| Count of each model is conserved | the 30-model accounting (derived from the permutation) | sampling with replacement |
| One global order | `index`-based ids, deterministic repair | per-slot decisions that skip slots |
| Only enemies of one class move | `fixedModelsByMap` is computed from *finished* special plans | category modes that mix bosses into the regular pool |
| Boss slots only receive bosses | boss-bar naming, boss events measured on the 15-event overlay | boss mode 2/3 (normal enemy in a boss slot) |
| Whole units swap | linked and dragon planners | a chance or mode that would replace one body of a unit |

---

## 3. Architecture alternatives

Criteria: fidelity to legacy behavior, determinism, old seeds staying intact, replacement chance, category
modes, fit and difficulty, Prevent same / Unique bosses / custom pool / Type replacement, linked bodies and
the existing special options, the 30-model budget, Fog and DsrDataTool compatibility, testability, effort and
maintenance.

### A. Keep the global permutation and emulate options around it

- Chance: permute only the participating subset (bijection over the subset). Modes: partition by class.
  Fit and difficulty: constrained *assignment* (bipartite matching with costs) instead of sampling.
- F: **poor.** Conserves the multiset, so frequency follows vanilla instead of uniform-by-type; cannot
  produce duplicates of rare types or bosses in normal slots; fit and difficulty become a global matching
  problem whose solution distribution is not the legacy sequential one. Cross-class modes are impossible
  without breaking the bijection.
- S: perfect (unchanged). D: good. C: ok. M: **not possible** (normal-to-boss needs duplicates). P: hard (matching,
  weights). X: unique bosses ok, pool ok, type replacement awkward. L/B/O: good (today's behavior).
  T: matching is hard to test for distribution. E: high and still incomplete.

### B. Replace the placement core with a slot-by-slot planner (no permutation)

- F: **good.** Same structure as the legacy loop. S: **breaks every old seed's enemy layout** unless the
  old algorithm is also kept. D: good if streams are designed. C/M/P/X: all natural. L: needs per-unit
  participation. B: needs the budget redesigned (first-come draws can paint a map into a corner, see 11).
  O: placements keep the same schema; duplicates and unconserved counts are already accepted by DsrDataTool
  (Pinwheel clones, Second Gargoyle). T: good (per-slot rules unit-testable). E: moderate.
- Risk: if the permutation is *deleted*, old presets cannot reproduce their output.

### C. Hybrid: global eligible pools, per-slot decisions, assignment by matching where possible

- Decide participation and category per slot, then solve fit, difficulty and budget as one matching.
- F: partial (matching is not the legacy sequential distribution), D: good, S: only for the unchanged path,
  C/M: natural, P: good for constraints but changes the distribution, B: good (global view),
  X: unique bosses natural, type replacement is awkward, L: ok. T: moderate. E: **highest**; a new solver.

### D. A new sampling core beside a frozen permutation core (recommended)

- Keep `permutation-v1` byte-for-byte as the algorithm of existing presets. Add `sampling-v1`: a legacy-style
  slot-by-slot planner (alternative B) with a per-slot RNG stream, derived size and difficulty classes, a
  per-map model set enforced by construction, and a bounded fallback ladder. Shared: catalog, special
  planners (dragons, linked units, Mass of Souls), placement decoration (`enemyPlacement` extras), the
  placement schema, DsrDataTool, FogHost.
- F: **good** (the legacy loop, with named deviations in section 5). S: **perfect for old presets** (the old
  code path does not run differently). D: good. C/M/P/X: natural. L: unit planners get participation per unit
  and stay swaps. B: by construction (section 6.6). O: unchanged schema. T: good. E: moderate, spread over
  small batches; cost is two cores, mitigated by freezing the old one (no further edits) and sharing
  everything downstream.

### Variant considered and rejected: palette-first

Choose each map's type set up front, then fill slots from it. It would guarantee the budget trivially, but it
changes the distribution (types are no longer introduced by chance of being drawn) and is not what the
legacy tool does. The only part kept is a minimal **coverage reserve** (section 6.6).

| | A emulate | B replace | C hybrid solver | **D new core + frozen old** |
|---|---|---|---|---|
| Fidelity | poor | good | partial | **good** |
| Old seeds | intact | broken | intact only if old kept | **intact** |
| Chance | partial | yes | yes | **yes** |
| Boss/normal/NPC modes | no | yes | yes | **yes** |
| Fit and difficulty | hard | natural | solver | **natural** |
| Prevent same / unique / pool / type | partial | yes | partial | **yes** |
| Linked bodies, Gwyn, Pinwheel, Gargoyle | as today | rework | rework | **units unchanged; options re-expressed** |
| Budget <= 30 | as today | needs design | solver | **by construction** |
| Fog / DsrDataTool | proven | same schema | same schema | **same schema** |
| Complexity | high, incomplete | moderate | highest | **moderate** |
| Test | hard | good | moderate | **good** |
| Migration risk | none | high | medium | **low (old path frozen)** |
| Maintenance | one core | one core | one core + solver | **two cores, one frozen** |

---

## 4. Chosen architecture

**D.** `enemies.placement.algorithm` is explicit and separate from every product version:

- `permutation` = `permutation-v1`: today's code, frozen. Existing presets, `RANDOMIZER_VERSION` 0.5.66 and
  all hash locks stay valid.
- `sampling` = `sampling-v1`: the new core; identified by its own stream version string
  `enemy-placement-v1` (section 7), not by `RANDOMIZER_VERSION`.

Count-preserving permutation **survives only as `permutation-v1`**. It is not part of sampling. This is a
deliberate fidelity decision (section 5, question 4): the legacy tool does not conserve counts, and the
option group cannot be expressed on a bijection.

Which algorithm is the **default for new presets** is a product decision (section 13). Recommendation:
ship `sampling` as opt-in until the whole group reaches parity and has been swept, then flip the default
in its own change with a documented preset migration.

---

## 5. Legacy fidelity decisions

| # | Question | Decision |
|---|---|---|
| 1 | Replacement chance per slot, independent, exactly like legacy? | **Yes.** One draw per eligible slot, the first draw of that slot's own stream, `int(100)+1 <= chance` (no 1-in-101 skew). Drawn for every eligible slot regardless of category. With type replacement, the draw is made once per (map, original type) by the first slot of that type, which is what the legacy sentinel behavior amounts to. |
| 2 | Unreplaced slot stays exactly vanilla? | **Yes**: no placement is emitted. Its vanilla model is counted in the map's model set from the start. |
| 3 | Source reused multiple times? | **Yes**, unlimited (a type may fill any number of slots, and a boss may appear in several places), except where Unique bosses is on (cycle of the boss pool, bounded) and where the 30-model set limits new types. DsrDataTool accepts repeated sources (proven by the Pinwheel clones and the Second Gargoyle sweeps). |
| 4 | Preserve per-model totals? | **No** (legacy does not). Sampling is uniform over *types*; the permutation remains for users who want conservation. |
| 5 | Boss replacement samples bosses with replacement? | **Yes**, from the portable single-body boss pool, uniform over types; repeats allowed; Unique bosses cycles. |
| 6 | Linked and multi-body bosses? | They are **units**, not sources. A unit slot participates as a whole (one chance draw per unit, from the stream of its first body); participating units swap with other participating units as today (`shuffledWithoutFixedPoints` over the participants, a unit that is alone keeps itself). Units are not sampled into normal slots. Supporting "a multi-body boss as a lone source" needs synthetic parts and is out of scope. |
| 7 | Prevent same with partial replacement? | A candidate is rejected when its model equals the slot's model. A slot that is not replaced is, by definition, unchanged and not subject to the rule, so the keep-vanilla fallback stays available under Prevent same (the legacy outcome when every candidate is rejected). |
| 8 | Fit? | `maxSize` per slot as in step 2 above, against **derived** size classes (section 6.2). NPC rule as legacy (fit 2 = anywhere except NPCs). |
| 9 | Difficulty mode and strictness? | Curve modes draw a class offset (authored tables, section 6.3) around the slot's derived class; empty class falls back to the own class, then to the ladder. **Deviation D-1:** the class filter is *kept inside the full model set* (falling back to size only when empty); the literal legacy lapse measured 30-31% same-class placements in the full phase against 72-77% with the refinement, and with DS1TR's stricter budget the full phase covers 56-63% of placements. Easy Asylum: an independently authored rule (section 12). |
| 10 | No valid replacement? | The deterministic, bounded ladder of section 6.7. Never an unbounded retry. |

Further deviations, all named in the option documentation when each batch ships:

- **D-2 budget definition.** DS1TR counts every model present in the map after the run (fixed, unchanged and
  placed), cap 30, because DsrDataTool prunes unused models. Legacy counts only newly introduced types
  (30; 50 for Remastered Blighttown). DS1TR's rule is stricter. Whether Blighttown may exceed 30 is open.
- **D-3 type-level participation** for Type replacement (above), and reuse of a type entry still respects
  fit and the hard pair rejections (legacy bypasses them). Built in Part C1 (10.4), with the injectivity relaxation described there.
- **D-4 per-slot streams** instead of one shared stream (section 7).
- **D-5 derived classes** instead of curated tables; **D-6** NPC and hostile-NPC allowlists authored here.
- **D-7 roaming boss souls and respawn** become properties of the placement's scaled NpcParam row (and, if
  needed, an event) instead of a separate "+50" parameter family (section 9).

---

## 6. Target algorithm (`sampling-v1`)

### 6.1 Inputs and data structures

```
Catalog view (derived once, deterministic, hashed as classTableHash and recorded in the manifest)
  SourceType { model, variants[(npcParam, thinkParam, ai)], class: normal|boss|npc,
               size 0-5, difficulty 0-7, flags: { portable, hostileNpc, ignored } }
  Slot       { id, map, model, class: normal|boss|npc, size, difficulty, order, unit? }
  mapOrder   = sorted map ids;  slot order = sorted slot ids (never catalog order)

Options (enemies.placement), all validated, all deterministic
  algorithm, replacementChance, normalReplacement, bossReplacement, npcReplacement,
  bossChanceInNormalSlots, bossChanceInBossSlots, fit, difficultyMode, easyAsylum,
  strictness, typeReplacement, uniqueBosses, ... (existing flat options stay where they are)

Per-map state: modelSet (Set), typeMap (Map original -> SourceType | KEEP), usedBosses (cycle list)
```

### 6.2 Classes (derived from game data)

- **Class:** boss = `effectiveBossSlots`; NPC = allowlist (section 12); everything else eligible = normal.
- **Size 0-5** from the capsule: `footprint = max(2 * hitRadius, hitHeight)`, edges 0.9 / 1.7 / 2.6 / 4.2 / 7.
  Evaluation (not tuning) against the legacy classes on the 82 shared normal models: rank correlation
  **0.74**, 79 of 82 within one class. Class 4 is empty in the regular pool (no footprint between 4.2 and 7
  among regular enemies); the edges are an open tuning item.
- **Difficulty 0-7** = `0.6 * native-area progression + 0.4 * HP rank`, where native area is the median of an
  authored map progression table. Evaluation against the legacy classes: rank correlation **0.58**, 73 of 82
  within two classes; known disagreements are special cases (a flying Batwing Demon, a Mimic). Weights and the
  table were **not** tuned against the legacy classes (that would amount to copying them); a small curated
  override table is an open item.

### 6.3 Pipeline per destination slot

```
for map in mapOrder:                               # sorted ids
  stream(slot) = createStream(seed, "slot:" + slot.id, "enemy-placement-v1")
  P1  participates(slot) = stream(slot).int(100) + 1 <= replacementChance        # first draw, always
      (type replacement: decided once per (map, original type) by the first slot of the type)
  P2  modelSet = fixedModels(map) ∪ vanilla models of non-participants ∪ coverage reserve (6.6)
  for slot in map sorted by id where participates:
    P3  category = f(slot.class, mode, bossChance)          # one extra draw only for "both"
        none -> keep vanilla (no placement)
    P4  pool = types of the category with size <= maxSize(slot, fit)         # NPC rule included
    P5  if modelSet.size < cap:  pool = difficulty-filtered pool            # curve modes (6.4)
        else:                    pool = modelSet ∩ pool, class filter kept if it has a candidate (D-1)
    P6  draw by bounded rejection (6.5); a typeMap entry is used first when still valid for P4 and P6
    P7  if no candidate: fallback ladder (6.7)
    P8  commit: modelSet ∪= {chosen}; typeMap; variant = uniform among the type's variants
        (stream of the slot); placement = existing enemyPlacement(...) with the usual extras,
        ALWAYS with a scaled NpcParam row (6.9)
  post-condition: |final models of the map| <= cap, else typed error (should be unreachable)
```

### 6.4 Difficulty

Curve modes draw `offset` from the strictness table (an authored distribution; the sum below 100 stays on
the slot's own class). Strict: +2 2%, +1 12%, -1 5%. Loose: +4 2%, +3 3%, +2 4%, +1 15%, -1 5%, -2 2%, -3 1%.
Very loose: +5 2%, +4 3%, +3 5%, +2 8%, +1 12%, -1 6%, -2 4%, -3 2%, -4 1%. Classes outside 0-7 are skipped.
These are starting values to tune against play, not legacy data. **Superseded by 10.3**: the tables actually used (and the reach of very loose up to +7) are listed there.

### 6.5 Rejection and exclusions

Bounded rejection with list shrink (at most `|pool|` iterations): pair exclusions (an authored safety table
with the engine's existing rules, for example Capra or Gwyndolin never receiving a dragon-scale boss),
Prevent same, type-replacement injectivity, Gwyn nerf **per candidate** with the legacy probabilities (this
replaces the per-seed exclusion of `permutation-v1`; the draw is from the slot's stream), Unique bosses (a
used boss is rejected while the cycle has other candidates), custom pool (filter applied when the pool is
built, not per draw), and the existing option exclusions (Mimics, mosquitoes, Pinwheel Chaos clones as
slots with their own `scaling: vanilla`).

### 6.6 Budget: an invariant with a checked precondition

> **Superseded in part (Part A).** The cap of 30 below is the *target*, not an unconditional whole-map invariant. sampling-v1 uses a non-worsening model-budget policy. The normal target is 30 distinct models per map. If out-of-scope/fixed plans already require more than 30, that unavoidable baseline becomes the effective cap. The regular sampler may preserve or reduce that overflow but may not increase it. See 10.2. The text that follows describes the coverage reserve, which still applies (trimmed to the effective cap).

The first-come cap alone painted a map into a corner in the prototype: a late small slot found the set full
of large types and no vanilla model to fall back on (0.13% of placements ended above their size class, every
time with "keep vanilla" blocked by the budget). The remedy is a **coverage reserve**, defined as an invariant
rather than as an unconditional addition:

- **Reserve.** For each slot class present among a map's participating slots (normal, boss, NPC), the vanilla
  model of the smallest participating slot of that class (two distinct ones under Prevent same, when the map
  has them) belongs to the model set.
- **Invariant.** After the reserve, every participating slot has an in-set type of its own class that fits it
  (size <= the slot's), and the set never exceeds the cap.
- **Precondition, checked per map before sampling:** `|fixed models + vanilla models of non-participants +
  reserve| <= cap`. The fixed models are those of the unit planners and special slots, which are known
  before sampling.
- **If the precondition fails** (deterministic, in this order): (a) a reserve entry is skipped when the set
  already holds a fitting type of that class; (b) otherwise the smallest participating slots of the class are
  demoted to non-participants (they keep their vanilla model, which only helps because it is a vanilla model
  of the map) until the precondition holds; (c) if the set is over the cap with **no** participant left, the
  special plans alone exceed the cap and the run fails with `ENEMY_PLACEMENT_BUDGET_UNSATISFIABLE` (map,
  models). `permutation-v1` fails in the same situation (its limiter throws), so this is not a new failure
  mode.
- **Prevent same** with a single distinct model at a class's minimum size: the slot holding that model cannot
  be replaced by a different fitting type and stays vanilla (rung 2 below). That is the legacy outcome and is
  recorded, not an error.

Measured on the real catalog (normal slots): 0 violations over 68,120 placements per variant (section 11).

### 6.7 Fallback ladder (deterministic, bounded), with category semantics

The requested **category** (normal, boss) comes from step P3 and is part of every rung. A slot's **own class**
is what it is in the game (normal slot, boss slot, NPC slot).

1. **Sampled candidate** of the requested category (P6).
2. **Category fallback**, only where the legacy tool has one: in a "both" mode, a failed boss draw is retried
   once as a normal draw (the legacy `canBeNormal`). In "bosses only" or "normals only" modes there is none.
3. **Keep the slot vanilla** (the legacy outcome), if its model is already in the set or the set has room
   (allowed under Prevent same). Recorded as `kept-vanilla`.
4. **Downgrade to the slot's own class:** a type of the slot's own class already in the set that fits it. A
   normal slot that asked for a boss when the full set holds no boss therefore receives an in-set normal enemy,
   not a boss and not a vacancy. Recorded as `in-set-fit`, plus `category-downgraded` when the category
   changed. Boss slots only use in-set bosses; **NPC slots are never relaxed or downgraded** (they stop at rung 3).
5. **(Removed in Part A.)** ~~Fit relaxed~~: it would place a type larger than the slot, breaking the fit invariant; a slot with no
   fitting type is now rung 6, the retryable `ENEMY_PLACEMENT_INFEASIBLE` (10.2). The original text: any in-set type of the slot's own class. Recorded as `fit-relaxed` with
   slot, slot size and chosen size. Unreachable with the reserve; counted in the manifest so a sweep can
   assert zero.
6. Otherwise `ENEMY_PLACEMENT_INFEASIBLE` (map, slot, class, options), a combined-stage retryable failure like
   the existing key-item retry (new combined seed, bounded attempts).

Every rung is a bounded scan over a list; nothing retries with fresh randomness.

### 6.8 Diagnostics

`randomizer.json` gains a `placementDiagnostics` block (not part of the placement hash): per-rung counts,
per-map model-set size, the list of fallback slots, the `classTableHash`, `algorithm` and option summary;
the spoiler gets a one-line summary and the manifest records the counts. A `kept-vanilla` or `in-set-fit`
count above a threshold is logged as a warning.

### 6.9 Rule: drops and team always follow the slot

**Every changed `sampling-v1` placement carries a scaled NpcParam row, whatever the global scaling mode.**
DsrDataTool copies the destination slot's item-lot fields and team onto the scaled row; without a row the
placed enemy keeps its own NpcParam and with it its own drops. `enemyPlacement` currently leaves the row out
when scaling is `vanilla`, drops are not randomized and no team or tangibility override applies, so the planner
must force it (an `extra` override; the per-placement `scaling: "vanilla"` already means "keep the
replacement's own combat stats, still take the slot's drops and team", as the Pinwheel clones use it).

Why this is a requirement and not a nicety (measured, section 11.2): a sampled type is placed any number of
times, so a drop that exists once in the game would be duplicated, and a drop of a type that is never drawn
would vanish. The FogMod "found in both areas" check is not the concern: none of FogMod's 27 tracked items is
reachable through an NpcParam drop lot (they are in world and gift lots; boss souls and lord souls are awarded
by events). The concern is item integrity.

---

## 7. RNG, versioning and compatibility

- **New streams only.** All sampling draws use `createStream(seed, name, "enemy-placement-v1")`. The version
  argument is a string unrelated to `RANDOMIZER_VERSION`, so the vendored version stays 0.5.66 and no old
  stream moves.
- **Per-slot streams** (`slot:<id>`, `unit:<id>`, plus `enemy-placement:unique-bosses`, `...:gwyn`): the
  *number of draws* a slot consumes cannot shift another slot's draws, so a rejection, an option toggle or a
  fallback in one slot does not reshuffle the others' random numbers. This is **not** full independence:
  outcomes still depend on slot order inside a map (the model set, the type map and the unique-boss cycle
  carry state). Describe it as "draws isolated, outcomes order dependent within a map".
- **Participation is always the slot's first draw** (`replacementChance = 100` always passes). Every other
  option draws only when it is enabled, and only from its own stream.
- **Old presets.** Presets of versions 1-4 migrate to `algorithm: permutation` with the neutral value of every
  new option (below); the permutation code path is untouched, so their output is byte-identical. The hash
  lock (24 placement hashes) must keep passing and gets a `sampling` counterpart.
- **Changing the algorithm necessarily changes every enemy seed.** `permutation` -> `sampling` is a different
  algorithm; that is why it is explicit in the preset and shown in the UI, never an implicit migration.
- **Contract rule (never silently ignore).** For each new option the document defines a *neutral value* under
  which `permutation` behaves exactly as today. The backend rejects any other value when the algorithm is
  `permutation`, and accepts all of them under `sampling`:

| Option | Neutral under `permutation` |
|---|---|
| `replacementChance` | 100 |
| `normalReplacement` | `normals` |
| `bossReplacement` | `bosses` |
| `npcReplacement` | `none` |
| `bossChance*` | the default (10 / 90); only meaningful in "both" modes |
| `fit` | `anywhere` |
| `difficultyMode` / `strictness` | `random` / default |
| `typeReplacement` | `disabled` |
| `uniqueBosses` | off |
| roaming boss souls / respawn, hostile NPCs, custom pool | off / default |

  Presets move to **version 5**, which adds `enemies.placement` (algorithm and options) and migrates versions
  1-4 to `permutation` plus neutral values. The flat existing keys (`preventSameEnemy`, `gwynNerf`,
  `pinwheelChaos`, `secondGargoyle`, mimics, mosquitoes, scaling) do not move.

---

## 8. Option interaction matrix

How each existing option behaves under the two algorithms, and how each new option interacts.

| Option | `permutation-v1` (today) | `sampling-v1` |
|---|---|---|
| Replace mimics / mosquitoes off | slots leave the pool (and as sources) | slots do not participate; their models stay in the set |
| Prevent same | swap repair after the budget pass | rejection per candidate; keep-vanilla fallback disabled |
| Gwyn nerf | Gwyn removed from boss candidates per seed | per-candidate rejection with the legacy probability |
| Pinwheel Chaos | main pinned out of the boss shuffle; 14 clones sampled with repetition | main skipped; clones are ordinary slots with `scaling: vanilla` |
| Second Gargoyle | unit swap + independent draw for one body | unchanged (unit planner); participation per unit |
| Enemy scaling | unchanged | unchanged; per-placement override for roaming bosses (section 9) |
| Replacement chance | n/a (100) | per slot / per type / per unit |
| Boss, normal, NPC modes | n/a | category step; bosses into normal slots and the reverse are new cross-class cases (batches D-E) |
| Fit, difficulty | n/a (anywhere, random) | pool filters |
| Type replacement | n/a | per-map type map with injectivity |
| Unique bosses | n/a | cycle over the boss pool |
| Custom pool | n/a | pool filter |

Interactions inside the group: chance gates everything; category gates the pool; fit and difficulty narrow
it; the budget and exclusions narrow it further; type replacement short-circuits it; Prevent same, Gwyn and
Unique bosses act on single candidates.

---

## 9. Fog and event risk matrix

Surfaces: MSB = parts only; EMEVD = event instructions or new events; NpcParam / GameParam = rows; Linked =
unit planning; Progress = key, boss-reward or item assumptions; Overlay = FogHost's 15-event boss overlay or
a new semantic rule.

| Option / change | MSB | EMEVD | NpcParam | Linked | Progress | Overlay | Risk |
|---|---|---|---|---|---|---|---|
| Algorithm switch alone | yes | per replaced entity (as today) | scaled rows; **must exist for every placement (6.9)** | no | item integrity: drops of repeated types (rule 6.9); FogMod's tracked items are not NpcParam drops | no | low **with** rule 6.9; medium without it. DsrDataTool accepts repeats. |
| Replacement chance | yes | fewer edits | fewer rows | per-unit | no | **only fewer edits** | low: a strict subset of today's surface. |
| Normal replacement (normals only) | yes | as today | as today | no | no | no | low |
| Fit, difficulty, strictness, Type replacement, Custom pool, Unique bosses | selection only | none new | none new | no | no | no | none beyond the algorithm switch |
| Boss replacement (bosses only) | yes | boss events as today | as today | per-unit | boss-reward lots move with the slot | **the 15 known events; events edited when a boss slot is *kept* disappear from the set** | medium: needs a boss sweep, not a new rule |
| Boss slot receives a **normal** enemy | yes | **new mutation kind** (boss bar and boss-event assumptions for a non-boss model) | scaled row must exist to carry the slot's reward | no | **boss rewards (Lord Souls, keys by boss lot)** depend on the scaled row keeping the destination's drop fields | **possibly new overlap** in whitelisted events | **high** |
| Normal slot receives a **boss** | yes | per-entity edits; wake-up rows | scaled row | no | the boss's own reward lot must not travel (scaled row copies the destination's drops) | no | medium; Fog sees only ordinary enemy edits |
| Roaming boss souls | no | none | `getSoul` on the scaled row (no new row family needed) | no | no | no | low-medium (NpcParam rows are already preserved via `scaledNpcParamId`) |
| Respawning roaming bosses | no | per-entity custom events (or an NpcParam bit on the scaled row) | maybe a flag | no | no | no | low; custom events are appended in free ranges |
| NPC replacement | yes | **NPC talk and quest events share entity ids** | row for the replaced NPC | no | quest flags | **possible new overlap** (Fog also edits NPC-adjacent events) | **high**; measure the edited-event set against FogMod's before shipping |
| Hostile NPCs | yes | allegiance events per entity | NPC rows | no | shop and quest NPCs as hostile sources | no | medium-high |

### 9.1 Drop ownership

Measured on the real catalog (section 11.2): 1,595 of 1,703 regular slots reference an NpcParam drop lot (193
distinct lots); 41 lots belong to a single slot and 30 of those contain weapons, armor or rings; 37 of the 86
regular types drop equipment of their own. None of FogMod's 27 key items is in an NpcParam drop lot, and the
12 lots holding boss souls are not referenced by any NpcParam. So drop duplication cannot trigger the
Peculiar Doll class of Fog failure, but it duplicates or loses equipment unless drops follow the slot (6.9).

**Finding about the current engine (not fixed here):** with Enemy scaling set to `vanilla` and drops not
randomized, `permutation-v1` already duplicates single-owner equipment drops in 95 of 100 seeds, because
placements without a scaled row keep the replacement's own drops. Most cases are the Titanite Demon dragon
group (Titanite Catch Pole, 92 of 100 seeds); the rest are archetype-duplicated bosses (Pinwheel's three masks
9, Taurus's axe 6, Super Ornstein's ring 5, Capra's weapon 3). The options added later (Gwyn nerf, Second
Gargoyle) add about 0.05-0.10 duplicated lots per seed to that baseline. The default scaling, `area`, creates
a scaled row for every changed placement and shows none (0 of 100). No Fog impact, no progression impact.

Rules for the implementation tasks, inherited from the Gargoyle work: do not broaden the FogHost overlay;
if a batch edits an event outside the known set, **stop and report** the event and its FogMod form. The two
cross-class cases and the NPC options are the only ones that can create new overlap; everything else changes
which model goes where and nothing else.

---

## 10. Incremental implementation plan

Each batch ends with: focused unit tests on synthetic catalogs, the permutation hash lock still identical,
`patch-enemies` and `verify-skips` on the real catalog, and the stated sweeps. Order is by dependency and
risk: data first, selection-only options next, event-touching options last.

| Batch | Scope | Engine | Contract / preset | UI | Tests | Real-format checks | Fog risk |
|---|---|---|---|---|---|---|---|
| **0** (done, see 10.1) | Infrastructure, no behavior change | `placement.js` (new repository-authored module in `vendor/src/core`, listed in the module-set test and the pin; patch `0005`); class derivation (size, difficulty, NPC/boss class); `classTableHash`; per-slot streams; ladder; diagnostics block | `enemies.placement.algorithm` (`permutation` only selectable), neutral-value rule, preset v5 migrating 1-4 | an "Enemy placement" group with the algorithm shown, read-only | class derivation vs fixtures; neutral-value rejection; preset v5 migration; permutation hash lock | none (output unchanged) | none |
| **A** (built, one open conflict, see 10.2) | Sampler for regular enemies | `sampling-v1`: chance, normal mode {none, normals}, fit, difficulty `random`, Prevent same, mimic/mosquito/Pinwheel clones, coverage reserve, budget, ladder | add `sampling`, `replacementChance`, `normalReplacement`, `fit`, `difficultyMode=random` | sliders and selects, Replacement chance / Placement activated (remove the planned badges) | determinism, budget <= 30, zero fit violations, ladder rung counts, chance statistics, hash lock; **every changed placement has a scaled row; each drop lot is referenced by the same slots as in vanilla** | 20-seed Bosses + Fog sweep with `sampling`, **one with Enemy scaling `vanilla`**, `verify-skips`, `patch-enemies`, duplicates per map | low |
| **B** (built, see 10.3) | Difficulty curve | class draw, strictness tables, D-1 refinement, Easy Asylum rule, curated overrides | `difficultyMode` curve values, `strictness`, `easyAsylum` | select + slider activation | offset distributions, same-class share, empty-class fallback | 20 seeds | none |
| **C** (C1 Type replacement built, see 10.4; C2 custom pool built, see 10.5) | Type replacement and custom pool | type map with type-level participation and injectivity; pool filter | `typeReplacement`, `customPool` (named list of enabled types) | select, pool editor (largest UI piece) | injectivity, sentinel cases, pool filter | 10 seeds | none |
| **D** (built, see 10.6) | Boss class (bosses only) | boss sampler over the portable single-body pool, per-candidate Gwyn nerf, Unique bosses, unit participation | `bossReplacement` {none, bosses}, `uniqueBosses`, `bossChanceInBossSlots` | activation | boss repetition, unit swaps, hash lock | **40 seeds** Bosses + Fog (overlay set must remain the 15 events), `verify-skips` | medium |
| **E** (E1 built 2026-10-06, section 10.7; E2 built as a three-encounter subset 2026-10-07, section 10.8) | Cross-class and roaming boss options | boss to normal slot, normal to boss slot, "both" modes, `getSoul` on the scaled row, respawn handling | modes `normals`/`both`, `bossChanceInNormalSlots`, `roamingBossSouls`, `respawningRoamingBosses` | activation | reward ownership, scaled row always present, boss bar rules | **40 seeds**, measure edited events against the overlay and FogMod; stop on any new overlap | **high** |
| **F** (F1 built 2026-10-07, section 10.9; NPC replacement not built by decision) | NPC class and hostile NPCs | NPC classification, hostile sources, slot-owned rows, wake-up events | `hostileNpcs` (an extension key); `npcReplacement` is not built | activation | slot-owned drops, talk preservation (no talk file touched) | event-set diff vs FogMod, then 100 Fog seeds | **high** |
| **G** (built 2026-10-07, section 10.10) | Default flip for NEW configurations (product decision), ordering lock, validation summary | none (summary only) | default `sampling-v1` with the legacy defaults; old Seed Codes, presets and settings keep their algorithm | "Sampling (recommended)" / "Classic permutation", no dead controls | ordering, default, migration tests | 500 engine + 300 real-tool default seeds, 810 event-owner seeds, feature matrix 384 x 30 | low |

### 10.1 Part 0 as built (2026-10-05)

Batch 0 was built narrower than the table above; the rest moves to Batch A, where it has a consumer.

- **Built.** Algorithm identifier `permutation-v1` | `sampling-v1` carried UI -> preset -> contract ->
  validation -> mapper -> engine config -> engine dispatch -> manifest (`enemyPlacement`) and result
  (`placementDiagnostics`); preset v5 (`enemies.placement.algorithm`; versions 1-4 migrate to
  `permutation-v1`; a v5 preset must state it); the narrow dispatch `planExtractedEnemies`; `placement.js`
  with stable slot-id validation, per-slot substreams, `ModelBudget`, diagnostics, drop-ownership helpers; the
  sampling-only-option gating mechanism (`SAMPLING_ONLY_OPTIONS`, empty).
- **Gating.** The selection of `sampling-v1` fails at four layers, with one stable code: backend validation (a
  problem containing `ENEMY_PLACEMENT_ALGORITHM_UNAVAILABLE`, also on preset import), the config mapper (typed
  result), the bridge (non-retryable `ENEMY_PLACEMENT_ALGORITHM_UNAVAILABLE`) and the engine (`generate()`
  throws `EnemyPlacementUnavailableError` before anything else, also when no enemy category is enabled).
  `normalizeConfig` does not apply the enum fallback to the key, so an unknown value is refused, not replaced.
  Part A enables the algorithm in exactly two lists (`IMPLEMENTED_ENEMY_PLACEMENT_ALGORITHMS` in the contract
  and in `placement.js`) and implements the planner behind the dispatch.
- **Not built (Part A or later).** Class derivation (size, difficulty), `classTableHash`, the fallback ladder
  as code, the sampler, the UI group. The ladder and rejection vocabularies exist as diagnostic constants
  only. No sampling option exists yet, so the neutral-value table in section 7 has no entries.
- **RNG.** `deriveSlotStreamSeed(seed, slot)` = `deriveSeed(seed, "slot:<id>", "enemy-placement-v1")`: first
  four bytes (little endian) of SHA-256 of `enemy-placement-v1`, NUL, seed, NUL, `slot:<id>`. It depends on no
  product or engine version. The permutation path derives none (a counter proves it in a test). Isolation is
  of draws, as in section 7; outcomes are still order dependent inside a map.
- **Model-budget policy (decided).** Safety first: the budget is the number of distinct enemy models a map
  holds **after** the run (default 30); fixed and vanilla-kept models count; admission is deterministic (fixed
  models sorted, then candidates in caller order); a rejected candidate carries the reason `model-budget`; a
  map that is over the cap through fixed models alone fails with `ENEMY_PLACEMENT_BUDGET_UNSATISFIABLE`. There
  is **no** Blighttown-50 exception and **no** legacy quirk of counting only newly introduced models. Checked
  against the real catalog: the vanilla maximum is 28 distinct models (Undead Parish, `m10_01`), no map
  exceeds 30, so a cap of 30 rejects no *vanilla* map. **Superseded by Part A (10.2): after the dragon/boss plans the
  Parish already holds 30-31 models, so a fixed cap of 30 is not an invariant; the effective cap is
  `max(30, baseline)`.** The budget is not applied to `permutation-v1`, which has its own limiter.
- **Drop-ownership policy (decided).** `permutation-v1` keeps its documented legacy behavior (placements
  without a scaled NpcParam row keep the replacement's own drops; 9.1) and is not fixed here. `sampling-v1`
  must give every changed placement a scaled row; `describeDropOwnership`, `slotOwnedDrops` and
  `assertSlotOwnedDrops` express and check that, and a test pins both behaviors.
- **Output proof.** The 24 permutation placement hashes are identical before and after; two combined-only
  packages (area scaling, and vanilla scaling with Prevent same and starting-loadout options) have identical
  hashes for all 56 non-metadata files, and the combined + Fog package is identical in all 73. The three
  metadata files (`config.json`, `randomizer.json`, `patch-manifest.json`) embed the temporary workspace path,
  so they differ between any two runs, and gain only the new fields `enemyPlacementAlgorithm` and
  `placementDiagnostics`. `placementHash` is computed from the placements and is unaffected.

### 10.2 Part A as built (2026-10-05): `sampling-v1` for regular enemy slots

**Scope.** Only regular enemy slots (the 1,702 slots the permutation also treats as regular) are sampled.
Bosses, linked and dragon units, Mass of Souls, the Second Gargoyle, Gwyn nerf, friendly NPCs and every
special entity are planned by the unchanged code and are the *fixed models* of their maps. The sampler and the
permutation share the slot filter (`regularEnemyContext`) and the placement decoration
(`decorateEnemyPlacements`); the permutation's own code path is byte-identical (24 placement hashes, 56/56 and
73/73 package files).

**Legacy semantics implemented.**

| Control | Values in Part A | Status |
|---|---|---|
| Replacement chance | 0-100, integer; one draw per slot, `int(100) + 1 <= chance`, the slot's first draw from `slot:<id>` | **FULL** (whole range, non-worsening budget) |
| Normal enemy replacement | `none` (legacy 0), `normals` (legacy 2); `bosses` (1) and `both` (3) need boss sources and are unavailable | **PARTIAL** (2 of 4 values) |
| Placement fit | `fits` (0), `anywhere` (1), `anywhere-except-npcs` (2: differs from 1 only for NPC targets, which are not replaced, so identical for regular slots) | **FULL** for regular slots (0 violations) |
| Prevent same enemy | candidate with the slot's own model is rejected; keep-vanilla stays allowed | **FULL** (already FULL for `permutation-v1`) |

A slot that is not replaced (chance, mode `none`, or kept-vanilla) is exactly vanilla: **no placement is
emitted** for it (decision 2). Pinwheel Chaos clones are ordinary slots with `scaling: vanilla`.

**Class derivation (`regular-size-v1`).** For every model that occurs in a regular slot: `footprint = max(2 *
hitRadius, hitHeight)` of a slot, the model's footprint is the **largest** usable one among its regular slots
(order independent); size class = number of edges `[0.9, 1.7, 2.6, 4.2, 7]` reached (0-5). A slot's class is its
model's class (so a slot always fits its own vanilla model); a slot of a model with no type (a Pinwheel clone)
uses its own capsule, or 5 when unknown. A model without any usable capsule raises
`ENEMY_PLACEMENT_CLASS_UNDERIVABLE` (does not occur: all 117 catalog models have consistent usable capsules).
No hand-authored corrections. Difficulty classes are Part B.

**`classTableHash`** = SHA-256 of `{version, edges, [(model, size)...]}` sorted by model. Same catalog, same hash;
catalog order does not matter; a change that crosses a class edge, or a new model, changes it; a change inside a
class does not. It is reported in `placementDiagnostics`, the run manifest (`enemyPlacement.classTableHash`) and
the bridge log. Real catalog: `4c7da92687b689ed...`.

**Non-worsening model budget (decided).** *`sampling-v1` uses a non-worsening model-budget policy. The normal
target is 30 distinct models per map. If out-of-scope/fixed plans already require more than 30, that
unavoidable baseline becomes the effective cap. The regular sampler may preserve or reduce that overflow but
may not increase it.* Implementation (`effectiveModelBudget`, `planSampledRegularEnemies`):

- `baseline(map)` = the distinct models of the fixed plans (every non-regular slot at its final model, which
  includes bosses, dragons, units and special placements) plus the vanilla models of regular slots that the
  replacement chance (or mode `none`) leaves alone. These are the models the sampler cannot remove.
- `effectiveCap(map) = max(30, baseline)`. The map's `ModelBudget` starts as baseline plus the coverage
  reserve and admits a candidate only while it stays within `effectiveCap`.
- No separate reference counting was needed: a map's final model set is exactly baseline plus the models the
  sampler admits, because the vanilla model of a *replaced* slot is simply never added. A model whose last slot
  is replaced therefore leaves the count by construction, and the capacity it would have used is available to
  later slots. The post-condition `finalModels <= effectiveCap` is checked per map after planning.
- The earlier wording that treated 30 as an unconditional whole-map invariant (6.6, 10.1, the first draft of this
  section) is superseded. The reason is measured: vanilla Parish has 28 models, the dragon/boss plans already
  push it to 30-31 (88 of 100 seeds with `permutation-v1`, regular enemies off, bosses on; up to 33 in other
  maps when vanilla regular models are kept), and the shipped `permutation-v1` produces such maps.
- Diagnostics per map: `baseline`, `effectiveCap`, `finalModels`; counter `mapsAboveCap`.
- `ENEMY_PLACEMENT_BUDGET_UNSATISFIABLE` can no longer be raised by the planner (the effective cap is never below
  the baseline); it remains a defensive, non-retryable error of the `ModelBudget` primitive.

**Per-map flow.** Maps in sorted order, slots in sorted id order, one stream per slot. (1) participation draw;
(2) model set = baseline + coverage reserve (trimmed to the effective cap); (3) per participating slot:
candidates = types with size <= slot size (`fits`), drawn uniformly with list shrink; a candidate is rejected for
`prevent-same` or `model-budget` (set full and model not in it); (4) fallback ladder when nothing survives:
`kept-vanilla` (own model in the set or room), `in-set-fit` (smallest in-set type that fits), else
`ENEMY_PLACEMENT_INFEASIBLE`; (5) variant drawn uniformly among the type's (NpcParam, think-param) variants;
(6) post-condition: final models of the map <= effective cap.

**Deviations from 6.6 and 6.7.** (a) The design's repair "demote the smallest participating slots until the
precondition holds" cannot lower the model count (a demoted slot keeps its vanilla model in the set); the
reserve is **trimmed** to what fits instead (counter `reserveTrimmed`). (b) The `fit-relaxed` rung was
**removed**: with zero headroom (a map already at its effective cap) a small slot whose own model is not in the
set can have no fitting in-set type, and an oversized type would break the fit invariant. The slot is then
`ENEMY_PLACEMENT_INFEASIBLE`, which depends only on the participation draws, so another attempt (new combined
seed) solves it. This is what the first Part A measurement showed as "fit violations" (3-5 per 100 seeds at 25%
and 60%); with the rung removed there are none.

**Error and retry classification.** `ENEMY_PLACEMENT_INFEASIBLE`: retryable, its own failure code, recorded as
the attempt result in the manifest (bounded by the 3 combined attempts of the pipeline). Deterministic
structural failures (`ENEMY_PLACEMENT_BUDGET_UNSATISFIABLE`, `ENEMY_PLACEMENT_CLASS_UNDERIVABLE`,
`ENEMY_PLACEMENT_OPTIONS_INVALID`, `ENEMY_PLACEMENT_ALGORITHM_UNAVAILABLE`): typed, non-retryable. They no longer
appear as a generic `GENERATION_FAILED`. The legitimate Fog retries are unchanged.

**Drop ownership.** Every changed `sampling-v1` placement carries a scaled NpcParam row
(`forceScaledNpcParam`), also with Enemy scaling `vanilla`; DsrDataTool copies the destination slot's item-lot
fields and team onto it (checked in `Program.cs`, `AddScaledNpcRows`), so repeated sources never multiply a
source's drops. `assertSlotOwnedDrops` runs on every plan. Catalog validation (real NpcParam/ItemLotParam): for
every changed placement the effective drop lots are those of the destination slot (0 mismatches), so the
multiset of lot owners equals vanilla and no tracked item is introduced. `permutation-v1` is unchanged (its
`vanilla`-scaling duplication remains documented in 9.1).

**Catalog-only validation (real catalog, 100 seeds per variant, bosses + dragons + Gwyn nerf + Second Gargoyle on).**
"seeds ok" are the seeds whose first attempt placed every slot; the others raised the retryable
`ENEMY_PLACEMENT_INFEASIBLE` (all in the Parish, only with `fits`; `anywhere` never fails).

| Variant | seeds ok / 100 | participation | placements/seed | model-budget rej./seed | kept-vanilla | max models/map | maps with baseline > 30 (max baseline) | sampler overflow | reserve trimmed | fit viol. | prevent-same viol. | non-det. | ms/seed |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0% fits | 100 | 0.00% | 0 | 0 | 0 | 33 | 108 (33) | 0 | 0 | 0 | 0 | 0 | 7 |
| 0% anywhere | 100 | 0.00% | 0 | 0 | 0 | 33 | 108 (33) | 0 | 0 | 0 | 0 | 0 | 6 |
| 0% fits + Prevent same | 100 | 0.00% | 0 | 0 | 0 | 33 | 108 (33) | 0 | 0 | 0 | 0 | 0 | 6 |
| 25% fits | 91 | 24.93% | 424 | 550 | 0 | 33 | 50 (33) | 0 | 0 | 0 | 0 | 0 | 8 |
| 25% anywhere | 100 | 24.92% | 424 | 868 | 0 | 33 | 54 (33) | 0 | 0 | 0 | 0 | 0 | 8 |
| 25% fits + Prevent same | 91 | 24.93% | 424 | 604 | 15 | 33 | 50 (33) | 0 | 7 | 0 | 0 | 0 | 8 |
| 60% fits | 95 | 59.98% | 1021 | 1636 | 0 | 31 | 5 (31) | 0 | 0 | 0 | 0 | 0 | 9 |
| 60% anywhere | 100 | 59.99% | 1021 | 2648 | 0 | 32 | 7 (32) | 0 | 0 | 0 | 0 | 0 | 9 |
| 60% fits + Prevent same | 95 | 59.98% | 1020 | 1809 | 59 | 31 | 5 (31) | 0 | 6 | 0 | 0 | 0 | 9 |
| 90% fits | 100 | 90.05% | 1533 | 2408 | 0 | 30 | 0 (29) | 0 | 0 | 0 | 0 | 0 | 10 |
| 90% anywhere | 100 | 90.05% | 1533 | 4103 | 0 | 30 | 0 (29) | 0 | 0 | 0 | 0 | 0 | 11 |
| 90% fits + Prevent same | 100 | 90.05% | 1532 | 2569 | 38 | 30 | 0 (29) | 0 | 0 | 0 | 0 | 0 | 10 |
| 100% fits | 100 | 100.00% | 1702 | 2451 | 0 | 30 | 0 (21) | 0 | 0 | 0 | 0 | 0 | 11 |
| 100% anywhere | 100 | 100.00% | 1702 | 4414 | 0 | 30 | 0 (21) | 0 | 0 | 0 | 0 | 0 | 11 |
| 100% fits + Prevent same | 100 | 100.00% | 1702 | 2425 | 9 | 30 | 0 (21) | 0 | 0 | 0 | 0 | 0 | 11 |
| 25% fits + Prevent same, vanilla scaling | 91 | 24.93% | 424 | 604 | 15 | 33 | 50 (33) | 0 | 7 | 0 | 0 | 0 | 8 |
| mode none | 100 | 0.00% | 0 | 0 | 0 | 33 | 108 (33) | 0 | 0 | 0 | 0 | 0 | 6 |
Only the Parish (`m10_01`, baseline up to 33, above 30 in 96 of 100 seeds at 0%) and `m14_01` (31) ever have a
baseline above 30; the sampler left every one of them at or below its effective cap (0 overflows). 33 is more
than any shipped output (`permutation-v1` with regular enemies off ends at 31), because Mass of Souls and the
vanilla regular models are kept as well; **the in-game behavior of a 32-33-model Parish has not been tested**.
Entity-id duplicates equal the permutation's (3.0 per seed, from regular placements of both algorithms; none
with regular enemies off). Bosses, dragons and linked units are identical to `permutation-v1` for the same seed
in every variant.

**Real-format validation (Items + Enemies + Fog, bosses on, FogHost 0.5.0, 55 seeds + 2 full proofs).**

| Configuration | seeds | generated | first-attempt placement failures (retried) | Fog retries (existing `FOG_UPSTREAM_INCOMPATIBLE`) | merge conflicts | boss overlay events | `verify-skips` failures | sampler overflow | changed without scaled row | max final models |
|---|---|---|---|---|---|---|---|---|---|---|
| 100%, fits | 10 | 10 | 0 | 2 | 0 | same 15, 10/10 seeds | 0 / 12 | 0 | 0 | 30 |
| 60%, fits | 10 | 10 | 1 | 3 | 0 | same 15 | 0 / 13 | 0 | 0 | 31 |
| 60%, fits + Prevent same | 10 | 10 | 0 | 1 | 0 | same 15 | 0 / 11 | 0 | 0 | 31 |
| 25%, fits | 10 | 10 | 1 | 3 | 0 | same 15 | 0 / 13 | 0 | 0 | 32 |
| 25%, anywhere + Prevent same | 10 | 10 | 0 | 1 | 0 | same 15 | 0 / 11 | 0 | 0 | 33 |
| 100%, mode `none` | 5 | 5 | 0 | 1 | 0 | same 15 | 0 / 6 | 0 | 0 | 32 |

Mismatches in events the combined engine edited: 0 in every sweep; the live installation was unchanged in every
run. Two full proofs (`fits` + Prevent same at 100% and at 25%): byte-identical rerun, activation and restore
pass. A first-attempt `ENEMY_PLACEMENT_INFEASIBLE` was retried with a new combined seed and the run succeeded
(2 of 50 partial-chance seeds). The final model count and the drop-ownership invariant were checked on the
generated placement plans (`changed without scaled row` = 0), not on the NpcParam regulation of the package.

### 10.3 Part B as built (2026-10-05): difficulty and map-local retries for regular enemies

**Legacy semantics confirmed (pinned revision 83f008d, `enemy_rng.py` / `enemy_randomizer.py`).**

| Item | Legacy behavior |
|---|---|
| Difficulty mode | GUI "Difficulty mode", config position 5: 1 "Random with difficulty curve", 2 "Fully random", 3 "Fully random + easy asylum" (**default**), 4 "Difficulty curve + easy asylum". |
| Difficulty strictness | GUI "Difficulty strictness", position 10: 0 Strict, 1 A bit loose (**default**), 2 Very loose. Disabled in the GUI unless the mode is 1 or 4 ("[No Effect]"). |
| Classes | Every original and every replacement enemy has a curated class 0-7 (`valid_replacements.txt`, `valid_new.txt`). The destination reference is the class of the **original model** of the slot. |
| Curve | `getDifficultyList`: draw a class offset from a strictness table around the destination class (offsets that leave 0-7 are skipped and their mass falls back to the own class), take the list of that class (already size-filtered), and draw uniformly from it. The class is a probabilistic preference; inside the chosen class the list is a hard filter. |
| Fallbacks | Drawn class empty (for the size) -> the destination's own class list; that empty too -> no replacement ("Failed to find fitting enemy with appropriate difficulty", the slot stays vanilla). Once the map's unique-type limit is reached the draw is from the types already used, filtered by size only: the difficulty filter lapses. |
| Modes 2 and 3 | No class filter at all (destination class ignored). Mode 3 and 4 add Easy Asylum: twelve hard-coded Asylum slots take a random pick from a hard-coded list of easy enemies, overriding everything including fit and Prevent same. |
| Order | Replace chance first (every eligible slot), then category by mode, then max size by fit, then the class list by difficulty, then rejection per candidate (hard-coded pairs, Prevent same) with list shrink. Boss and NPC slots use the same functions; NPC slots always use mode 2. |
| Quirks | `randint(0, 100)` has 101 outcomes against percentages that sum to 100 (a 1-in-101 skew to the own class); the cap lapses the filter entirely; Easy Asylum bypasses fit and Prevent same. |

**Values supported for regular enemies: all four modes and all three strictness values (parity FULL for both
controls).** Strictness keeps the legacy meaning (only effective in the curve modes). New `sampling-v1`
configurations start at the legacy defaults, mode `random-easy-asylum` and strictness `loose`; the neutral values
under `permutation-v1` are `random` and `loose`.

**Project-owned classes (`regular-class-v2`, no legacy table).** For every model in a regular slot:
`difficulty = floor(8 * (0.6 * tier / 7 + 0.4 * hpRank))`, clamped to 0-7, where `tier` is the upper median of the
**map progression tier** of the maps the model occurs in (an authored play-order table, 0 Asylum ... 7 Kiln:
`m18_01` 0; `m10_01`, `m10_02` 1; `m10_00`, `m12_00`, `m12_00_01` 2; `m13_00`, `m11_00` 3; `m14_00`, `m16_00` 4;
`m15_00`, `m15_01`, `m13_02` 5; `m12_01`, `m13_01`, `m14_01`, `m17_00` 6; `m18_00` 7) and `hpRank` is the rank
(0..1) of the model's median log HP among all regular models. The destination class of a slot is the class of its
own model; a slot whose model is not a regular type (a Pinwheel clone) uses its map tier and its own HP with the same
formula. A model without usable HP or in a map outside the table raises `ENEMY_PLACEMENT_CLASS_UNDERIVABLE`; there is
no default class. `classTableHash` now covers the version, size edges, the progression table and every
`(model, size, difficulty)`. Real catalog: 85 regular models over the classes 0-7 as 1 / 6 / 12 / 13 / 23 / 16 /
9 / 5, hash `b48e6ebfba7474f4...`. The derived classes agree with the original ones only moderately (rank correlation
about 0.6 in the Part A evaluation); the goal is the user-facing semantics, not table parity. Weights and tiers were
not tuned against the legacy classes.

**Strictness tables (authored; percent, the rest stays on the own class).**

| Strictness | Harder (+1, +2, ...) | Easier (-1, -2, ...) | Own class | Reach |
|---|---|---|---|---|
| Strict | 11, 4 | 5 | 80 | +2 / -1 |
| A bit loose | 14, 8, 4, 2, 1 | 6, 3, 2 | 60 | +5 / -3 |
| Very loose | 13, 10, 8, 6, 4, 2, 1 | 7, 4, 3, 2 | 40 | +7 / -4 |

Very loose reaches any class from any class (the original describes it as able to put a late boss in the Asylum).
The class draw uses `int(100)`, so there is no 1-in-101 skew.

**Eligibility matrix** (`eligibleDifficultyClasses`, tested for every mode x strictness x destination class 0-7 x map):
fully random: classes 0-7; curve: destination - down .. destination + up clamped to 0-7 (always containing the
destination), with the reach column above; easy-Asylum modes in the Asylum map (`m18_01_00_00`, every regular slot):
classes 0-1 whatever the mode or destination. The draw maps onto the distribution exactly (tested over all 100
draws). Easy Asylum is an authored rule (a hard cap of class 1, still subject to fit, Prevent same and the budget),
not the hard-coded slot and enemy lists.

**Filtering order (one deterministic order, per slot, sorted maps and ids).** (1) replacement chance, the slot's
first draw; (2) replacement mode `none`; (3) Placement fit: types no larger than the slot, never relaxed; (4)
difficulty: the Easy Asylum cap, or the curve class (second draw, then the pool of that class), or nothing; (5)
Prevent same; (6) special exclusions: none for regular-to-regular replacement (Mimic and mosquito slots are not
regular slots when their options are off); (7) model budget (effective cap); (8) uniform draw over the surviving
types with list shrink. Pools are tried in order: wanted class, the destination's own class, and (only when the
budget removed candidates) the nearest classes outward. Then the fallback ladder (keep vanilla, smallest in-set
fitting type) and the variant draw. A difficulty fallback exists only where the legacy tool has one (own class) or
where the budget forces it (nearest class: deviation D-1, counted as `difficultyLapses`); **physical fit is never
relaxed**.

**Map-local retries.** If a slot has no placement the whole map is planned again from scratch (participation,
reserve, every draw) with the streams `enemy-placement-v1 / attempt:<n>:slot:<stable-slot-id>` (the slot id contains
the stable map id); attempt 0 is the original `slot:<id>` stream, so a map that succeeds at once is byte-identical to
Part A. At most `MAP_ATTEMPTS` = 3 attempts per map; only the failing map is replanned, every other map, the
boss/dragon/NPC plans and the master seed are untouched. Diagnostics: `mapAttemptsFailed`, `mapsRetried`,
`maxMapAttempt`, `retries` (map, attempt, slot, baseline, cap), per-map `attempts`. When all attempts fail the run
raises `ENEMY_PLACEMENT_INFEASIBLE` with the map, the attempt count, difficulty and fit settings, Prevent same,
chance, baseline, effective cap and the terminal slot. That error is still retryable at the pipeline level (a new
combined seed also changes the baseline); structural errors stay non-retryable. The Fog retry is unchanged.

**Part A compatibility (measured).** 24 synthetic placement hashes of the Part A planner are identical in Part B; on
the real catalog, 100 seeds x 7 Part A variants: 681 outputs are identical, 0 differ, and the 19 Part A
first-attempt failures (5 + 5 at 60% fits, 9 at 25% fits) are now solved by a map retry.

**Catalog-only validation (real catalog, 100 seeds per variant, bosses + dragons + Gwyn nerf + Second Gargoyle on).**

| Variant | seeds ok | participation | map attempts 1 / 2 / 3 | seeds with a retry | final failures | max models | sampler overflow | fit viol. | Prevent-same viol. | Easy Asylum viol. | mean abs class offset | class fallbacks | difficulty lapses | non-det. | ms/seed |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| default diff (random+easy Asylum) 100% fits | 100 | 100.00% | 1800 / 0 / 0 | 0 | 0 | 30 | 0 | 0 | 0 | 0 | 0.00 | 0 | 0 | 0 | 14 |
| default diff 100% anywhere | 100 | 100.00% | 1800 / 0 / 0 | 0 | 0 | 30 | 0 | 0 | 0 | 0 | 0.00 | 0 | 0 | 0 | 14 |
| default diff 60% fits | 100 | 59.98% | 1795 / 5 / 0 | 5 | 0 | 31 | 0 | 0 | 0 | 0 | 0.00 | 0 | 0 | 0 | 12 |
| default diff 25% fits | 100 | 24.93% | 1791 / 7 / 2 | 9 | 0 | 33 | 0 | 0 | 0 | 0 | 0.00 | 0 | 0 | 0 | 9 |
| permissive (curve very-loose) 100% fits | 100 | 100.00% | 1800 / 0 / 0 | 0 | 0 | 30 | 0 | 0 | 0 | 0 | 0.70 | 11815 | 6186 | 0 | 14 |
| permissive (curve very-loose) 60% fits + Prevent same | 100 | 59.98% | 1795 / 5 / 0 | 5 | 0 | 31 | 0 | 0 | 0 | 0 | 0.65 | 7638 | 3244 | 0 | 12 |
| curve loose 100% fits | 100 | 100.00% | 1800 / 0 / 0 | 0 | 0 | 30 | 0 | 0 | 0 | 0 | 0.46 | 8080 | 7142 | 0 | 13 |
| curve loose 25% fits + Prevent same | 100 | 24.93% | 1791 / 7 / 2 | 9 | 0 | 33 | 0 | 0 | 0 | 0 | 0.44 | 2012 | 777 | 0 | 9 |
| strict curve 100% fits | 100 | 100.00% | 1800 / 0 / 0 | 0 | 0 | 30 | 0 | 0 | 0 | 0 | 0.24 | 3981 | 9849 | 0 | 14 |
| strict curve 60% fits + Prevent same | 100 | 59.98% | 1795 / 5 / 0 | 5 | 0 | 31 | 0 | 0 | 0 | 0 | 0.23 | 2335 | 2840 | 0 | 11 |
| strict curve 25% fits + Prevent same | 100 | 24.93% | 1791 / 7 / 2 | 9 | 0 | 33 | 0 | 0 | 0 | 0 | 0.21 | 949 | 802 | 0 | 9 |
| strict curve+easy Asylum 60% fits | 100 | 59.98% | 1795 / 5 / 0 | 5 | 0 | 31 | 0 | 0 | 0 | 0 | 0.18 | 2152 | 534 | 0 | 11 |
| strict curve 100% anywhere | 100 | 100.00% | 1800 / 0 / 0 | 0 | 0 | 30 | 0 | 0 | 0 | 0 | 0.27 | 385 | 8925 | 0 | 14 |

The catalog sweep is clean: 0 final failures in 13 variants x 100 seeds, 0 sampler overflows, 0 fit violations, 0
Prevent-same violations, 0 Easy Asylum violations, deterministic. A map needed a retry in 5 of 100 seeds at 60% and
9 of 100 at 25% (11 failed attempts, never more than 3 attempts); at 100% never. The mean absolute class offset
follows the tables (strict 0.21-0.24 against 0.24 expected, a bit loose 0.44-0.46, very loose 0.65-0.70). Difficulty
lapses (budget-forced widening to the nearest class) are the cost of a full map: 534-9,849 placements per 100 seeds,
i.e. at most 5.8% of the placements of the strict 100% variant; the lapse of the original tool was an unfiltered draw.

**Parish (`m10_01`) baseline / effective cap** at 25% fits (100 seeds): 29/30 20, 30/30 27, 28/30 6, **31/31 27,
32/32 15, 33/33 5**; at 60%: mostly 25-30 under the 30 target and 31/31 in 2; at 100%: 14-17. The sampler never raises
a map above its effective cap. These are the unverified-in-game cases; reproducible master seeds (config: bosses on,
Enemy scaling area, Gwyn nerf medium, Second Gargoyle replace, `sampling-v1`, 25% replacement, fits, default
difficulty `random-easy-asylum`/`loose`; attempt-0 combined seed; checked through `generateRun`):

| Parish final models | master seed | notes |
|---|---|---|
| 31 | `parish-12` | baseline 31, no retry |
| 32 | `parish-11` | baseline 32, no retry |
| 33 | `parish-28` | baseline 33, no retry (Fog needed one extra combined attempt) |
| 33 | `parish-92` | baseline 33, the Parish was planned with attempt 2 (a map retry) |
| 30 | `parish-10` | the Parish needed attempt 3 and ended at 30 |

**Real-format validation (Items + Enemies + Fog, bosses on, FogHost 0.5.0): 50 seeds + 3 full proofs.**

| Configuration | seeds | generated | Fog retries (`FOG_UPSTREAM_INCOMPATIBLE`) | merge conflicts | boss overlay | `verify-skips` failures | sampler overflow | changed without scaled row | max final models |
|---|---|---|---|---|---|---|---|---|---|
| default difficulty, 100%, fits | 10 | 10 | 1 | 0 | same 15 events | 0 / 11 | 0 | 0 | 30 |
| default difficulty, 60%, fits | 10 | 10 | 1 | 0 | same 15 | 0 / 11 | 0 | 0 | 31 |
| strict curve, 60%, fits | 10 | 10 | 0 | 0 | same 15 | 0 / 10 | 0 | 0 | 30 |
| strict curve, 25%, fits + Prevent same | 10 | 10 | 0 | 0 | same 15 | 0 / 10 | 0 | 0 | 31 |
| very-loose curve, 100%, fits | 10 | 10 | 0 | 0 | same 15 | 0 / 10 | 0 | 0 | 30 |

Mismatches in events the combined engine edited: 0; the live installation was unchanged in every run. No map retry
happened inside these 50 seeds (in the catalog sweep a retry occurs for about 1 in 10-20 seeds at 25%-60%), so three
full proofs were run on the Parish seeds above (`parish-92` and `parish-10` with Parish retries, `parish-28` without):
byte-identical reruns, activation and restore pass. The final model count and the drop-ownership invariant were
checked on the generated placement plans, not on the NpcParam regulation of the package.

**Remaining risk.** A Parish with 31-33 models has not been tried in the running game; use the seeds above. For Part C
(type replacement, custom pool) note that both reduce the candidate pool further, so the per-map retry bound (3) and
the nearest-class lapse should be re-measured; the class table is shared (`regular-class-v2`).

### 10.4 Part C1 as built (2026-10-05): Type replacement for regular enemies

**Legacy semantics confirmed (pinned revision 83f008d, `enemy_rng.py` / `enemy_randomizer.py`).**

| Item | Legacy behavior |
|---|---|
| Control | GUI "Type replacement": 0 "Enabled", 2 "Enabled, except roaming bosses.", 1 "Disabled" (**default**); config position 14. |
| Key | `creatureTypeId = creatureId.split('_')[0]`, the **original model** of the slot (`c2500`). `typeReplaceMap` maps it to the chosen replacement and is **reset for every map**, so the group is the original model **within one map**. |
| When | For every eligible slot the normal pipeline runs first: the replace-chance roll, the category, the candidate draw. Only afterwards, `if typeSub and creatureTypeId in typeReplaceMap and creatureType != "1"`, the result is **overwritten** by the stored value; otherwise the result of this slot is stored for the type. The first slot of a type in the map therefore decides for all later slots. |
| Chance | The roll that counts is the first slot's. Later slots still roll (RNG is consumed) but their result is overwritten, so a later slot whose own roll failed is replaced when the first one succeeded, and the reverse. |
| First slot not replaced | The stored value is the sentinel (`-2` chance, `-6` empty difficulty list, `-4` all candidates rejected): **every later slot of the type in the map is left alone**. |
| Same replacement | All slots of the group receive the same replacement model. The variant (AI / NpcParam) is still drawn **per slot**. |
| Constraints | Later slots **bypass** fit, difficulty, pair rejections and Easy Asylum: they take the stored model whatever their own size, destination class or map position. Only the first slot's context is checked. |
| Injectivity | `isCombinationInvalid`: with Type replacement on, a candidate whose id is already a stored value of the map is rejected ("avoid replacing multiple enemy types in one area with the same enemy"). The check indexes `validNew` with the stored value, so a stored sentinel (negative) indexes from the end of the list: an undefined quirk. |
| Prevent same | Applies to the first slot's draw only (the candidate must not contain the slot's own id). |
| Map limit | The type is counted in `uniqueIndices` once, when first introduced. |
| "Except roaming bosses" (2) | Stores nothing when the new type is a boss; it concerns boss sources only. |

**Group identity.** `map:<stable-map-id>:model:<original-model>`: map-specific like the original. Groups are built from
the regular slots of the map and ordered by model id, never by catalog position or array index. The same model in two
maps is two groups with independent decisions (tested).

**RNG.** Type replacement has its own namespace, disjoint from the slot streams: `enemy-placement-v1 / type:<map>:<model>`,
and for a map-local retry `attempt:<n>:type:<map>:<model>` (`placement.js`: `typeStreamName`, `createTypeStream`). The
group stream is drawn in the order: replacement chance (`int(100) + 1 <= chance`), difficulty class (curve modes),
candidate draws. Each member slot draws only its variant from its own slot stream. With the option off none of this
runs: the Part B code path, its draws and its output are untouched (tested against pinned hashes and, on the real catalog,
against the Part B planner over 700 outputs).

**Participation.** Replacement chance is evaluated **once per group**, as the original's first slot does (here the group
decides, so a later member can no longer contradict it). A group that is not replaced stays vanilla as a whole and its
model is pinned in the map (baseline). Mode `none` leaves every group alone without a draw.

**Candidate for the whole group (intersection).** The pool is the replacement types that fit **every** member (the smallest
member fit), then the difficulty class draw and pools of Part B, then per candidate: Prevent same, injectivity, budget.
Under `regular-class-v2` the size and the destination difficulty of a slot are functions of its model, so the members of a
real group have identical constraints and the intersection is trivial; slots whose model is not a regular type
(Pinwheel clones) are sized by their own capsule and the group takes the smallest (tested). The original lets later
members bypass the checks; DS1TR never builds a placement that a member cannot take. Nothing is relaxed: an empty
fit intersection means the group stays vanilla or the attempt fails.

**Difficulty across a group.** One class draw per group from the destination class of the group's model (the original: the
first slot's class); Easy Asylum caps every group of the Asylum map; the nearest-class lapse of Part B applies only under
budget pressure. No change to `regular-class-v2`, so `classTableHash` is unchanged.

**Prevent same.** Group level: replacement model != original model, rejected candidate by candidate with list shrink from
the group stream (no per-slot rejection draws). Off allows the identity replacement.

**Budget (reference counted).** A group moves one model for many slots, so the set is counted by owners (`ModelLedger`).
Pinned (solid) owners: fixed plans and groups left alone. Every participating group still *owns its original model* until it
is decided (pending). Deciding a group releases that ownership; the replacement gets a solid owner; keeping the group
re-takes the original. A model leaves the map with its last owner (an original model with no other reference is freed; one a
fixed plan still holds stays and counts); a replacement already present costs nothing; a new one is admitted only below the
cap; a model held only by pending groups counts as new (choosing it does not shrink the map). The effective cap is the
unchanged `max(30, baseline)` with the baseline = fixed plans plus groups left alone. A map may start above its cap (the
Parish with 32 models) and can then only shrink. The coverage reserve of slot mode is not needed: keeping the original
model is the fallback while the map is within its cap.

**Fallback sequence per group (bounded, nothing relaxed except one preference).**
(1) sampled candidate (class pools, Prevent same, injectivity, budget); (2) keep the group vanilla if its model is
admissible (the legacy outcome of "no candidate"); (3) the smallest in-set fitting model not used by another group;
(4) **shared model**: a model already in the map that fits every member, even if another group has it (solid owners first,
then nearest difficulty class, then model id, ties by one draw); (5) the attempt fails, the map is rebuilt with the next
attempt streams (at most 3), then `ENEMY_PLACEMENT_INFEASIBLE` with the group, map, attempts, settings, baseline and cap.
Fit, the budget and the fixed entities are never relaxed.

**Why rung 4 exists (measurement, not preference).** Without it, 93 of 100 seeds failed at 100% replacement chance in
every variant: the Parish holds 15-16 fixed models and 17 groups, which are 32 models before the run, and distinct
replacements cannot reduce a map (each group frees one model and takes one other). The failure was deterministic (100%
chance draws the same participation in every attempt), so no retry count could help; the retry budget was not
touched. Legacy injectivity is a variety preference of an engine that does not cap the total model count; the DS1TR cap is
a safety limit, so injectivity yields only when the alternative is a failed map (about 1.6 groups per seed at 100%, 0.01
at 60%, 0 at 25%).

**Other deviations.** (a) The sentinel indexing quirk of injectivity is not reproduced: only groups that were actually
replaced block a model. (b) A later member of the original can be replaced although its own roll failed; here the group
decides. (c) "Enabled, except roaming bosses" equals "Enabled" for regular enemies (no boss source exists), exactly as
Placement fit "anywhere, fit NPCs" equals "anywhere". (d) The hook for a custom pool is `createCandidatePool(table, accept)` /
`candidateFilter`: groups, slot sampling and every fallback read the pool from there.

**Parity.** E15 Type replacement: FULL for regular enemies (all three values; the roaming-boss exception has no effect
until boss sources exist).

**Diagnostics.** Counters: `typeGroups`, `typeGroupsReplaced`, `typeGroupsKeptByChance`, `typeGroupsKeptVanilla`,
`typeGroupsFallback`, `typeGroupsShared`, `typePreventSameRejections`, `typeInjectivityRejections`,
`typeFitRejections`, `typeDifficultyRejections`, `typeMapAttemptsFailed`, `typeMapsStartedOverCap`; per map `typeGroups`.
Detail level: one record per group (id, model, member count, participation, selection, stage, member fit, classes,
attempt) in `groups`, and the group id on every member slot record.

**Catalog-only validation (real catalog, 100 seeds per variant, bosses + dragons + Gwyn nerf + Second Gargoyle on;
"Part B" columns are the same seeds with Type replacement off).**

| Variant | final failures | seeds ok on attempt 0 | seeds with a map retry (Part B) | failed attempts (Part B) | map attempts 1 / 2 / 3 | groups replaced | slots in replaced groups | kept vanilla | shared-model fallbacks | fit viol. | Prevent-same viol. | above effective cap | difficulty lapses | max models | ms/seed |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| T 100% fits, default difficulty | 0 | 100 / 100 | 0 (0) | 0 (0) | 1800 / 0 / 0 | 99.5% | 100.0% | 97 | 164 | 0 | 0 | 0 | 0 | 30 | 14 |
| T 60% fits, default difficulty | 0 | 100 / 100 | 0 (5) | 0 (5) | 1800 / 0 / 0 | 60.4% | 60.5% | 0 | 1 | 0 | 0 | 0 | 0 | 30 | 12 |
| T 25% fits, default difficulty | 0 | 92 / 100 | 8 (9) | 9 (11) | 1792 / 7 / 1 | 25.2% | 25.5% | 0 | 0 | 0 | 0 | 0 | 0 | 32 | 10 |
| T 100% anywhere, fully random | 0 | 100 / 100 | 0 (0) | 0 (0) | 1800 / 0 / 0 | 99.5% | 100.0% | 97 | 164 | 0 | 0 | 0 | 0 | 30 | 13 |
| T 100% fits, strict curve | 0 | 100 / 100 | 0 (0) | 0 (0) | 1800 / 0 / 0 | 99.0% | 100.0% | 177 | 164 | 0 | 0 | 0 | 175 | 30 | 15 |
| T 60% fits, strict curve + Prevent same | 0 | 100 / 100 | 0 (5) | 0 (5) | 1800 / 0 / 0 | 58.1% | 60.5% | 417 | 1 | 0 | 0 | 0 | 73 | 30 | 12 |
| T 100% fits, strict curve + Prevent same | 0 | 100 / 100 | 0 (0) | 0 (0) | 1800 / 0 / 0 | 95.6% | 100.0% | 787 | 164 | 0 | 0 | 0 | 226 | 30 | 14 |
| T 25% fits, strict curve + Prevent same | 0 | 92 / 100 | 8 (9) | 9 (11) | 1792 / 7 / 1 | 24.3% | 25.5% | 165 | 0 | 0 | 0 | 0 | 44 | 32 | 10 |
| T 100% fits, very-loose curve | 0 | 100 / 100 | 0 (0) | 0 (0) | 1800 / 0 / 0 | 99.0% | 100.0% | 186 | 164 | 0 | 0 | 0 | 175 | 30 | 14 |
| T 60% fits + Prevent same, default difficulty | 0 | 100 / 100 | 0 (5) | 0 (5) | 1800 / 0 / 0 | 60.4% | 60.5% | 0 | 1 | 0 | 0 | 0 | 0 | 30 | 11 |

Reading the table: 0 final failures in 10 variants x 100 seeds, 0 fit violations, 0 Prevent-same violations, every group
coherent (all members one model), 0 effective-cap violations, bosses and units identical to `permutation-v1`, repeated runs
identical. **Retry pressure is not higher than in Part B**: at 25% a map needed a retry in 8 of 100 seeds (9 in Part B), at 60%
in 0 (5 in Part B), at 100% in 0 (0); no map needed a third attempt more than once in 1,800 maps. The map-local limit of 3 attempts is
sufficient and was not changed. The cost of Type replacement is not in retries but in the deliberate fallbacks: at 100%
about 1 group per seed stays vanilla (no candidate that fits and is distinct) and about 1.6 groups per seed share a
replacement (see "Why rung 4 exists"); at 25% neither happens.

Type replacement also lowers the Parish model count: one decision moves many slots to one model and frees the original, so
at 25% the Parish ended at 28-32 models (28: 15, 29: 92, 30: 276, 31: 11, 32: 6 of 400 seeds) against 30-33 without it.

**Parish (`m10_01`) seeds for manual in-game validation with Type replacement** (bosses on, Enemy scaling area, Gwyn nerf
medium, Second Gargoyle replace, `sampling-v1`, 25% replacement, fits, difficulty `random-easy-asylum`/`loose`, Type
replacement enabled; attempt-0 combined seed; checked through `generateRun`):

| Parish final models | master seed | notes |
|---|---|---|
| 31 | `parish-26` | baseline 31 |
| 32 | `parish-1` | baseline 32 |
| 30 (retry) | `parish-11` | the Parish was planned with attempt 2 (a map retry); Fog needed one extra combined attempt |
| 29 (retry) | `parish-34` | the Parish was planned with attempt 2 |

33 models does not occur with Type replacement at 25%; the 33-model seeds of 10.3 (`parish-28`, `parish-92`) are for the
non-type configuration.

**Real-format validation (Items + Enemies + Fog, bosses on, FogHost 0.5.0): 50 seeds + 3 full proofs, Type replacement enabled, `fits`.**

| Configuration | seeds | generated | Fog retries (`FOG_UPSTREAM_INCOMPATIBLE`) | merge conflicts | boss overlay | `verify-skips` failures | sampler overflow | changed without scaled row | groups / shared | max final models |
|---|---|---|---|---|---|---|---|---|---|---|
| 100%, default difficulty | 10 | 10 | 0 | 0 | same 15 events | 0 / 10 | 0 | 0 | 1,780 / 14 | 30 |
| 60%, default difficulty | 10 | 10 | 2 | 0 | same 15 | 0 / 12 | 0 | 0 | 1,780 / 0 | 30 |
| 25%, strict curve + Prevent same | 10 | 10 | 0 | 0 | same 15 | 0 / 10 | 0 | 0 | 1,780 / 0 | 30 |
| 100%, strict curve + Prevent same | 10 | 10 | 2 | 0 | same 15 | 0 / 12 | 0 | 0 | 1,780 / 17 | 30 |
| 60%, very-loose curve | 10 | 10 | 1 | 0 | same 15 | 0 / 11 | 0 | 0 | 1,780 / 1 | 30 |

Mismatches in events the combined engine edited: 0; the live installation was unchanged in every run. Every sweep ran with
bosses enabled, and their plans and the 15 overlay events are those of Part B. No map retry fell inside the 50 seeds (the
catalog sweep predicts about 1 seed in 12 at 25%), so three full proofs were run on seeds with and without a Parish
retry (`parish-11`, `parish-34`, `parish-1`): byte-identical reruns, activation and restore pass. The final model count and
the drop-ownership invariant were checked on the generated placement plans, not on the NpcParam regulation.

**For Part C2 (custom enemy pool; built, see 10.5).** Pass the pool through `createCandidatePool(table, accept)` / `candidateFilter`; nothing
else reads `table.types` for candidates (the in-set fallback and the shared rung filter by `pool.models`). A narrow pool
makes rung 4 and group-level failures more likely, so re-measure retry pressure and the share of groups kept vanilla, and
reject a pool that is empty for a size class that occurs in a map with a typed error before generation. The reserve
logic of slot mode and the ledger of group mode both assume that the original model stays available as a fallback, which a
custom pool must not change (the pool restricts replacements, not originals).

Batch 0 must also decide where `placement.js` lives: new code has to be inside `vendor/src` (that is what
the packager copies and what the loader imports), so it is a repository-authored GPL file recorded in
`UPSTREAM_PIN.md`, not an edit of an upstream file.

---

### 10.5 Part C2 as built (2026-10-05): custom enemy pool for regular enemies

**Legacy semantics confirmed (pinned revision 83f008d, `enemy_rng.py` / `enemy_randomizer.py`).**

| Item | Legacy behavior |
|---|---|
| Control | A named *enemy config*: a copy of `valid_new.txt` (`enemyRandomizerData/customConfigs/<name>.txt`) in which the third flag column of every row is rewritten by the editor (`SaveEnemyConfig`): `0` enabled, `1` ignored. "Default" is the shipped `valid_new.txt`. The selected config is read instead of `valid_new.txt` in `loadValidNew`. |
| Granularity | One row per model id (`c2500`, ...), covering normal enemies, bosses and NPCs (type 0 / 1 / 2) in the **same** list. A row carries one or more (AI, NpcParam) variants. |
| When it applies | Once, when the replacement lists are **built**: only rows with flag `0` (or `2` = Artorias DLC row on Remastered) enter the normal and boss candidate lists, the per-size lists and the per-difficulty lists. It is not a per-draw filter. |
| What it restricts | **Replacement candidates only.** The slots to replace come from `valid_replacements.txt` and are unaffected: an enemy that is not selected is still in the game where it stands and is still replaced like any other. |
| Size / difficulty lists | Built from the selected rows only, so a pool without a given size or difficulty class has empty lists there; the legacy fallbacks handle an empty list as for any other empty list (the sentinel results of 10.3/10.4). |
| Unselected originals | Not removed from the model list of a map; a map keeps the models its non-replaced slots still use. |

**Scope and parity.** The pool applies to **regular enemies** only. The legacy list is one global list that mixes bosses
and NPCs with normal enemies; `sampling-v1` does not sample bosses or NPCs yet, so those entries are not offered and the
editor shows no control for them. Parity is therefore **PARTIAL** (`FEATURE_PARITY.md`): the regular-enemy part of the
legacy config is reproduced, the boss and NPC entries are the remaining gap (Part D and later). No fake control exists.

**Identity and canonical form.** A pool is a set of stable model ids (`c2500`). The canonical form is the list sorted
ascending; duplicates, an empty list, non-ids and `null` handled as: `null` = the full pool (the neutral default),
duplicates / empty / malformed lists are refused with `ENEMY_PLACEMENT_CUSTOM_POOL_INVALID` (`duplicate`, `empty`,
`format`); an id that is not a regular candidate of the game data is refused as `unknown` and **never silently
dropped** (a stale selection after a game-data change is an error the user sees). Order is irrelevant; the hash is
`SHA-256({version: "custom-pool-v1", models})`. Listing every regular model is the full pool and produces the same
output as `null` (tested).

**Where the pool enters.** Only at the candidate boundary: `createCandidatePool(table, accept)` builds the replacement types
from the class table and `planSampledRegularEnemies` passes the pool as the accept predicate. Nothing else reads
`table.types` for candidates: the in-set fallback and the shared rung filter by `pool.models`. Filtering order is unchanged
(10.2): physical fit, Prevent same, Easy Asylum / difficulty, Type injectivity, model budget - the pool simply defines
which types exist. Fit, Prevent same, Easy Asylum, the structural exclusions and the model budget are never relaxed for it.
Originals outside the pool stay in the game and may be the vanilla fallback; **replacement** candidates (including the
Type replacement shared fallback) always come from the pool.

**Type replacement.** Groups draw from the same pool. The ladder of 10.4 is unchanged: sampled candidate, keep the group
vanilla, smallest fitting model already present, a model another group holds (shared fallback), failure. Rungs 3-4
(in-set / shared) only consider pool models; rung 2 (keep vanilla) may leave the group on an original that is not in the pool.

**What the investigation found (and the accounting that came out of it).** The first real-catalog runs showed that
restricted pools failed maps for which a valid final model set existed. A feasibility oracle (the exact smallest final
distinct-model count for the sampled participation state, a bounded exact cover search) classified every failing attempt:

| Pool | Cause | Class |
|---|---|---|
| B (43/85), 100% | the pool never reached the coverage reserve (`mapBudget` did not pass it on) | accounting |
| E (29/85, 60%, strict curve) | the reserve could be a larger slot's model instead of one that fits the smallest slot | accounting |
| C + Type (c2430 in Lost Izalith) | the map starts at 31 models against a cap of 30 (21 fixed + 10 original groups); the first group could neither stay (its model was released) nor take a pool model (the set was "full" of pending originals, none in the pool) | accounting (Type ledger) |
| C at 25% / 60%, E, B | a unique single-slot model that participates on a map the quiet slots already fill to 30: every option adds a model, 31 is the smallest count | feasibility (stochastic by participation) |
| D (2 large models), fits, 100% | the one replaceable slot (Parish `c2860_0000`) has no room, deterministically | structural |

The slot-mode "ledger" tried first (every participant's original counted as a pending owner from the start) made
results far worse (B at 100%: 7/100): it started most maps above the cap and then only admitted models that already had a
solid owner. It was dropped. What was built instead is small and based on the final model set:

- **Slots no pool model can ever replace** (nothing in the pool fits, or the only fit is the slot's own model under
  Prevent same) stay vanilla and their models join the **baseline** of the map (the models the sampler cannot remove).
  Restricted pools only; the full pool is treated as in Part C1.
- **Coverage witness** (`coverWitnessSearch`): the fewest further models that let every participating slot (or Type
  group) either keep its own model or take a fitting pool model (own model excluded as a replacement under Prevent same).
  Exact for the maps involved (a bounded branch-and-bound over a greedy cover, never more than every subject keeping its
  own model); it replaces the coverage reserve of a restricted pool. In slot mode the witness models are part of the
  model set from the start; in Type mode they are *insurance* in the ledger (`reserve` / `dropReserve`): they occupy
  capacity and may be taken at no cost, but are not owners and are not counted in the final set.

**Model budget: nominal target, baseline overflow, and the controlled grace.**

| Term | Meaning |
|---|---|
| nominal target | 30 distinct models per map (unchanged). |
| baseline | the models the sampler cannot remove: fixed plans, vanilla models of slots left alone (chance, mode `none`), and slots no pool model can replace. |
| base cap | `max(30, baseline)` (10.2). A map may already be above 30 before the sampler runs (Parish: 32-33 in a few seeds). |
| minimum feasible | `baseline + size of the proven-minimal cover`: the fewest models the map can hold for this participation state. |
| effective cap | the base cap, **except** the one-model grace below. |

*Controlled grace (decision of 2026-10-05).* A restricted custom pool may use `effectiveCap = 31` on a map **only if all**
of these hold: the base cap is the nominal 30 (baseline <= 30); the cover is proven minimal; the minimum feasible count is
exactly 31 (30 is proven impossible, 31 proven sufficient). Anything else keeps the base cap: a map whose baseline is
already above 30 gets **no** grace (baseline 31 stays 31, 32 stays 32, 33 stays 33, so the pool never creates a 32 / 33 /
34), and no map is ever granted more than 31. It is evidence-driven, not a new limit: it is not consumed when 30 suffices,
and 31 is a count `permutation-v1` already produces in valid output. 31 is **not** the new general model limit; the nominal
target and the baseline rule are unchanged, and the Parish 32-33 baseline risk stays a separate, unchanged item. The pure
function is `resolveModelCap`; per map the diagnostics record `nominalCap`, `minimumFeasibleModels`,
`customPoolModelGrace` (and the counter `customPoolModelGraceMaps`), next to `baseline`, `effectiveCap`, `finalModels`.
At a replacement chance of 100% every replaceable enemy takes part, so the verdict is exact before sampling: a map whose
minimum feasible count exceeds its cap even with the grace is refused up front (see errors); below 100% the participation
draw decides, and a draw that cannot fit is an ordinary map-local retry.

**Validation and errors.**

| Condition | Error | Retry |
|---|---|---|
| malformed / empty / duplicate list, unknown (stale) id, any pool under `permutation-v1` | `ENEMY_PLACEMENT_CUSTOM_POOL_INVALID` (`format`, `empty`, `duplicate`, `unknown`) or `ENEMY_PLACEMENT_OPTIONS_INVALID` | never |
| no regular slot can take any selected enemy (fit + Prevent same) | `ENEMY_PLACEMENT_CUSTOM_POOL_INVALID` (`no-candidate`) | never |
| chance 100% and a map's proven-minimal cover does not fit even with the grace | `ENEMY_PLACEMENT_CUSTOM_POOL_INVALID` (`map-infeasible`: map, baseline, effective cap, models needed, pool size, fit, Type) | never |
| `ENEMY_PLACEMENT_BUDGET_UNSATISFIABLE`, `CLASS_UNDERIVABLE`, `OPTIONS_INVALID` | typed, structural | never |
| a participation / draw state for which a map attempt does not fit | `ENEMY_PLACEMENT_INFEASIBLE` | at most 3 map-local attempts, then the typed error naming the pool hash and configuration |

Validation rejects a pool only when the impossibility is *proven*. Heuristics such as "reject a pool serving fewer than X% of
the slots" or "reject a pool whose smallest enemy is larger than some slot" were tried and discarded: the second rejects a
perfectly healthy pool of 78 of 85 models (everything except size 0) that the planner serves 100/100. The map-local retry
count stays 3.

**Coverage analysis.** `analyzePoolCoverage` reports the selected types per size class (0-5) and difficulty class (0-7),
the smallest selected size and the number of slots without a fitting candidate; `describeRegularEnemyPool` (and the
backend `POST /api/enemy-pool`, cached per pristine snapshot) lists every regular model with size, difficulty, slot and map
counts for the editor, computed from the clean game data. The renderer holds only the selection.

**Preset and UI.** `enemies.placement.customPool` (`null` or a sorted list); preset version 9 (versions 1-8 migrate to
`permutation-v1` with the full pool; a stale id is an error, never dropped silently). The editor offers search, All / None /
Reset, selected-count and per-size summary, and the hint that unselected enemies stay as they are. It is disabled unless
`sampling-v1` with regular enemies is active.

**Diagnostics.** `placementDiagnostics.customPool`: `enabled`, `hash`, `total`, `selected`, size and difficulty coverage,
`slotsWithoutFit`. Counters `candidatesOutsideCustomPool`, `keptByPool`, `typeGroupsKeptByPool`,
`customPoolModelGraceMaps`; per map `nominalCap`, `minimumFeasibleModels`, `customPoolModelGrace` (restricted pools only).
The log line names the pool as `custom pool X/Y (hash12)`.

**Measurements (real catalog, 100 seeds per row, 18 maps, bosses on).**

| Pool / configuration | success | map attempts 1 / 2 / 3 | grace maps | max final models |
|---|---|---|---|---|
| A full pool, 100% fits | 100 | 1800 / 0 / 0 | - | 30 |
| B (43/85) 100% fits; Type; strict curve; Prevent same | 100 each | 1800 / 0 / 0 | 0 | 30 |
| B 25% / 60% | 100 / 100 | 1796 / 3 / 1; 1798 / 2 / 0 | 6 / 3 (Parish) | 31 / 31 |
| C (10/85) 100%; Type (100%, 60%); strict curve; Prevent same; Type + strict + Prevent same | 100 each | 1800 / 0 / 0 | 0 | 30 |
| C 25% / 60% | 100 / 100 | 1796 / 3 / 1; 1798 / 2 / 0 | 11 / 6 (Parish, Lost Izalith) | 31 / 31 |
| E (29/85) 60% fits, strict curve | 100 | 1797 / 3 / 0 | 9 (Parish, Lost Izalith) | 31 |
| N (78/85: all but size 0) 100% fits; 25% + Type | 100 / 100 | 1800 / 0 / 0 | 0 | 30 / 32 (baseline) |
| D (2 large models) fits 100% | 46 ok, 54 `map-infeasible` | 828 / 0 / 0 | 42 (Parish) | 31 |
| D anywhere | 100 | 1800 / 0 / 0 | 0 | 23 |

Full pool at the same chances retries more often (25%: 9 seeds, 60%: 5 seeds with a map retry). In every run: zero fit,
Prevent-same and pool-membership violations; zero maps above their effective cap; zero changed placements without a
scaled NpcParam row; boss plans identical to the reference; deterministic reruns; and **the cap invariant**: every map
without the grace has `effectiveCap == max(30, baseline)`, every map with it has `baseline <= 30`, `effectiveCap == 31`,
`minimumFeasibleModels == 31`, and no map with a baseline above 30 ended above that baseline (53 such maps in the 25% runs,
18 in the N + Type run). The grace was used only on Parish (`m10_01_00_00`) and Lost Izalith (`m14_01_00_00`).

**Real-format validation (Items + Enemies + Fog, bosses on, FogHost 0.5.0): 50 seeds + 3 full proofs, `fits`, custom pool.**

| Configuration | seeds | generated | Fog retries (`FOG_UPSTREAM_INCOMPATIBLE`) | merge conflicts | boss overlay | `verify-skips` failures | sampler overflow | cap invariant / pool membership violations | changed without scaled row | groups / shared | max final models | grace maps |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| broad B (43/85), 100% | 10 | 10 | 2 | 0 | same 15 events | 0 / 12 | 0 | 0 / 0 | 0 | - | 30 | 0 |
| small C (10/85), 60% | 10 | 10 | 1 | 0 | same 15 | 0 / 11 | 0 | 0 / 0 | 0 | - | 30 | 0 (1 map retry) |
| small C, 25% (partial replacement) | 10 | 10 | 1 | 0 | same 15 | 0 / 11 | 0 | 0 / 0 | 0 | - | 33 (baseline) | 2: Parish in `c2-c25-4`, `c2-c25-9` (31 models) |
| C + Type replacement, 100% | 10 | 10 | 3 | 0 | same 15 | 0 / 14 | 0 | 0 / 0 | 0 | 1,780 / 16 | 30 | 0 |
| E (29/85), 60%, strict curve | 10 | 10 | 4 | 0 | same 15 | 0 / 14 | 0 | 0 / 0 | 0 | - | 31 | 4: Parish in `c2-e-1`, `c2-e-4`, Lost Izalith in `c2-e-6`, `c2-e-10` (31 models) |

Mismatches in events the combined engine edited: 0; Fog-only (stock FogMod) event mismatches: 0; the 15 boss overlay events are
identical to Part C1's; the live installation was unchanged in every run; every sweep ran with bosses enabled. The six
real-format maps that used the grace ended at exactly 31 models, none of them had a baseline above 30; five maps with a
baseline above 30 (25% sweep) kept it (33 at most) and did not grow. The drop-ownership invariant (every changed placement
has a scaled row) and pool membership (every sampled placement is a pool model) were checked on the generated plans, not on
the NpcParam regulation. Three full proofs (generation, byte-identical rerun, activation, restore) ran on the grace seeds
`c2-c25-4` (Parish) and `c2-e-6` (Lost Izalith) and on the Type + pool seed `c2-ct-1`: byte-identical reruns, activation (72-73 files) and restore pass, and the live installation is unchanged.


**Regression.** `permutation-v1`: the 24 lock hashes are identical to the pre-C2 values and the package files of the combined-only (56 files, two
configurations) and combined + Fog (73 files) runs are unchanged. `sampling-v1` with the full pool or a listed full pool is byte-identical to Part C1
(pins A, B, C1 and 700 real-catalog comparisons: output, `classTableHash`, retry counts).

**Residual risk and notes for Part D.** The pool is regular-enemy only; boss / NPC pool entries need Part D. Heavily
restricted pools leave many slots vanilla by design. Pools whose replaceable slots cannot fit a map at 100% are refused
with `map-infeasible` (D at fits is the reference case). The Parish 32-33 baseline risk of 10.3 is unchanged and not
worsened.

---

### 10.6 Part D as built (2026-10-06): boss sampling for single-body bosses

**Scope.** `sampling-v1` with `enemies.placement.bossReplacement: "bosses"` draws a replacement for every *single-body* boss slot
(24 on the real catalog) from the portable boss pool: the boss archetypes the classic planner already shuffles (the same `sources`
list, `nonPortableBossReplacementModels` empty). A boss may appear any number of times. `"classic"` (the neutral value and the default)
runs the existing archetype permutation byte for byte. **Linked units stay classic in both modes**: Crossbreed Priscilla, the two Black
Dragon Kalameet bodies, Gaping Dragon, Seath, the Sanctuary Guardian, the Bell Gargoyles and the Centipede Demon (the bosses of the
dragon / linked-boss planners; they swap with each other as whole units). **Correction (2026-10-06):** an earlier version of this text
also listed Four Kings and Bed of Chaos; both are ordinary members of the 24-slot boss pool (the same as in the permutation) and take
part in boss sampling like the other 21 archetypes. Chance-based unit participation and unit sampling are not built; the "unit participation" item of the plan is deferred. The cross-class modes, roaming-boss souls and respawn are Part E1 (10.7);
`bossChanceInBossSlots` (a regular enemy as the body of a boss encounter) is Part E2, a safe subset of three encounters (10.8).

| Option | Contract | Neutral | Rule |
|---|---|---|---|
| Boss replacement | `enemies.placement.bossReplacement` `classic` / `bosses` | `classic` | sampling-v1 only; needs Bosses on to have an effect |
| Unique bosses | `enemies.placement.uniqueBosses` | `false` | needs `bossReplacement: "bosses"`; refused otherwise |
| Replacement chance | `enemies.placement.replacementChance` (existing) | 100 | now also decides, per boss slot, whether it takes part (first draw of its own stream) |
| Gwyn spawn nerf | `enemies.gwynNerf` (existing) | - | per candidate: Lord Gwyn is rejected with 60% (medium) / 85% (strong), a draw per evaluation from the slot's stream; the classic per-seed coin and the Second Gargoyle draw are unchanged |

**Planner** (`planSampledBosses`, `sampling.js`). Per slot in stable id order, each with its own stream (`slot:<id>`, `enemy-placement-v1`,
disjoint from regular slots): (1) replacement chance - a slot that fails it stays vanilla and gets no placement; (2) candidates = the
portable archetypes except the slot's own boss and the authored unsafe pair (a Ceaseless Discharge slot never receives the c5250
archetype, the rule of the permutation); (3) uniform draw with list shrink; rejected: Gwyn by the nerf, an already used boss while an
unused candidate remains (Unique bosses; the cycle restarts when every boss of the pool was used; the legacy 22 is not copied); (4) nothing
left: the slot stays vanilla (`bossesKeptVanilla`). Every sampled placement is slot-owned (`forceScaledNpcParam`, rule 6.9) and carries the
tag `sampled-boss-replacement`. Boss counters are separate from the regular-slot summary (`bossSlots`, `bossesReplaced`,
`bossesKeptVanilla`, `bossSlotsNotParticipating`, `bossRejections:<reason>`); the log line gains "bosses sampled x/y".

**Model budget.** No boss cap exists (legacy: bosses are exempt). The models of the boss placements are fixed models of their maps for the
regular-enemy planner (`regularEnemyContext`), so the regular sampler respects them exactly as before; unchanged boss slots count with their
vanilla model.

**Defect found and fixed while building.** The first sweep failed 36 of 40 seeds in `patch-enemies` with "Conflicting custom NpcThinkParam
definition: 9700002": a boss slot with deferred activation names its think-param row `9,700,000 + source archetype index`, which was only
consistent because the permutation gives every slot of an archetype the same replacement. Sampled bosses differ per slot, so each slot now
uses its own id (`9,750,000 + index`), only in the sampled path.

**Evidence.** Real format (clean game copy, Items + Enemies + Bosses + Fog, Gwyn nerf strong, Second Gargoyle replace, `verify-skips` on every
combined attempt and on the final Fog package):

| Sweep | Seeds | Result | Boss overlay events | Instruction merges | `verify-skips` |
|---|---|---|---|---|---|
| classic bosses under sampling-v1 (reference) | 40 | 40 success, first attempt | the 15 known events, each in 40 seeds | 12 events | 40/40 clean |
| `bosses`, chance 100 | 40 | 40 success, first attempt | the same 15 events, each in 40 seeds | the same 12 | 40/40 clean |
| `bosses` + Unique, chance 60 | 40 | 40 success | the same 15 events, 17-27 seeds each (a boss slot left vanilla does not edit its event) | the same 12 | 40/40 clean |
| `bosses`, Enemy scaling `vanilla` | 20 | 20 success | the same 15 events, each in 20 seeds | the same 12 | 20/20 clean |

0 merge conflicts, 0 changed placements without a scaled row, 0 maps above the cap (maximum 30 models), no event outside the known set.
Neutral identity: the 24 permutation lock hashes and the sampling pins A/B/C1 are identical to the values before the change; all
golden fixtures (the original 10, the two recorded before Part D with sampling-v1 and the classic boss planner, and the two boss-sampling
cases) reproduce from their Seed Codes, also under a different application version.

**Open.** (a) Unit participation by chance and unit sampling (Part D2). (b) Whether a map can reach more than 30 models through repeated
bosses was not observed (maximum 30 in 100 seeds). (c) In-game behaviour of repeated bosses (two copies of one boss in different maps, a
boss in an arena built for another) is covered by the same compatibility rules as the permutation but has not been played.

### 10.7 Part E1 as built (2026-10-06): bosses in regular enemy slots ("roaming bosses")

**Scope.** `sampling-v1` with `enemies.placement.normalReplacement` `bosses` or `both` lets bosses stand in regular enemy slots. This is Part E1.
**Part E2 (a regular enemy as the body of a boss encounter, legacy "Boss chance, boss slots") is built as a safe subset; see 10.8.** NPC and hostile-NPC
replacement (Part F) is untouched.

#### Legacy semantics (pinned Enemy Randomizer `83f008d`; reconstructed, not copied)

| Legacy element (source) | Behavior |
|---|---|
| "Normal enemy replacement" (`randomizer.py`, config position 1) | 0 none, 1 only bosses, 2 only normal enemies, 3 either. With 1 every participating normal slot receives a boss; with 3 the slot receives a boss with the chance of "Boss chance, normal slots" (position 7, default 10), otherwise a normal enemy. |
| Participation | The replacement chance is the first draw of a slot (or of a Type replacement entry); a slot that fails it keeps its enemy in every mode. |
| Souls (`NpcParam.applyBossSouls`, `BossSouls`) | A boss in a normal slot uses a copy of its NpcParam row (id + 50, "<boss> with souls") whose `getSoul` is `int(authored souls of the boss fight * percent / 100)`. The table is authored per NpcParam row (Asylum Demon 2000, Taurus 3000, Capra 6000, Gaping 25000, Gwyn 70000, ...). Almost every vanilla boss row has `getSoul` 0 in the real catalog (the arena awards the reward; only Kalameet and the Anor Londo Gargoyles carry a value), which is why the copy exists. Percent default 50. |
| Respawn (`AddRespawnEventInit`, template event 0 of `eventTemplates.dkscript`) | "Respawning roaming bosses: stay dead" initializes one event per entity (a normal slot receives an entity id if it has none) that makes the boss stay dead. The NpcParam copies themselves are written with `disableRespawn` = false at both call sites. |
| Type replacement "except roaming bosses" | A replacement whose new type is a boss is not stored for the other slots of the original type; normal replacements are. |
| Difficulty, Placement fit, Prevent same | Apply to the boss draw like to any draw (size class of the boss against the slot, curve class, own model). |
| Unique bosses | Only the boss-replaces-boss draw (step 7); not the boss-in-normal-slot draw. |
| Boss-in-normal-slot restrictions | None beyond the generic ones; the hard-coded pair rejections concern boss *destinations* (Gwyndolin, Capra, Iron Golem, Stray Demon, Pinwheel, Ceaseless Discharge slots). |

#### What DS1TR builds

| Option | Contract | Neutral | Rule |
|---|---|---|---|
| Normal enemy replacement | `normalReplacement`: `none` / `normals` / `bosses` / `both` | `normals` | `bosses` and `both` need `sampling-v1`; refused under `permutation-v1` |
| Boss chance in normal slots | `bossChanceInNormalSlots` 0-100 | 10 | needs `both`; refused otherwise |
| Roaming boss souls | `roamingBossSouls` 0-100 (percent) | 50 | needs `bosses` / `both` |
| Roaming bosses stay dead | `roamingBossesStayDead` | `false` | needs `bosses` / `both` |
| Type replacement | `typeReplacement` (existing) | `disabled` | `enabled`: a whole kind in a map gets one boss; `enabled-except-roaming-bosses`: each slot of the kind decides on its own (the legacy distinction; the exception is now real) |

**Planner** (`planRoamingBosses`, `sampling.js`). A *second pass* after the unchanged regular sampler. The sampler runs with `normalReplacement` `none`
(`bosses`) or `normals` (`both`); the pass only *overrides* slots, so a slot that gets no boss keeps exactly the placement it had without the pass
(proved by the sweeps: every slot outside the pass is byte-identical to the same world with `normals`/`none`). Per map, in stable slot order (a Type
replacement group in `enabled`): (1) participation = the first draw of the slot's / group's existing stream (replacement chance); (2) category: `bosses`
always, `both` a draw from the named stream `roaming:slot:<id>` / `roaming:type:<map>:<model>` against the boss chance; (3) per-map limit of 10 roaming
bosses (a group counts all its slots); (4) candidates: fit, Prevent same, the difficulty class(es) of the curve, the Easy Asylum rule, uniform draw with list
shrink from the same stream; (5) **model budget** admission: the boss is accepted only if the final model set of the map (fixed models and the final model
of every slot) stays within `max(30, its size before the pass)` - the non-worsening rule, **no** Custom Pool grace. A group with a member that cannot take
a boss (protected slot) stays whole. New streams are derived under `enemy-placement-v1` with names that did not exist before, so no earlier draw moves.

**Roaming candidates.** One archetype per boss with an authored souls entry (`bossKillSouls`, generator.js) that is not on the safety exclusion list:
Stray Demon, Demon Firesage, Asylum Demon, Capra Demon, Taurus Demon, Iron Golem, Smough, Super Smough, Moonlight Butterfly, Pinwheel, Knight Artorias,
Manus, Sif, Gravelord Nito, Dragon Slayer Ornstein, Super Ornstein, Chaos Witch Quelaag. **Never roaming (DS1TR safety boundary, a conscious restriction, not a legacy rule):**
Bed of Chaos (immobile, floor script), Ceaseless Discharge (immobile, 10 m body), Dark Sun Gwyndolin (neutral team, stationary, scripted), Lord Gwyn (scripted
entrance), and every multi-body boss (Priscilla, Hellkite, Sanctuary Guardian, Kalameet, Centipede Demon, Gaping Dragon, Seath, Bell and Anor Londo Gargoyles,
Four Kings). The exclusion holds with or without boss randomization (the dragon plan only reserves those bosses when bosses are randomized).
**Protected slots** never take a boss: the passive slots (Asylum, New Londo, the Kiln knight, the Parish bonfire Titanite Demon), tangible-ghost slots,
the Darkroot butterfly path, slots whose events lock their model (`eventModelLocked`) and Pinwheel Chaos clones.

**A roaming boss placement** (`decorateEnemyPlacements`): compatibility `sampled-roaming-boss`; `scaling: "vanilla"` (the boss keeps its own combat
stats; drops and team still follow the slot through the scaled NpcParam row, the model document 6.9 rule); `roamingBossSouls = trunc(souls * percent / 100)`
written to the scaled row's `getSoul`; `disableRespawn: true` (when "stay dead") written to the scaled row's own `disableRespawn` bit; the standard
wake-up event (`forceCombatActivation`, an entity id is assigned if the slot has none) that every portable boss gets; no boss bar, no fog gate, no
boss-death event (a regular slot has none).

**Deliberate deviations.** (1) *Respawn is a field, not an event.* (The row is a copy of the boss row, whose `disableRespawn` is already 1; since backend 0.35.1 the bit is written for both values of the option. Before that, "stay dead off" did not clear it: `GENERATION_COMPATIBILITY.md`, "Recorded exception".) The roaming boss that stays dead was played in the real game and stayed dead after the reset; the respawning variant is statically verified and not played yet. The game's `NpcParam.disableRespawn` bit exists in the paramdef; setting it on the
scaled row needs no custom event (legacy writes an event per entity). It has not been seen working in the game - treat "stay dead" as unverified.
(2) The per-map limit of 10 is DS1TR's (legacy has none; `bosses` at 100% would otherwise place a boss in every slot). (3) The exclusion list above.
(4) Difficulty class of a boss uses the regular formula (map tier and HP rank); a boss's HP is above every regular enemy, so bosses are class 3-7 and the
curve modes keep them out of the earliest maps and the Easy Asylum rule keeps them out of the Asylum. (5) The budget is the non-worsening rule of 10.3.
(6) Unique bosses does not apply to roaming bosses (as in the legacy). (7) Placement fit uses an authored size class for the Moonlight Butterfly (class 3, `roamingSizeClassOverrides`) instead of the class of its 0.4 x 0.2 hit capsule: the capsule of a flier does not describe its body, and without the override "only where they fit" would put the Butterfly into almost every slot (27 of 31 roaming bosses in the first smoke world).

**Seed Codes.** The three options are *extension keys* (stored in a code only when different from the neutral values 10 / 50 / false); `normalReplacement`
gains the values `bosses` and `both`. A code made before Part E contains none of them, decodes to the neutral values and re-encodes to the same text; its
world is the same world (all 15 golden fixtures of `ds1tr-gen-v1` verify byte for byte with the Part E engine and data tool). The profile stays `ds1tr-gen-v1`: the
change is additive and gated behind options an old code cannot have. New worlds are pinned by `ds1tr-gen-v1.roaming-bosses.json`. Presets: version 11.

**Data tool** (patch 0014). `PatchPlacement` gains `roamingBossSouls` and `disableRespawn`; the scaled NpcParam row sets `getSoul` / `disableRespawn` and
asserts them (the other cells stay asserted equal to the boss row). Events: one wake-up event per roaming boss inside the map's custom event window
(`docs/EVENT_ALLOCATION.md`); nothing is added to the vanilla events and nothing to the Fog whitelist.

#### Measurements and proofs

All on the real extracted catalog of a clean Dark Souls Remastered copy; zero unexplained failures; nothing was retried and no limit was relaxed.

| What | Result |
|---|---|
| Engine, 18 configurations x 100 seeds (`roamingSweep.mjs`: normal baseline, boss-sampling baseline, bosses and "either" at several chances / fit / difficulty / Prevent same / Type replacement enabled and except-roaming / souls 0 and 100 + stay dead / Unique bosses / Gwyn nerf / Second Gargoyle / Pinwheel Chaos) | 1800/1800 worlds: deterministic; every roaming boss is an allowed archetype with the right souls, respawn bit, vanilla scaling, scaled row and wake-up; at most 10 per map; no roaming boss on a protected or passive slot; the final model count of a map is never above `max(30, count without the pass)`; every slot the pass did not take is byte-identical to the same world without the pass |
| Real data tool, `placementMatrix.mjs roaming-e1` (before the Butterfly size override): 6 cases x 100 seeds and 8 cases x 60 seeds | 1080/1080 generated, 0 retries, 0 event-window overruns, 0 non-deterministic reruns |
| Same matrix after the override: 10 cases x 40 seeds | 400/400, same invariants |
| Baselines through the real tool (`type-event-former` 100: Type replacement enabled, Placement anywhere, bosses on = the case of the event-ID fix; `type-event-matrix`: curve at 60% and boss sampling, 100 each) | 300/300 |
| Fog (real format): 6 configurations x 10 seeds, run twice (before and after the override), `verify-skips` on every package | 120/120 generated, 0 retries, 0 conflicts, the same 15 boss-overlay events and 12 instruction merges as without roaming bosses, 120 skip checks, 0 failed |
| Model limit | 30 in every map except the Undead Parish, which reaches 33 (30 + dragon/boss models) in the same worlds without roaming bosses; never above its own baseline (`mapsAboveCap` never counts a map worse than before) |
| Custom event window | at most 122 events in one map (Demon Ruins, Type replacement except roaming) against a 100-ID window; in 16 of the Type-replacement seeds the window moved off the Bed of Chaos floor event, which is exactly what `docs/EVENT_ALLOCATION.md` provides; 0 overruns |
| Read back from the generated `GameParam` of real packages (`roamingProbe.mjs` + an NpcParam reader) | every roaming row has the planned `getSoul` and `disableRespawn` (71/71 and 109/109) |
| Compatibility | all 15 earlier golden fixtures of `ds1tr-gen-v1` verify byte for byte with the Part E engine and data tool; 3 new fixtures pin roaming worlds |
| Tests | backend 393 tests (391 pass, 2 skipped by design), desktop 6/6 |

**Not verified by playing:** wake-up, navigation, killability, souls, "stay dead", textures and crashes with up to ten bosses per map. The six Part E worlds under Advanced, Test worlds for playtesting (`docs/SMOKE_TESTS.md`) are made for that.

---

### 10.8 Part E2 as built (2026-10-07): a regular enemy as the body of a boss encounter (safe subset)

**Scope and verdict.** `sampling-v1` with `enemies.placement.bossReplacement` `normals` or `both` lets a **normal enemy** stand in a boss arena. DS1TR builds this for
**three audited boss encounters only: the Asylum Demon, the Taurus Demon and the Capra Demon.** Every other boss encounter keeps its boss (`normals`) or gets a boss
(`both`), for the reasons in "Boss encounter audit". This is a *safe subset*, not the whole legacy feature: the legacy tool puts normal enemies into every boss
slot; DS1TR does not claim that and shows the three encounters in the control's hint. **The result is STATICALLY VERIFIED / GAMEPLAY UNVERIFIED**: the generated files
contain what the design needs and the events were proven unchanged beyond Part D's, but nobody has played an E2 world yet (`docs/SMOKE_TESTS.md`).

#### Legacy semantics (pinned Enemy Randomizer `83f008d`; reconstructed, not copied)

Evidence: `randomizer.py` (UI, config string), `randomizer_rng.py` (the draw), `NpcParam.py`, the shipped `eventscripts`. Nothing below is copied; where the legacy source
shows no code for a question, that is said.

| # | Question | Legacy behavior |
|---|---|---|
| 1 | Option name and values | "Boss replacement": 0 don't, 1 only with bosses, 2 only with normal enemies, 3 with bosses or normal enemies. Config position 1. |
| 2 | Chance | "Boss chance [Bosses] (%)", default 90, config position 8; only enabled with mode 3. A boss slot draws a boss with that chance, otherwise a normal enemy. |
| 3 | Participation | The replacement chance is the first draw of a slot; a boss slot that fails it keeps its boss in every mode. |
| 4 | Which slots | Every slot of creature type "1" (boss) in every map the tool covers: no per-boss audit. Mode 2 puts a normal enemy into *every* boss arena. |
| 5 | Pool | `GetNormalEnemy`: the type "0" rows of the enemy table that pass the difficulty mode, size (Placement fit), Prevent same and, **for boss slots, no unique-enemy limit** (`careAboutLimit` False). |
| 6 | Entity ID | The part is rewritten in the MSB (model, NpcParam, think); its entity ID stays. The boss events keep waiting for that entity. |
| 7 | Death detection | Vanilla events: the arena's events wait for the entity's death; the tool shows no change of that logic for the maps it did not replace the events of (15). |
| 8 | Completion | The boss-defeated flag, the fog gate and the reward event hang on that death; the new body inherits them through the entity. |
| 9 | Health bar | The vanilla health-bar instruction targets the entity; no code that renames the bar or the name was found, so a normal body shows the bar with the boss's name from the event / name id. |
| 10 | Name | See 9 (no rename logic found). |
| 11 | Scaling | No scaling code exists in the legacy tool: the body uses its own NpcParam row (its own HP, damage and souls); `ApplyBossSoulCount` only concerns bosses placed in normal slots. |
| 12 | HP | The body's own HP (a Hollow in the Asylum Demon arena has Hollow HP). |
| 13 | Drop / reward | No code found that moves the encounter's reward to the body or the body's lot away: the arena events award the encounter's reward, the part keeps the body's own NpcParam item lot. |
| 14 | Respawn | No special handling found for a body in a boss slot. |
| 15 | Exclusions | Hard-coded special cases: Seath (scripted death), the Key Serpent (key drop), Gwynevere, flying Kalameet (`c4510_0000/0002`), the Pinwheel clones/main body; whole-file EMEVD replacements for `m12_01`, `m13_00`, `m14_01`, `m15_00`, `m15_01`, `m17_00`; the Asylum Demon is moved to a fixed position (`c2232`). |
| 16 | Multi-body | Not generalized: each part is replaced on its own; the encounters with several bodies are covered only by the per-map event replacements and special cases of 15. |
| 17 | Second Gargoyle | A separate option (Gargoyle mode): the second body is replaced on its own. |
| 18 | Ornstein & Smough | Replaced part by part inside the Anor Londo event replacement (`m15_01`); no general rule. |
| 19 | Four Kings | A single catalog part stands for the encounter; no dedicated handling was found in the files read. |
| 20 | Fog interplay | None (the legacy tool predates FogMod); the combined project must decide it. |

**Reading.** The legacy approach for a body in a boss slot is *entity-preserving*: keep the entity ID, change only the MSB columns, rely on the vanilla events. It works where
the arena events are plain (Asylum Demon, Taurus Demon, Capra Demon, which sit in maps the legacy tool needed **no event replacement** for) and needs event surgery in the
maps it replaced. DS1TR keeps exactly that boundary.

#### Boss encounter audit

Categories: SAFE_SINGLE_BODY (one entity, plain events), SAFE_WITH_ADAPTER (one entity, needs an event edit DS1TR already makes), SPECIAL_SINGLE_BODY (one entity,
special behavior), MULTI_BODY (several bodies or phases), SCRIPTED_MULTI_ENTITY (a script spawns or drives other entities), TRANSFORMATION (the body changes),
CUTSCENE_DEPENDENT (the fight is started or ended by a cutscene/scripted animation), UNSUPPORTED. *Basis* names the evidence: the catalog flags (`riskFlags`, team,
`talk`, `patrol`), the legacy event surgery list (15), Part D's special handling in the generator, and the authored boss tables. Where only "not audited" is possible the
class says so; nothing here was played.

| Encounter (slot) | Class | E2 | Reason |
|---|---|---|---|
| Asylum Demon (`m18_01 c2232_0000`, entity 1810800) | SAFE_WITH_ADAPTER | **supported** | One entity; completion depends on its death only. Its intro event `11810310` plays model-specific animations: Part D's `patchAsylumIntro` already removes them and re-enables AI (the same edit; proven identical for an E2 body). Legacy needed no event replacement in this map. |
| Taurus Demon (`m10_01 c2250_0000`, 1010700) | SAFE_WITH_ADAPTER | **supported** | One entity; `model-specific-event` is handled by the portable-boss sanitization Part D already applies. Map outside the legacy surgery list. |
| Capra Demon (`m10_01 c2240_0000`, 1010750) | SAFE_SINGLE_BODY | **supported** | One entity, no model-specific flag. The two dogs are regular enemies with their own entities; the fight does not wait for them. |
| Stray Demon (`m18_01 c2230_0000`) | SPECIAL_SINGLE_BODY | kept | `patrol` and `model-specific-event`: the second Asylum visit with a patrol route; not audited. |
| Bell Gargoyles (`m10_01 c5350_0000`, `c5350_0002`) | MULTI_BODY | kept | Two bodies, the fight ends when both are dead. The Second Bell Gargoyle option handles the second body on its own. |
| Gaping Dragon (`m10_00 c5260_0000`) | SPECIAL_SINGLE_BODY | kept | Dragon-class body (classic dragon shuffle only); `model-specific-event`; entrance not audited. |
| Crossbreed Priscilla (`m11_00 c2730_0000`) | SCRIPTED_MULTI_ENTITY | kept | Neutral until attacked (`talk`, team 1), invisible-tail logic; dragon-class plan. |
| Moonlight Butterfly (`m12_00 c3230_0000`, also in `m12_00_00_01`) | SPECIAL_SINGLE_BODY | kept | Flier; the same entity exists in two map files; the arena start is scripted. Not audited for a ground body. |
| Great Grey Wolf Sif (`m12_00 c5210_0000`) | CUTSCENE_DEPENDENT | kept | Starts from the scripted kneeling idle and cutscene; `model-specific-event`. |
| Sanctuary Guardian (`m12_01 c3471_0000`) | SPECIAL_SINGLE_BODY | kept | Dragon-class plan; Oolacile events are replaced by the legacy tool. |
| Knight Artorias (`m12_01 c4100_0000`) | CUTSCENE_DEPENDENT | kept | Oolacile arena events (legacy replaced the file); scripted entrance. |
| Manus (`m12_01 c4500_0000`) | CUTSCENE_DEPENDENT | kept | Same map; scripted entrance, Abyss arena events. |
| Black Dragon Kalameet (`m12_01 c4510_0000`, `c4510_0002`) | MULTI_BODY | kept | Two entities (ground and flying); the legacy special-cases the flying ones. |
| Pinwheel (`m13_00 c3320_0000`) | MULTI_BODY | kept | Main body plus 14 clones (Pinwheel Chaos); legacy replaced the map events. |
| Gravelord Nito (`m13_01 c5220_0000`) | SCRIPTED_MULTI_ENTITY | kept | Skeleton entities are part of the fight; `model-specific-event`. |
| Chaos Witch Quelaag (`m14_00 c5280_0000`) | CUTSCENE_DEPENDENT | kept | `talk`, her sister's events and an intro; `model-specific-event`. |
| Demon Firesage (`m14_01 c2231_0000`) | SAFE_WITH_ADAPTER (candidate) | kept | Single entity, but the legacy tool replaced the whole map events of `m14_01`; the map's Fog overlay and Bed of Chaos floor events make a first release unsafe without a game test. |
| Ceaseless Discharge (`m14_01 c5200_0000`) | UNSUPPORTED | kept | Immobile 10 m body; a normal enemy cannot reach the player. |
| Bed of Chaos (`m14_01 c5230_0000`) | UNSUPPORTED | kept | Immobile, floor script, two bodies. |
| Centipede Demon (`m14_01 c5250_0000`) | CUTSCENE_DEPENDENT | kept | Rises from lava on a scripted entrance; the map events are legacy-replaced. |
| Daughter of Chaos (`m14_01 c5401_0000`) | SCRIPTED_MULTI_ENTITY | kept | 1 HP shell; Part D's own backread edit. |
| Iron Golem (`m15_00 c2320_0000`) | SAFE_WITH_ADAPTER (candidate) | kept | Single entity, but Sen's Fortress events are legacy-replaced and the fog gate scripts are not audited. |
| Smough (`m15_01 c2360_0000`), Dragon Slayer Ornstein (`c5270_0000`), Super Ornstein (`c5271_0000`) and the Super Smough semantic slot | MULTI_BODY | kept | Two kinds of body; the second to die is empowered by events (the "Super" bodies); the encounter completes after both. |
| Dark Sun Gwyndolin (`m15_01 c5320_0000`) | UNSUPPORTED | kept | Neutral team, stationary, scripted; `friendly-or-neutral`. |
| Anor Londo Gargoyles (`m15_01 c5351_0000`, `_0001`) | MULTI_BODY | kept | Two bodies. |
| Four Kings (`m16_00 c5390_0000`) | SCRIPTED_MULTI_ENTITY | kept | One catalog part for an encounter whose kings are spawned by script. |
| Seath the Scaleless (`m17_00 c5290_0001`) | TRANSFORMATION | kept | Immortality phase and scripted death (legacy special case). |
| Gwyn, Lord of Cinder (`m18_00 c5370_0000`) | CUTSCENE_DEPENDENT | kept | Scripted entrance; Gwyn spawn nerf; not audited. |

**Rule of thumb that produced the three.** An encounter is supported only if (a) it is a single entity whose completion is that entity's death, (b) its map is not on the legacy
event-surgery list, and (c) the model-specific edits it needs are edits Part D already makes for the same slot. Adding a fourth encounter is a documented decision plus a
game test, not a change of a constant alone.

#### The encounter wrapper (centralized)

There is no per-boss code. The "participant" is a boss part (its slot, entity ID and events stay) whose *body* is another model; the wrapper is made of rules that already
existed and one new one:

| Responsibility | Mechanism |
|---|---|
| Entity identity | The slot keeps its entity ID (`encounterBody` placements are boss placements; the data tool never changes an entity ID). |
| Activation and AI wake-up | Part D's portable-boss rules: `forceCombatActivation` (wake-up with the arena), the base think row of the body's model (`activeReplacementThinkParams`), the safe ground position of the arena. |
| Health bar and name | The vanilla health-bar events keep targeting the entity. The bar keeps the **original boss name** on purpose (see "Deliberate deviations"); `PatchBossNames` is not applied to a body. |
| HP and scaling | A scaled NpcParam row is written for the body (`forceScaledNpcParam`). With Area / Progressive scaling the body gets the boss's HP and strength (so the fight has a boss's length); with Vanilla scaling it keeps its own stats. |
| Death and completion | Unchanged vanilla events: they wait for the entity's death. The static verifier proves that no condition, flag-write or event-initializer instruction of an event naming the entity was removed (except the model-specific forced animation `2003[18]` aimed at the encounter's own entity - the Asylum Demon drop and the Taurus Demon entrance - the same sanitization as Part D). |
| Respawn | `disableRespawn = 1` is written to the body's row (**new, `EncounterOwnedCells` in `DsrDataTool`**). |
| Reward and drop ownership | `dropType`, `getSoul`, `disableInitializeDead` and the item-lot cells come from the boss row: the encounter owns the reward, the body's own drops do not appear. |
| Music, camera, cutscene | Not touched; the supported encounters have no cutscene. |
| Fog compatibility | No new Fog overlay and no new event: the events that Fog rewrites for these arenas are the ones it already rewrites for Part D (proof below). |
| Event IDs | None added by E2: nothing to allocate; the corrected allocator (`EVENT_ALLOCATION.md`) is unaffected. |

#### What DS1TR builds

| Option | Contract | Neutral | Rule |
|---|---|---|---|
| Boss replacement | `bossReplacement`: `classic` / `bosses` / `normals` / `both` | `classic` | `bosses`, `normals`, `both` need `sampling-v1` and boss randomization |
| Boss chance in boss slots | `bossChanceInBossSlots` 0-100 | 90 | needs `both`; refused otherwise; the control is shown only with `both` |
| Unique bosses | `uniqueBosses` | `false` | needs `bosses` or `both`; bodies never take part in the cycle |

**Planner.** `planSampledBosses` (`sampling.js`) gains `bodies`. A boss slot that takes part (the participation draw is unchanged) and is in the audited set gets, in `normals`, a body; in
`both`, a category draw from its own stream `category:slot:<id>` against the chance (so with the chance at 100 `both` equals boss sampling exactly). The body is drawn from `body:slot:<id>`:
candidates are the audited body models (`c2500` Hollow, `c2540` Armored Hollow, `c2550` Undead Soldier, `c2560` Balder Knight, `c2520` Undead Assassin, `c2790` Black Knight,
`c2410` Silver Knight, `c2390` Darkwraith, `c2810` Infested Barbarian, `c2690` Serpent Soldier, `c2800` Undead Crystal Soldier: ordinary awake ground melee enemies with a plain brain; no
passive, ambush, ghost, mimic, flier, statue, NPC or boss-family model), filtered by Placement fit against the destination class, the Custom Pool, the difficulty mode (the curve
looks for the nearest class the pool has) and the **model budget** `max(30, the map's vanilla model count)` against the running counts of the pass. A slot with no candidate keeps its
boss in `normals` and draws a boss in `both`. New streams use names that did not exist before: no earlier draw moves, and neutral configurations draw nothing new.

**Seed Codes.** `bossChanceInBossSlots` is an *extension key* stored only when different from 90; `bossReplacement` gains the values `normals` and `both`, which an earlier code cannot
contain. Every earlier code decodes to the same settings, re-encodes to the same text and generates the same world (all 18 earlier golden fixtures verify byte for byte). The profile stays
`ds1tr-gen-v1`. New worlds are pinned by `ds1tr-gen-v1.encounter-bodies.json`. Presets: version 12.

**Data tool** (patch 0016). `PatchPlacement.EncounterBody`; for such a placement the scaled row copies the encounter-owned cells (`dropType`, `getSoul`, `disableInitializeDead`, the item-lot cells) from the boss row,
sets `disableRespawn` to 1 and asserts them. Nothing is added to the events; nothing is added to the Fog whitelist.

**Deliberate deviations.** (1) Only three encounters (above). (2) The health bar keeps the boss's name: a Balder Knight in the Asylum Demon arena shows "Asylum Demon". The alternative, renaming the bar through `PatchBossNames`, needs a boss-name id per
body model that the game does not have; functional completion beats visual parity. (3) Scaling is a configuration decision (area/progressive = the boss's stats), not a boss-slot special case; with Vanilla scaling a Hollow
is a very short fight. (4) The body never respawns even if its own row would. (5) The legacy unique-enemy limit does not exist; the model budget of the map does. (6) `bosses` mode is untouched: `both` with the chance at 100 is exactly
`bosses` (checked by the sweeps).

#### Measurements and proofs

All on the real extracted catalog of a clean Dark Souls Remastered copy; zero unexplained failures; nothing was retried and no limit was relaxed.

| What | Result |
|---|---|
| Engine, 18 configurations x 100 seeds (`bodySweep.mjs`: basic, fits, anywhere, curve strict, chance 50, Vanilla and Progressive scaling, + roaming bosses (both directions), `both` at 40 / 90 / unique / + roaming, Type replacement enabled, Custom Pool with and without body models, Prevent same, Gwyn strong + Second Gargoyle) | 1800/1800 worlds: deterministic; bodies only in the three slots and only with the allowed models; entity ID, scaled row and mark present; scaling as configured; no body outside the Custom Pool; the final model count of every map never above `max(30, vanilla)`; `both` at 100 equals `bosses`; `both` at 0 has the bodies of `normals` |
| Model limit | 30 in every map; with Type replacement enabled the maximum was 28, with a Custom Pool 28 / 27 |
| Share of bodies | `normals`: 300 bodies per 100 worlds (3 slots x 100); `both` at 40%: 184 (94 worlds with any); at 90% (the default): 36 (35 worlds with any); chance 50 replacement: 158 |
| Event proof (real data tool, EMEVD diff of E2 against Part D packages of the same slots) | the Asylum Demon intro and every event that names the entity are identical to Part D's edit; E2 adds no event, no event ID and no instruction beyond Part D's (only the boss-name rewrite is absent) |
| Static verifier (`tools/smoke-verify`, 23 checks per encounter; read back from the real package of one world with a body in **all three slots**: Asylum Demon, Taurus Demon and Capra Demon pass) | entity ID equals the boss's, model replaced, body row differs from the boss row, `dropType` / `getSoul` / `disableInitializeDead` / item lots equal the boss row, `disableRespawn` 1, events naming the entity kept, no condition / flag-write / initializer instruction removed (the only removals are the model-specific forced animations `2003[18]` aimed at the encounter's own entity: the Asylum Demon drop in `11810310`, the Taurus Demon entrance in `11015382`; the same sanitization as Part D), event IDs unique, model count within the limit |
| Real golden fixtures | all 18 earlier fixtures of `ds1tr-gen-v1` verify byte for byte with the E2 engine and data tool; 4 new fixtures pin E2 worlds (basic, with Fog, with roaming bosses and `both`, Custom Pool edge) |
| Fog (real format, `fogHostE2e.mjs`, `verify-skips` on every package) | 10 configurations x 10 seeds = 100/100 generated (`normals`, `both`, curve / fits / anywhere, chance 60-100, Unique bosses, Type replacement enabled and except roaming bosses, roaming bosses together, Custom Pool): 0 retries, 0 conflicts, 0 skip mismatches over 11,526 edited events and 33,914 surviving skips; the union of the Fog boss overlays is **inside the 15-event whitelist** (`11005392`, `11010000`, `11210001`, `11405392`, `11415342`, `11415373`, `11415382`, `11515392`, `11600001`, `11700000`, `11810000`, `11810310`, `11815382`, `11815390`, `11815394`): **no new overlay, no new merge ID**; the maximum model count is 33 (the Undead Parish baseline) |
| Real data tool matrix (`placementMatrix.mjs encounter-e2`, 10 cases x 40 seeds; E1 used 100 and 60) | 400/400 generated, 0 retries, 0 event-window overruns, 0 non-deterministic reruns, maximum model count 30, at most 86 custom events in one map against the 100-ID window |
| E1 regression (`roamingSweep.mjs`, 18 configurations x 100 seeds) | 1800/1800; Part D and E1 golden fixtures byte-identical |
| Tests | see `IMPLEMENTATION_STATUS.md` |

**Not verified by playing:** the wake-up, killability, the health bar, the unlocking of the way on, rewards and the absence of crashes for a body in each of the three arenas. The seven quick and the seventeen extended worlds
(`docs/SMOKE_TESTS.md`; quick test "First boss fight - a normal enemy") are made for that.

---

### 10.9 Part F1 as built (2026-10-07): hostile NPCs (safe subset; friendly NPC replacement not built)

**Scope and verdict.** Part F as planned has two legacy options: **Hostile NPCs** (E20) and **NPC replacement** (E3). DS1TR builds **F1 only: Hostile NPCs**, as a safe subset
(three of the legacy's four NPC models). **NPC replacement is not built, by decision** (below): it would replace the Merchant, Andre, Vamos, Gough and the Fair Lady - a shop,
three blacksmiths and a Fire Keeper - by enemies, which removes services and the Chaos ember; the task's own hard stop ("friendly NPC support cannot preserve services") applies.
There are no "hostile NPC destination slots" and no "NPC to NPC" cases in the legacy data, so priorities 2 and 3 of the plan have nothing to build. The result is
**PART F SAFE SUBSET - STATICALLY VERIFIED / GAMEPLAY UNVERIFIED**: nobody has played a hostile NPC yet.

#### Legacy semantics (pinned Enemy Randomizer `83f008d`; reconstructed, not copied)

Evidence: `randomizer.py` (options, config string positions 2 and 19), `randomizer_rng.py` (the draw, `newCharacterAllegiances`), `valid_new.txt`, `valid_replacements.txt`,
`original_enemies_ref/*.txt`, `event_tools.py`. "No code found" means exactly that.

| # | Question | Legacy behavior |
|---|---|---|
| 1 | NPC-related options | "NPC replacement" (`npcMode` 0 off / 1 only bosses / 2 only normal enemies / 3 either; default 0; config position 2) and "Hostile NPCs" (toggle, default off; position 19). The boss chance of normal slots also applies to NPC slots. |
| 2 | Categories distinguished | Only the table column `Type`: 0 normal enemy, 1 boss, **2 NPC**. There is no friendly / hostile / scripted / invader / black phantom / merchant / covenant / summon category, and `c0000` (the player model: quest NPCs, phantoms, invaders, summons), `c1000` (bonfires) and the Fire Keepers `c5330` are in no table: **they are never touched**. |
| 3 | Which NPCs can be replaced | The five type-2 models of `valid_replacements.txt`: Undead Merchant `c2510` (two slots: Undead Burg, Lower Undead Burg), Andre of Astora `c2640` (Parish), Vamos `c2920` (Catacombs), Hawkeye Gough `c4110` (Oolacile), the Fair Lady `c5340` (Quelaag's sister, Blighttown). Six slots. |
| 4 | Which slots receive NPCs | None of those six. NPC models enter the **normal** candidate pool when Hostile NPCs is on (`parts[Type] == "0"` or `"2" and spawnNPCS`), so any normal draw can return one (also for a boss slot replaced "with normal enemies"). The Fair Lady is not in `valid_new.txt`: never a source. |
| 5 | NPCs replacing normal enemies | Yes, with Hostile NPCs on: Merchant (size 1, difficulty 0), Andre (1, 6), Vamos (1, 5), Gough (4, 5), equal weight with every other candidate. |
| 6 | Normal enemies replacing NPCs | Yes, with NPC replacement 2 or 3: the six slots, always with difficulty mode 2 (fully random), size limit 5 unless Placement is "anywhere except NPCs". |
| 7 | NPC to NPC | Possible through the shared pool (an NPC slot drawing an NPC source); no special rule. |
| 8 | Hostile NPCs as normal candidates | See 4-5. The only extra: an event per placed NPC that sets its team to 6 (hostile) through the entity (`SetCharacterAllegiance`, `2004[02]`); a slot without an entity id is given one. |
| 9 | Quest-critical characters | No handling at all. NPC replacement removes the services; the option is off by default. Andre alone has a special case: with a boss mode he becomes Gwyndolin (index 117) with 40%. |
| 10-12 | Merchants, blacksmiths, Fire Keepers | Merchant, Andre, Vamos, Gough, and the Fair Lady (a Fire Keeper) are **replaced** (their slot gets an enemy; the shop or service is gone). Other Fire Keepers (`c5330`) and bonfires (`c1000`) are not in the tables. |
| 13 | Covenant leaders | Quest NPCs are `c0000`: not in the tables, untouched. |
| 14 | Drops | No NPC drop handling found. The NPC's own NpcParam row (and so its own item lot) comes with the model, so a hostile Andre carries Andre's loot. |
| 15 | Death flags | No handling found; the events of the entity are those of the slot. |
| 16 | Respawn | No handling found; the NPC rows have `disableRespawn` 1. |
| 17 | Team / faction | Only the allegiance event of 8 (team 6 for the four hostile models). The NPC AI of an NPC *slot* is not changed ("lets not mod npc ai for now"). |
| 18 | Talk scripts | Not touched. |
| 19-20 | Invaders, summons | Not in the data: never touched. |

**Reading.** Hostile NPCs, in the legacy, is "four more models in the normal pool, hostile through one event each". NPC replacement is "six friendly NPCs become enemies and their services vanish".

#### NPC classification (the real catalog)

`classifyNpcSlot` (`generator.js`) gives every non-regular slot a semantic role; the table is what the real catalog produces. Unknown is `UNKNOWN_UNSAFE`.

| Role | Slots | What | Use |
|---|---|---|---|
| SAFE_HOSTILE_SOURCE | 3 | Undead Merchant (Undead Burg `c2510_0000`), Andre `c2640_0000`, Vamos `c2920_0000` | source of a body (F1) |
| MERCHANT_UNSAFE | 1 | the Lower Burg Merchant `m10_02 c2510_0000` | not a source: its think row (251001) names the logic script `251000_logic.lua` that the data tool does not copy to another map (it copies battle goals); a placed copy would name a missing script |
| BLACKSMITH_UNSAFE | 1 | Hawkeye Gough `c4110` | not a source: the part has think id 0 (his AI row is never run as a part's brain in the vanilla game), `eye_dist` 0 (event driven), an 8 m x 2 m capsule, 20,000 souls; unproven. A deviation from the legacy. |
| FIREKEEPER_OR_BONFIRE | 62 | 57 bonfires (`c1000`), 4 Fire Keepers (`c5330`), the Fair Lady (`c5340`) | never touched |
| QUEST_NPC | 8 | talking NPCs of non-player models (`c3210`, `c3450`, `c4090`, `c4140`, `c2860`, `c5310`, `c5361` x2) | never touched |
| PLAYER_MODEL_NPC | 80 | `c0000`: quest NPCs, phantoms, invaders, summons (teams 2 and 7) | never touched |
| BOSS_COMPANION | 3 | talking boss-family bodies (Priscilla, Nito, Quelaag) | the boss audit (10.8) |
| DUMMY_OR_PASSIVE | 35 | dummy parts (not spawned) | never touched |
| UNKNOWN_UNSAFE | 8 | `c2750`, `c4520` x3, `c4531`, `c3451`, `c5320`, `c5291` (friendly or neutral, no talk) | never touched |

#### What DS1TR builds

| Option | Contract | Neutral | Rule |
|---|---|---|---|
| Hostile NPCs | `enemies.placement.hostileNpcs` (extension key) | `false` | `sampling-v1`, normal enemies replaced (`normalReplacement` `normals` or `both`), no custom pool; refused otherwise |

UI: a toggle "Hostile NPCs" in the legacy options, shown only when it can apply; text: "Randomizes hostile NPC combatants: the Undead Merchant, Andre of Astora and Vamos can appear as enemies in place of regular enemies.
Quest and service NPCs are kept safe". The old disabled placeholders "NPC replacement" and "Hostile NPCs" are removed from the planned group (the first is not built, the second is real).

**Planner.** The three source slots become three *types* (`deriveHostileNpcTypes`: size from the capsule, difficulty with the formula of the regular classes, one variant per source slot) that are appended to the candidate table
**after** the regular class table is derived, so every regular class, the table hash and the HP ranks are what they are without them. They are drawn from the existing streams (a slot's or a Type group's own stream) like any model: legacy semantics is
pool membership, and a second pass with its own chance would invent a constant the legacy does not have. Consequently the option on gives a different world (different draws) and the option off gives exactly today's world (no type, no draw, no stream).
Per-map limit **12** hostile NPCs (DS1TR safety limit, not legacy): once a map holds twelve they are no longer candidates there; a Type replacement group counts all its slots. The Darkroot Basin (`m12_00_00_01`) shares the Garden's event file; its wake-ups are written there (event owners, `docs/EVENT_ALLOCATION.md`). A dummy part (not spawned) can be assigned a body but gets no wake-up event.

**Placement and entity.** A hostile NPC is a *body in a regular enemy slot*: `hostileNpc: true`, `makeTangible` (the NPC row's wandering-ghost transition is switched off like for every tangible replacement), a scaled NpcParam row (forced, like every sampled replacement), the standard wake-up event
(`forceCombatActivation`: clear standby, team 6, enable AI, the legacy allegiance event and the one every portable boss gets), and an entity id that is the slot's own or one assigned from the 16099000 range. Passive slots keep their passive rule. No talk script, no shop, no quest binding: the part's talk id is the slot's (none).

**Cell ownership (what the data tool writes, and asserts, for a hostile NPC row).** The row starts as a copy of the NPC's row and then takes from the slot's row every cell that says what kind of actor it is:

| Cells | Owner | Why |
|---|---|---|
| `teamType` | slot (in `AddScaledNpcRows`, as for every replacement) | the NPC row says 2 (friendly); a regular slot says 0 |
| `npcType`, `vowType` | slot | NPC type 2 and a covenant mark (Andre 1) are not the slot's kind of actor |
| `disableRespawn` | slot | NPC rows are 1 (they never come back); a regular enemy of the slot comes back |
| `dropType`, `humanityLotId`, `itemLotId_*` | slot | the reward and the humanity drop belong to the slot (Part A drop ownership); no blacksmith loot, no unique item |
| `getSoul` | slot, **in every scaling mode** | Gough has 20,000 souls, Andre 1,000: a vanilla-scaled NPC must not pay NPC souls from a Hollow's place |
| `hp`, defenses, resistances, `stamina`, every `spEffectID*` | slot with Area / Progressive scaling; the NPC's own with Vanilla scaling | the existing scaling setting |
| `isGhost`, `isChangeWanderGhost`, `ghostModelId` | cleared | `makeTangible` |
| model, animations, `behaviorVariationId`, `aiThinkId`, size, weight, material, ragdoll, knockback, `modelDispMask*`, `normalChangeTexChrId`, `animIdOffset`, `isWeakSaint`, guard levels | the NPC | the body and its way of fighting |

**AI.** The NPC's own think row (cloned with the slot's perception, like every replacement) and its battle script (`GOAL_KajiyaAndre264000_Battle`, `HusibitoKojiki251000Battle`, the Vamos script) are copied into the destination map's AI bundle by the data tool; they are real combat scripts (`REGISTER_GOAL`, `TARGET_ENE_0`, attack and step functions). The static verifier checks that the destination bundle holds the battle script and the logic script of the variant that was placed.

**Seed Codes.** `hostileNpcs` is an extension key (neutral `false`, appended after `bossChanceInBossSlots`): every earlier code keeps its text, decodes to off and generates the same world (all 22 earlier golden fixtures verify byte for byte). New worlds are pinned by `ds1tr-gen-v1.hostile-npcs.json`. Presets: version 13.

**Data tool** (patch 0017). `PatchPlacement.HostileNpc`; `AddScaledNpcRows` copies and asserts the slot-owned cells of the table above. No talk file, no new overlay, no new event kind: the wake-up is the existing one, in the existing custom event window. **No talk script (ESD) file is read, written or present in any generated package** (checked on the package file list).

**Deliberate deviations from the legacy.** (1) Gough and the second Merchant are not sources (classification). (2) The slot owns team, respawn, reward and drops (the legacy lets the NPC carry its row). (3) At most twelve per map. (4) NPC replacement (E3) is not built. (5) With a custom pool the option is refused: the pool lists enemy models and the hostile NPCs are not part of it; widening its meaning silently would be wrong.

**What it takes to build friendly NPC replacement (not done).** A friendly slot's body can be replaced only if its entity keeps its talk id, its ESD binding (`talkesdbnd`, also edited by FogMod's world data), its shop and upgrade menus and its quest flags; the legacy does none of it. That is broad talk-script and quest-event work with an unprovable result without playing: out of scope by the hard stop.

#### Measurements and proofs

All on the real extracted catalog and a clean copy of Dark Souls Remastered; zero unexplained failures; nothing was retried and no limit was relaxed. Fixed during the work: the per-map limit (an uncapped run put up to 117 hostile NPCs in one map under strict curve and 73 under Type replacement, which would have been that many wake-up events), the missing logic script of the Lower Burg Merchant (found by the verifier), the Darkroot Basin without events (found by the verifier), and wake-up events for dummy parts.

| What | Result |
|---|---|
| Engine, 15 configurations x 100 seeds (`npcSweep.mjs`: basic, fits, anywhere + Easy Asylum, curve strict and loose, Type replacement enabled / except roaming, + roaming bosses, + boss bodies, + boss sampling both, Prevent same, Vanilla / Progressive scaling, Gwyn nerf + Second Gargoyle) | 1500/1500 worlds: deterministic; sources only the three authored slots; destinations only regular (team 0) slots; scaled row, tangible mark and wake-up present (none for a dummy); at most 12 per map (the Basin included, since the event owner fix); the model count of a map never above `max(30, vanilla)` (maximum 30); option off equals option absent |
| Share | about 55 hostile NPCs per world with every slot replaced (twelve per map at most); 17-21 per world with Type replacement |
| Static verifier (`tools/smoke-verify`, 29 checks for the quick world; 41 expectations of a 100%-replacement world pass 41/41) | model, entity, no talk, scaled row, `teamType` / `npcType` / `disableRespawn` / `dropType` / `getSoul` / `humanityLotId` / `vowType` / item lots equal the slot's row, no ghost transition, think row, battle and logic script in the map's AI bundle, wake-up event naming the entity |
| Golden | all 22 earlier fixtures verify byte for byte with the F1 engine, data tool and presets; 5 new fixtures pin hostile NPC worlds (basic, Fog, roaming bosses, Type replacement, boss bodies) |
| Fog (real format, `fogHostE2e.mjs`, `verify-skips` on every package) | 10 configurations x 10 seeds = **100/100** generated (normal enemies `normals` and `both`, curve strict / Easy Asylum / chance 60 / anywhere / fits, Type replacement enabled and except roaming bosses, roaming bosses with stay dead, boss bodies, boss sampling `both` + Unique bosses, Prevent same): 0 retries, 0 conflicts, 0 skip mismatches over 14,288 edited events and 39,119 surviving skips; the union of the Fog boss overlays is **inside the 15-event whitelist** and the merge IDs are the same 12 as without hostile NPCs: **no new overlay, no new merge**; maximum model count 30. No talk file in any package |
| Real data tool matrix (`placementMatrix.mjs hostile-npc-f1`) | 10 cases x 40 seeds (E1 used 100 and 60): **399/400** on the first run and 400/400 with the one exception rerun: seed 36 of `npc-vanilla-scaling` failed once with a Windows file-lock error while the data tool wrote a map file (`The process cannot access the file ... m13_02_00_00.msb because it is being used by another process`, an environment error, not a generation error) and passed when run alone (`--only 36`). 0 retries, 0 event-window overruns, 0 non-deterministic reruns; at most 108 custom events in one map (Demon Ruins, Type replacement except roaming bosses + roaming bosses; the window moved off the Bed of Chaos floor event in 3 seeds, which is what `docs/EVENT_ALLOCATION.md` provides); maximum model count 30 (31 in a map whose fixed models already exceed 30, the non-worsening rule) |
| E1 / E2 / Part D regression | with hostile NPCs off: `roamingSweep.mjs` (E1) 18 configurations x 100 seeds = 1800/1800 with exactly the numbers recorded before Part F; `bodySweep.mjs` (E2) 18 x 100 = 1800/1800; the Part D, E1 and E2 golden fixtures byte-identical. With hostile NPCs on together with E1, E2 and boss sampling: the engine sweeps above (`npc-plus-roaming-both`, `npc-plus-e2-normals`, `npc-plus-boss-sampling-both`, 300 worlds), Fog `h4`, `h6`-`h9`, the matrix cases `npc-plus-*`, and 3 golden fixtures |

**Not verified by playing:** whether a hostile NPC wakes up, attacks, navigates, dies and gives only the slot's rewards; whether the NPC models show no T-pose in a regular slot; whether Gough would have worked (not tested). The quick world "Hostile NPC - Andre as an enemy" and five extended worlds (`docs/SMOKE_TESTS.md`) are made for that.

### 10.10 Part G as built (2026-10-07): legacy-style sampling is the default of new configurations

**Decision.** `sampling-v1` with the legacy defaults is what a **new** configuration starts with (a first start, "Restore defaults", the base of a new preset). It is a product decision made after A-F were complete: the legacy defaults are fit = only where they fit, difficulty = random + Easy Asylum, normal enemy replacement = normals, boss replacement = classic shuffle, chance 100, boss chance 10 / 90, no hostile NPCs, no Type replacement, no custom pool (`DEFAULT_GENERATION_SETTINGS` in `@ds1tr/contracts`).

**What the default does not touch.** Nothing that already says what it is.

| Where an algorithm is stored | What it does now |
|---|---|
| Seed Code | the algorithm is part of the canonical settings of the code: a sampling code is a sampling code, a classic code stays a classic code (the extension keys exist only when non-neutral; neutral = the classic permutation, so an old code decodes exactly as before) |
| Preset (version 13) | an export always states the algorithm; a preset from before the placement section (version 4 and older) has none and imports as the **classic permutation**, never as the new default |
| Persisted settings and the saved settings file | an existing choice stays (`permutation-v1` stays classic); only "nothing saved yet" starts from the default |
| Run history | a record keeps the Seed Code it was made with; "Load seed" restores the settings of the code |
| The value a record without the key means | `NEUTRAL_ENEMY_PLACEMENT_OPTIONS` (classic permutation): unchanged, and tested (`placementDefault.test.mjs`) |
| The test worlds (`docs/SMOKE_TESTS.md`) | built from their own frozen base `SMOKE_TEST_BASE_SETTINGS` (classic unless the world names sampling); the product default does not move them, so every recorded Seed Code of a test world is unchanged |

**UI.** The algorithm select reads **Sampling (recommended)** and **Classic permutation** (the serialized values `sampling-v1` / `permutation-v1` are unchanged). With the classic permutation the sampling-only controls are **not shown** (no dead controls: a control that cannot do anything on the chosen algorithm is not offered); with sampling each control is shown and is greyed (never hidden, with the reason in its tooltip) only when another switch turns the feature off (no regular enemies, no bosses, mode not selected). The "Legacy parity - planned" placeholder is gone; "Allow T-pose behavior" is not built and is not advertised. Wording is the plain-language form of the legacy options ("Normal enemy replacement", "Placement", "Difficulty mode").

**Deterministic ordering (documented and locked by `enemyPlacementOrdering.test.mjs`).** One place says in which order the planners decide, so that a feature cannot silently depend on a later one:

| Step | Decides | Reads | Draws from |
|---|---|---|---|
| 1. Items | item placements | the item options only | its own streams; **no enemy option changes an item placement** (test: every enemy toggle leaves the item placements byte for byte alone) |
| 2. Boss plan (Part D, E2 bodies) | boss slot assignments, boss bodies | boss options, Gwyn nerf, Second Gargoyle | its own streams (boss class draws, `category:slot:<id>`, `body:slot:<id>`); **decided before the regular sampler**: no regular, roaming or hostile option changes a boss placement |
| 3. Class tables | the derived classes, the table hash | the catalog | none |
| 4. Hostile NPC types | appended to the candidate table **after** the classes are derived | the option | none (appended, so every class, the hash and the HP ranks are what they are without them) |
| 5. Regular sampler (Part A-C) | regular slots and Type groups | the model budget (bosses only through the budget they leave) | `slot:<id>` and the Type group streams (`map:<map>:model:<model>`) |
| 6. Roaming pass (E1) | overrides slots with bosses | the plan of step 5 | `roaming:type:*` / `roaming:slot:*`; **it only overrides**: every slot it did not take keeps the placement it had without the pass (test, hostile NPCs on and off) |
| 7. Own rows, wake-ups, events | scaled NpcParam rows, wake-up events, event ids | the final placements | none |

**No RNG cascade.** A **neutral option draws nothing**: its off value, its absence and its default are the same world for every extension key (test), because every draw comes from a named stream of the slot or group it belongs to, never from a shared sequence. Boss bodies (E2) take no draw from the regular sampler or the roaming pass. The replacement chance is the one shared first draw of a slot, boss slots included (so chance 100 and "not asked" agree).

**Placement validation summary.** Every run now reports `result.validation.enemyPlacement` (engine) and `manifest.enemyPlacement.validation` (run manifest; plain counters, no per-slot detail, **not part of the placement hash**): algorithm, maps, placements (regular and boss slots placed, changed, kept vanilla), the final models of the fullest map and maps above the limit, events (wake-ups, passive-until-attacked, disabled entities, deferred or contained), own rows (NpcParam, NpcThinkParam), roaming bosses, hostile NPCs, boss bodies, map retries, difficulty lapses, class fallbacks and the safety exclusions (boss slots kept because unsupported, slots kept by chance, slots kept by the pool). The run manifest already carried the planner counters (`enemyPlacement.summary`); the validation block is the placement-level view next to it.

**Event owners.** Part G contains the Darkroot Basin fix (Part A of the same work): `m12_00_00_01` has no event file of its own and writes its events in the Garden's (`docs/EVENT_ALLOCATION.md`, "Event owners"). The hostile NPC restriction for the Basin is gone. Recorded exception 2 in `docs/GENERATION_COMPATIBILITY.md`.

**Classic stays frozen, with its quirks.** The permutation core and its hash lock are untouched, and every classic golden fixture verifies byte for byte. Quirks that are kept on purpose because fixing them would change old Seed Codes: (1) Basin placements of the classic permutation have no events (the event owner is resolved for sampling only); (2) with Vanilla scaling a changed placement keeps the replacement's drops (13.11). A new profile is the only way to change either.

**Sweeps** (all on the real extracted catalog and a clean copy of the game; zero failures; no retry budget was raised, nothing was relaxed):

| What | Result |
|---|---|
| Engine, the exact product default (`defaultSweep.mjs`, `prepareRun` + the application's config mapping) | **500/500** worlds: deterministic, 0 maps over the model limit (max 30), every changed placement slot-owned; mean per world: 1,753.8 changed, 138.1 kept, 159 wake-ups (max 201), 20 passive, 32 disabled entities, 0 retries, 0 difficulty lapses, 0 class fallbacks. The same sweep with `--classic` (the frozen permutation): 500/500 |
| Real data tool, default configuration (`placementMatrix.mjs product-default`) | Items + Enemies + Bosses **100/100**; Enemies + Bosses **100/100**; the full Triple with the real FogHost (Items + Enemies + Bosses + Fog) **100/100**: 0 retries, 0 window overruns, 0 event owner failures, 0 non-deterministic reruns, max 30 models |
| Real data tool, event owners (`placementMatrix.mjs event-owners`, Part A) | **810/810**: E1 (roaming, both + stay dead), hostile NPCs, Type replacement (enabled, except roaming), general sampling, Part D boss sampling, E2 bodies, Second Gargoyle: 0 event owner failures over about 230,000 checked events; the classic cases keep the quirk (1,145 frozen events, no failure) |
| Feature matrix (`featureMatrix.mjs`): Type replacement x custom pool x normal replacement x boss replacement x hostile NPCs x fit x difficulty = 384 combinations x 30 seeds | 7,680 worlds generated, 3,840 refused by the typed options errors (hostile NPCs with a custom pool or without normal enemies; every one refused as designed), 0 failures: at most 12 hostile NPCs and 10 roaming bosses per map, boss bodies only in the audited slots, no map above max(30, its models without the roaming pass) (+1 for the custom pool grace), the validation summary consistent with the placements, deterministic; 956 worlds had roaming bosses and hostile NPCs together, 606 boss bodies and hostile NPCs together |
| Fog real format (`fogHostE2e.mjs`, `verify-skips` on every package) | see the status document (4 configurations x 25 seeds with the event-owner data tool: default, E1 both + stay dead, hostile NPCs + Type, E2 boss bodies) |
| Golden fixtures | all 27 verify from their Seed Codes (classic and unaffected cases byte-identical; the 12 + 5 sampling / hostile fixtures re-recorded as recorded exception 2) |

**Gameplay status (honest).** Runtime-verified by playing: the main E1 quick tests, boss sampling (first boss), sampling first enemies, E2 Asylum Demon and E2 Taurus. **Not verified by playing:** Part F (hostile NPCs), E2 Capra, the extended Fog / Gargoyle / Parish worlds and the Darkroot Basin test worlds. Everything above for them is static (verifier, sweeps, fixtures).

**Enemy Placement A-G status: Enemy Placement core complete with documented safe-subset deviations.** The deviations are the safe subsets chosen by decision (E2: three boss encounters; F: hostile NPCs only, no friendly NPC replacement; E1 roaming bosses limited to supported single-body bosses) and the frozen classic quirks above. Nothing is "complete" in the sense of full legacy parity: `docs/FEATURE_PARITY.md` lists what remains.

---

## 11. Prototype evidence

### 11.1 Sampler prototype

`engines/combined/prototypes/enemy-placement-sampler.mjs` implements batch A's core plus the curve modes and
Type replacement for **regular enemies only** on the real catalog (1,703 slots, 86 types). 40 seeds per
variant, every run executed twice for determinism:

| Variant | Left out by chance / seed | Fit violations (all placements) | Prevent-same violations | Budget errors | Non-deterministic | Top model share | Types / map | ms / seed |
|---|---|---|---|---|---|---|---|---|
| 100%, fit, random, **no reserve** | 0 | **87 (0.13%)**, each with keep-vanilla blocked by the budget | 0 | 0 | 0 | 6.6% | 21.7 | 7 |
| 100%, fit, random | 0 | **0** | 0 | 0 | 0 | 6.1% | 21.7 | 7 |
| Curve, loose | 0 | 0 | 0 | 0 | 0 | 5.8% | 20.9 | 6 |
| Curve, strict | 0 | 0 | 0 | 0 | 0 | 5.6% | 20.7 | 6 |
| Fit anywhere | 0 | 0 | 0 | 0 | 0 | 3.1% | 22.1 | 8 |
| 60% chance | 679 | 0 | 0 | 0 | 0 | 5.8% | 18.4 | 7 |
| Prevent same + type replacement | 0 | 0 | **0** | 0 | 0 | 8.0% | 9.0 | 7 |

Curve adherence (share of placements on the slot's own class): while the map's model set has room 73% (loose)
and 83% (strict), matching the tables. Once the set is full, the literal legacy rule gives **30% / 31%** and
covers 56-63% of placements; keeping the class filter inside the set (D-1) gives **72% / 77%**.

With the coverage reserve no fallback rung was used at all (kept-vanilla, in-set-fit and fit-relaxed counts are all
0 over 68,120 placements per variant).

What the prototype does **not** show: anything about bosses, NPCs, units, EMEVD, scaling or Fog, and nothing
about how the game behaves with many repeated sources in one map; those need the batch-level sweeps. The
class-evaluation figures in section 6.2 come from comparing the derived classes with the legacy table as a
yardstick; the table is not stored in the repository.

### 11.2 Drop-ownership measurements

From the vanilla `GameParam` (NpcParam and ItemLotParam rows read with SoulsFormats) crossed with the catalog
and FogMod's `fog.txt`: figures in 9.1. The duplication measurement runs the shipped engine 100 times per
configuration with scaling `vanilla` and counts lots owned by a single vanilla slot that appear on two or more
placements without a scaled row: permutation baseline 95/100 seeds (1.15 lots per seed), product defaults
95/100 (1.25), product defaults with `area` scaling 0/100.

To reproduce (needs a game-derived catalog from `DsrDataTool scan`, never committed):
`node engines/combined/prototypes/enemy-placement-sampler.mjs <catalog.json> 40`.

---

## 12. Data that must be authored (not copied)

- **Size and difficulty edges and weights** (derived, section 6.2) plus the map progression table.
- **Pair exclusions** (safety): start from the engine's existing rules (`shuffledPortableBosses`, the
  Bell/Taurus staging, the legacy tool's reasons as hints) and test each in the real patch.
- **Easy Asylum:** a rule such as "Asylum slots only accept types with difficulty <= k and size <= slot size",
  not the legacy hard-coded index lists.
- **NPC allowlist** (which NPC slots may be replaced) and the **hostile NPC source list**: decided from
  mechanics (quest flags, talk ESDs, shops) and verified against FogMod's NPC-adjacent events.
- **Strictness tables** (section 6.4).

---

## 13. Unresolved questions

1. **Product default.** Decided (Part G, 10.10): new configurations start with `sampling-v1` and the legacy defaults; old Seed Codes, presets and settings keep their explicit algorithm.
2. **Model budget definition.** DS1TR counts all models in the map (cap 30, measured safe). The legacy tool
   counts only new types (30, Blighttown 50 on Remastered) and keeps unused originals in the list. Can a map
   exceed 30 total? Blighttown may justify a higher cap; this cannot be answered without running the game.
3. **Difficulty classes.** Is a derived class acceptable (rank correlation 0.58), or does DS1TR want a small
   curated override table (Mimic, flying and special enemies)? Same for the empty size class 4 edges.
4. **Boss in a normal slot and Enemy scaling.** Legacy bosses keep their stats; DS1TR's area scaling would
   give them the slot's stats. Follow the user's scaling setting, or offer "keep boss stats" (per-placement
   `scaling: vanilla`)?
5. **NPC allowlist** and what "replace an NPC" must preserve (talk ESD, shop, quest flags). Legacy keeps the
   NPC's AI and talk; whether that works with DS1TR's placement fields is untested.
6. **Respawning roaming bosses:** an NpcParam bit on the scaled row, or an event as in the legacy tool?
   Needs an in-game check.
7. **Wake-up of boss AI in normal slots** (portable brains in neutral states): same risk as the Second
   Gargoyle release; the combined engine's wake-up rows exist for the bosses that need them.
8. **Type replacement edge cases** the pinned source leaves undefined (sentinel indexing in "except roaming
   bosses"); the design defines them (section 5, question 1) but the intended behavior is not stated in the
   source.
9. **Variant choice:** uniform over a type's (AI, NpcParam) variants as legacy, or weighted by how often each
   variant occurs in the game.
10. **Linked bosses as lone sources** (for example a Gaping Dragon in a normal slot) need synthetic parts;
    out of scope here, listed so the pool restriction is a conscious one.
11. **Vanilla-scaling duplicate drops in `permutation-v1`** (9.1): leave as is (default `area` scaling is
    unaffected) or give every changed placement a scaled row in a future fix? Doing so changes the packages of
    existing `vanilla`-scaling presets (more NpcParam rows), so it is a compatibility decision, not a silent
    fix.
