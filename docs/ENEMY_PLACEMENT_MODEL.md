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

Status: **design, the Part 0 infrastructure (10.1) and Part A (10.2).** `sampling-v1` runs for regular enemy slots
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
  fit and the hard pair rejections (legacy bypasses them).
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
These are starting values to tune against play, not legacy data.

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
| **B** | Difficulty curve | class draw, strictness tables, D-1 refinement, Easy Asylum rule, curated overrides | `difficultyMode` curve values, `strictness`, `easyAsylum` | select + slider activation | offset distributions, same-class share, empty-class fallback | 20 seeds | none |
| **C** | Type replacement and custom pool | type map with type-level participation and injectivity; pool filter | `typeReplacement`, `customPool` (named list of enabled types) | select, pool editor (largest UI piece) | injectivity, sentinel cases, pool filter | 10 seeds | none |
| **D** | Boss class (bosses only) | boss sampler over the portable single-body pool, per-candidate Gwyn nerf, Unique bosses, unit participation | `bossReplacement` {none, bosses}, `uniqueBosses`, `bossChanceInBossSlots` | activation | boss repetition, unit swaps, hash lock | **40 seeds** Bosses + Fog (overlay set must remain the 15 events), `verify-skips` | medium |
| **E** | Cross-class and roaming boss options | boss to normal slot, normal to boss slot, "both" modes, `getSoul` on the scaled row, respawn handling | modes `normals`/`both`, `bossChanceInNormalSlots`, `roamingBossSouls`, `respawningRoamingBosses` | activation | reward ownership, scaled row always present, boss bar rules | **40 seeds**, measure edited events against the overlay and FogMod; stop on any new overlap | **high** |
| **F** | NPC class and hostile NPCs | NPC allowlist, talk preservation, hostile sources, allegiance events | `npcReplacement`, `hostileNpcs` | activation | NPC fit rules, talk preservation fixtures | event-set diff vs FogMod first, then 40 seeds | **high** |
| **G** | Default flip (product decision) | none | default `sampling`, docs, migration notes | default values | n/a | full 40-seed default sweep | low once A-F passed |

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

Batch 0 must also decide where `placement.js` lives: new code has to be inside `vendor/src` (that is what
the packager copies and what the loader imports), so it is a repository-authored GPL file recorded in
`UPSTREAM_PIN.md`, not an edit of an upstream file.

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

1. **Product default.** Keep `permutation` as the default until the group is complete, then flip? (The
   legacy defaults are fit = only where they fit, difficulty = random + Easy Asylum, boss chance 10/90; a flip
   changes every new preset's enemy layout.)
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
