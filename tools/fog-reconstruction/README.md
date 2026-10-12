# Fog reconstruction development tool

This tool is development-only. It is not referenced by either packaged distribution and must not be used to copy Fog or game data into the product.

The reference Fog cache is an oracle only. Keep its files and all generated reports outside the repository. The checked-in tests use synthetic semantic models and contain no game or Fog binaries.

Commands:

```text
FogReconstruction self-test
FogReconstruction verify-reference <root-containing-dist>
FogReconstruction verify-inputs <pristine-dsr-root> <root-containing-dist>
FogReconstruction inspect-paramdefs <pristine-dsr-root>
FogReconstruction extract <pristine-dsr-root> <root-containing-dist> <report-json> [ds1-common.emedf.json]
FogReconstruction compare-start-area-shape <fog-reference-root>
FogReconstruction audit-area-catalog <fog-data-root> <pristine-dsr-root> <report-json-outside-repo>
FogReconstruction audit-area-graph-behavior <fog-data-root> <pristine-dsr-root> <report-json-outside-repo>
FogReconstruction inspect-reference-area-tag-ids <fog-data-root> <comparison-json-outside-repo>
FogReconstruction audit-boss-defeat-facts <fog-data-root> <pristine-dsr-root> <report-json-outside-repo>
FogReconstruction audit-transition-events <fog-data-root> <pristine-dsr-root> <report-json-outside-repo>
FogReconstruction audit-area-gates <fog-data-root> <pristine-dsr-root> <report-json-outside-repo>
FogReconstruction audit-world-transition-inventory <fog-data-root> <pristine-dsr-root> <report-json-outside-repo>
FogReconstruction inspect-map-transition-candidates <fog-data-root> <pristine-dsr-root> [map-key]
FogReconstruction inspect-custom-start-boss-proximity <fog-data-root> <pristine-dsr-root> [custom-start-id]
FogReconstruction inspect-game-item-id <fog-data-root> <pristine-dsr-root> <category-id> <item-id>
FogReconstruction inspect-event <emevd-path> <event-id> [emedf-json]
FogReconstruction inspect-event-arg-references <emevd-path> <int32-value>
FogReconstruction inspect-added-event-refs <pristine-dsr-root> <root-containing-dist> <analysis-output-outside-repo>
FogReconstruction inspect-warp-calls <common.emevd.dcx>
FogReconstruction inspect-warp-call-audit <output-path-outside-repo> <common.emevd.dcx> [...]
FogReconstruction inspect-event5700 <common.emevd.dcx> <analysis-output-outside-repo>
FogReconstruction verify-event5700-behavior <reference-common.emevd.dcx> <matrix-output-outside-repo> <generated-common.emevd.dcx> [...]
FogReconstruction inspect-event5700-history <analysis-output-outside-repo> <label=common.emevd.dcx> <label=common.emevd.dcx> [...]
FogReconstruction replace-event5700 <generated-common.emevd.dcx> <output-common.emevd.dcx-outside-repo>
```

`verify-inputs` authenticates the 19 map/GameParam files used by Fog against the pinned hashes and verifies the 59-file reference root. It also requires the DSR ParamDef binder. It does not replace the application's signed-off pristine snapshot authority.

`extract` writes the semantic diff, including game-derived instruction/evaluator values, only to the caller-provided report path. Use a location outside the repository, do not commit or ship the report, and delete it when analysis is complete. The extractor identifies candidate vanilla event sources but does not decide that an added event is safe to reproduce; added-event provenance still requires review of the pinned FogMod code.

`inspect-added-event-refs` lists added event IDs, body sizes, and direct `InitializeEvent` callers across all supplied archives, separating same-archive and cross-archive sites. Removed event rows show vanilla and remaining callers in both scopes. It does not write instruction operands and rejects output paths inside the repository.

`inspect-event5700-history` compares operation shape, operands, and parameter mappings for caller-labeled historical versions. It writes only counts, equality results and fingerprints; it never writes instruction arguments or event bodies.

`inspect-event-arg-references` searches one EMEVD archive for raw `int32` occurrences within instruction argument bytes. These are candidate references only; confirm the instruction schema with `inspect-event` before assigning semantics.

When a compatible DarkScript EMEDF JSON file is supplied, `inspect-event` decodes each instruction and prints named operations/arguments. The game's `common.emedf.dcx` is a different binary format and is rejected; do not pass it as the optional JSON argument.


`compare-start-area-shape` checks whether the current project-authored custom-start area slice agrees with the comparison reference on consumed area tags, scaling-base presence, and static-link destinations, conditions, and active tags. It emits no reference edge targets or conditions and does not copy reference values into project policy; unconsumed link tags are excluded.

