# Solver witness (the "Solver route")

Status: implemented in desktop 0.42.0 / backend 0.48.0 / contracts 0.35.0 / FogHost project 0.9.0 (HostVersion stays `0.7.3`). Diagnostic only: it changes no generation semantics, no Seed Code and no generated file.

## Why

The solver has repeatedly accepted worlds that the real game does not let a person finish (the Asylum -> Ceaseless start trap, the reverse side of Quelaag's boss fog, the Gaping Dragon arena entered through a mislabelled wall). A summary such as "131 of 131 Areas reachable" cannot be compared with the game. The witness shows **the exact route and the exact edges the solver believed**, so a person can follow it. The first step that cannot be done in the game identifies the graph/model defect.

## Source of truth

The proof is recorded inside the search that accepts the world, not rebuilt afterwards:

- `StartReachabilityValidator.Validate` (generation profile `ds1tr-gen-v6`) runs `GraphChecker.Check(options, graph, start, null, null, observer)`.
- `WitnessObserver` (project-owned, `tools/fog-host/src/WitnessObserver.cs`) is called by `Check` at five points: the start Area, the beginning of each pass, a boss-completion grant, and, immediately before `visit(edge.To)`, the **first** edge that reaches a new Area (with the edge's combined condition, which of its variables were already true, and the key items the new Area makes available). It reads nothing back and changes nothing: with a null observer (every other caller, including the connector) the checker is byte-for-byte the previous one (`SolverWitnessTest.VerifyObserverDoesNotChangeTheSearch`).
- `NodeRecord.InEdge`, `Dist` and `Visited` are **not** used: a cheaper path rewrites them later, and they are not "why the solver first believed it".

Only profile `ds1tr-gen-v6` (`compact-v3`) has the accepting search with a witness. Profiles V1-V5 are frozen and write none.

## What is shown

`solver-witness.json` (machine-readable) and `solver-route.md` (human-readable), kept in the **run folder** next to `hints.json` (never in the package, which is hashed and applied to the game). FogHost writes the raw witness next to its log (`fog-<combined>-<fog>/solver-witness.json`); after a successful run the backend copies the **accepted attempt's** witness to the run folder, adds the run context (Seed Code, profile, master seed, Fog seed, attempt numbers) and renders the route.

- **Main route** (`mainRoute`): the dependency closure of one target. The target is the Gwyn arena `kiln_gwyn` when the solver reached it (the deepest progression Area; reaching it is the completion the solver proves), otherwise the Area with the largest solver distance. Starting at the target, the closure adds the predecessor Area and, recursively, everything the edges on the way needed: Areas, key items (taken from the Area that holds them), boss completions. Every prerequisite precedes the step that needs it (`VerifyMainRouteIsSelfConsistent`).
- **Full proof tree** (`reachTree`): every Area with the edge that first reached it, its search pass, its condition, which variables satisfied it, and the key items it provides. This is for debugging and support, not the main UI.
- **Both sides of every gate**: a fog gate is shown as the gate you leave (object, description, Area, other-side Area) and the gate you arrive at (a different object when the pairing is shuffled). `bossTriggerSide` marks the side that starts the boss encounter; `areaIsBossArena` marks an Area tagged `boss`. `pairing` is `vanilla` (an object whose exit side leads to its own other side) or `shuffled`.
- **Direction**: read from the graph, not from a missing pair. An in-map link is built without a pair, and a walkable way back is a **separate edge of the other Area**, so `oneWay` means "no edge from the arrival Area back to the exit Area exists in the graph". `reverse` is `none`, `unconditional` or `conditional`, with `reverseCondition` and `reverseVia` (the gate object, or "in map"). For a gate the reverse is its pair on the same object; otherwise the best edge back (unconditional first). The route prints `TWO-WAY (the graph also has B -> A via ..., always / only when X)` or `ONE-WAY (no edge back in the graph)`.
- **Order**: the route is the dependency closure walked greedily: among the steps whose prerequisites are already met, one that leaves the Area the player stands in comes first, then the lowest solver sequence. It stays deterministic and valid, and a "First be in X" line is then a return the solver really assumes.
- **Conditions**: the condition as the solver evaluated it (`requires`), each term with its kind (`area`, `key-item`, `boss-completion`), where it comes from and the step that provided it.
- **Key items**: where the solver believes each is obtained (the Area, the staged `ItemLotParam` rows in the solver's lot table, whether it was moved from its default Area) and its English name by game item id.
- **Glitched logic**: only edges whose side carries the `hard` tag, which `Graph.Construct` drops when glitched logic is off; they are marked `glitched-route` with a visible warning.

### Edge kinds, from metadata only

`randomized-fog` / `fixed-fog` (an Entrance; boss, PvP, major, world, lordvessel qualifiers come from its tags), `warp` (an entry of `ann.Warps`), `door` (tag `door` or a lordvessel gate), `fixed-route` (an in-map `Area.To` link; `shortcut` is a qualifier), `glitched-route`. The compact catalog has **no ladder, drop or elevator tag**, so none is invented: such links are `fixed-route (in-map)`.

### Names

Gate descriptions are the catalog's own `Text` (project transition ids such as `o4950_0000 "blighttown-quelaag-boundary"`, or the pristine-data warp descriptions). The profile `compact-v3` does not carry FogMod's upstream `fog.txt` descriptions (that file is user-supplied and not used by V6), so none can be shown. The technical id is always shown; an in-map link is `<Area> (in-map link)`.

## What the solver assumes (printed in every route)

1. Every Area the solver has already reached can be entered again at any time (it expands every visited Area; it does not check the way back). A step whose exit Area is not where the previous step ended says "First be in: ...".
2. A key item is obtained as soon as its Area is reached.
3. A boss completion is derived only from a directed arrival through the boss encounter entrance; the game sets the real flag only when the boss dies.
4. Two-way / one-way is what the graph has.

## Where it appears

- Desktop: **Solver route** in the run menu after a successful generation (generated state), in the applied state, and in the history row menu. A modal shows the context, the route, an optional full proof tree and **Copy route** (the copied text is `solver-route.md`).
- Support bundle: `generation/solver-route.md` and `generation/solver-witness.json` of the chosen world.
- A world made before this feature has no witness (`solverRouteAvailable` is false); *Regenerate files* creates it again (same Seed Code, same world).

## Determinism and identity

Same Seed Code + profile + world data -> the same witness bytes (fixed key order, no paths, no timestamps; the run context holds the app version only). `solverWitness.test.mjs` generates the same Seed Code twice and compares witness, route and package. Regenerating the two suspect worlds (below) gave packages that match the stored ones file by file (the second world with Skill Issue Mode on, as it was made: all 73 game files identical and only `run-manifest.json` differs; the older first world: only `GameParam` also differs, because it predates the final Skill Issue loadout), so the witness changes nothing about a world. The existing golden compatibility cases (`goldenCompatibility.mjs verify`) are the standing proof for the frozen profiles.

## Tests

- `tools/fog-host/tests/SolverWitnessTest.cs`: observer neutrality, predecessor/first-reach order, both gate sides, conditions, key acquisition, route self-consistency, glitched marking (absent when off), deterministic JSON; data-backed: the Asylum -> Ceaseless trap (regression A) shows the fake unconditional hallway exit as the route's first step, Quelaag (regression D) shows the arena entered by the fixed back link and left by `o4950_0000` from the boss-arena side, Gaping (regression E) shows `o1420_0000` entering `depths_gaping`, and an accepted real V6 layout has a self-consistent route and the same witness on every connection.
- `apps/backend/src/core/solverWitness.test.mjs`: rendering (sides, conditions, acquisition, glitched warning, boss-completion references), JSON round trip, accepted-attempt persistence, missing witness, support bundle, and the real double generation.
- `apps/desktop/src/ui/solverRoute.test.mjs`: the action after generation, in the applied state and in the history; the panel; the typed boundary; both languages.

## Not fixed here

The witness exposes the solver's reasoning; it does not correct it. Suspect routes are listed with their findings in `docs/SOLVER_WITNESS_SUSPECT_ROUTES.md` and must be tested in the game before any change.