`audit-area-catalog` emits a development-only JSON inventory for all 131 project Area nodes. It checks the ordered identity inventory against the comparison inventory, resolves custom-start map anchors from a byte-verified pristine DSR copy, applies the project-owned ID-namespace map rule, validates every map against pristine DSR, and verifies that FogMod can construct the full node graph. The authored tag policy classifies 24 boss encounters from exact pristine event/MSB anchors and 35 connector/optional area identities from explicit project balancing rules; other nodes use an explicit ordinary-progression default. All 131 assigned tag states are compared. Static route policies are separately authored and validated: ordinary routes must stay within one verified map, while only an explicitly classified `CrossMapGlitch` route may cross verified maps, and it must carry the consumed `hard` tag. Same-map hard routes remain valid static routes. Conditions validate against project area IDs and resolved key-item records whose game item IDs exist in pristine GameParam. Any declared destination boss event is checked against pristine facts. Project policy leaves `ScalingBase` unset because the shipped option contract does not expose FogMod's `scalingbase` or `dumpdist` debug switches; the report records this rationale and treats that inactive-field difference as behavior-equivalent. Each row includes only the number of reference static links, never their targets or conditions, so route coverage can be prioritized without copying the oracle graph. Each inventory row also includes unresolved fields; currently 98 areas remain open on `staticLinks`. All 24 boss encounter nodes plus the Catacombs approach to Pinwheel have explicit no-static-link policy; both sides of the Pinwheel gate are anchored to pristine object, trigger-region, and interaction-event identities. The report also includes a fixed-default/fixed-hard combined `Area.To` + `Entrances` + `Warps` diagnostic. It compares FogMod routes through `LinkedExpr`, checks the node inventory and unlinked endpoints, and writes only aggregate route counts. It does not prove randomized graph behavior or executable warp destinations. The pinned `fog.txt` supplies only comparison inventory, aggregate consumed-field counts, tag comparison and R/E/B/U results; reference field values are never emitted or imported. The output must remain outside the repository.

`inspect-reference-area-tag-ids` writes only the three consumed Area tag identity sets (`boss`, `optional`, `trivial`) for difference identification. Treat the output strictly as a comparison checklist; each project-owned tag rule still needs independent semantic and pristine-game validation. Keep the report outside the repository.

`audit-boss-defeat-facts` writes a development-only index of boss-defeat event IDs linked to their unique pristine MSB enemy part, collision, transform, and common reward-flag recognition. Each matched boss also includes up to 40 nearby entity-backed MSB objects and regions for route-boundary review. It requires the byte-verified clean DSR source and treats every row only as evidence; it does not assign an area tag or graph edge. Keep the JSON report outside the repository.

`audit-transition-events` indexes every name-marked transition region and nearby object on the 17 verified maps, then finds raw 4-byte `int32` windows at every argument offset in the corresponding pristine map EMEVD instructions. It records the opcode, instruction index, argument offset, and parameter-binding overlap, but does not decode or classify a hit as a gate. Confirm the instruction schema and event flow before using any match as project policy. The JSON report must remain outside the repository.

`audit-area-gates` inventories pristine MSB ObjAct part/entity/parameter/flag/state bindings, all map-local `SetEventFlag` event/flag pairs, and typed `IfPlayerHasItem`, `IfActionButtonLineSegment`, event-flag and warp operations. It resolves `InitializeEvent` parameter bindings per call site, matches action entity IDs to exact region/object IDs in the same pristine MSB, and resolves short-warp entity IDs to same-map region/object/enemy anchors when unique. The report is evidence only: it does not assign logical area endpoints or decide between `Area.To`, Entrances and Warps. Keep the output outside the repository.

`audit-world-transition-inventory` validates and lists the currently authored project Area routes, no-static-link dispositions, and evidence-backed Entrance boundary policies (currently the Depths-to-Blighttown key door and Pinwheel gate) against byte-verified clean DSR data. For required key items it reports pristine ItemLotParam rows, MSB treasure sources, enemy drop sources resolved through NpcParam, and item-lot awards from typed EMEVD operations `2003[4]`/`2003[36]`, resolving same-archive `InitializeEvent` arguments per caller. It reads no reference transition endpoints or conditions. Missing transition-owner policy remains `UNRESOLVED`; an inventory with unresolved rows is not a world-graph closure result. Keep the output outside the repository.

`inspect-custom-start-boss-proximity` reports project starts' named pristine spawn-region positions and nearest boss-defeat enemy facts on each map. With one custom-start ID, it also lists nearby MSB object and region anchors. This is evidence for manual area-boundary review, not an automatic tag or area assignment.

`inspect-map-transition-candidates` lists pristine MSB regions whose Japanese names mention fog walls, action regions, gates, doors or fog, together with nearby object identities, region extents, and object/region yaw. These are candidate physical boundaries only; proximity and labels do not assign project-owned Areas, Entrances or conditions.

The Event 5700 builder uses named DS1 EMEVD operations and the same 32-byte initializer payload used by the pinned FogMod caller. Its static behavior verifier compares named route/condition/effect contracts and parameter bindings, then applies those contracts to every supplied generated-call row. It is not a full EMEVD VM or a substitute for in-game traversal tests. Detailed event disassembly and value-level call matrices must be written outside the repository.

The area graph behavior audit runs FogMod graph construction, item placement, connector, and progression checking for fixed and randomized profiles. It compares the project Area projection with reference Areas while holding the remaining categories equal. The report may include project-owned Area IDs left unreachable by the project graph, to support diagnosis; it never includes reference Area edge endpoints or conditions. A passing sample is observational evidence, not a proof of equivalence. Keep the report outside the repository.
