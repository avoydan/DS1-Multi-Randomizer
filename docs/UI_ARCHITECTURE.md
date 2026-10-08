# User interface architecture (desktop 0.35.0)

The interface is a view of the configuration. It never decides what a world is: Basic / Advanced, the language, the help, the navigation and the sidebar consume no randomness, mutate nothing and are in no Seed Code, preset or history entry. Every control goes through one registry.

## One registry, one configuration

* `packages/contracts/src/settingsRegistry.ts` is the single description of every option: its field (`items.randomizeWorldItems`), its kind (toggle, select, range, pool), its section, whether the Basic view shows it, when it is available and **why not** (`settingState`), what changes together with it (`applySetting`) and which advanced values differ from the recommended ones (`customAdvancedSettings`). It is pure and tested in node; the generation never reads it, the backend validates the same combinations on its own (`configValidation.ts`).
* Labels and help live in the translation resources under the same id: `settings.<id>.label`, `settings.<id>.options.<value>`, `help.settings.<id>.{short,what,affects,limits,example,choices.<value>}`, `help.reasons.<reasonId>`. English is canonical, Polish is complete (`i18n/locales.test.mjs`).
* `docs/SETTINGS_INVENTORY.md` and `docs/settings-inventory.json` are generated from the registry, the resources and the defaults (`apps/desktop/scripts/settings-inventory.mjs`; `--check` and a test fail when they are out of date).
* The coupled resets are the ones the product always applied, now in one place (`applySetting`): the placement method puts the sampling-only options to that method's defaults, leaving the key modes turns Lordvessel / Lord Soul placement off, Race Mode + sets Fair, leaving a boss mode puts its chance / souls / stay-dead back, a custom pool turns hostile NPCs off, the fog randomizer puts the DLC back in. Nothing else is ever rewritten, and nothing is reset because a view or a language changed.

## Navigation (sidebar)

Your game (settings, run panel, history) · Presets (current settings, restore the recommended ones, preset or Seed Code) · Seed Code (the current world, load a code, what a code contains) · Import / Export (presets as text) · App settings (language, tools: plan preview, game files check, diagnostics, logs, internal test worlds) · About (version, licence). Loading a Seed Code still generates that world straight away.

## Basic and Advanced

* **Basic** (default): *Main options* (Item randomization, Enemy randomization, Transition and fog randomization, Boss randomization), *Additional options* (NPC gifts, Shops), *DLC content*, *Result file* (spoiler file, with the warning that it reveals the world), Generate world, Load seed.
* **Advanced**: seven accordions with their option counts, each option with its own `?`: Items (10), Enemies (11), Bosses (7), Transitions and fog (15), Keys and progression (4), Classic options (10), World and output (2) = 59 options.
* They are two views of one state. Switching never resets anything. When advanced values differ from the recommended ones the Basic view says **"Custom advanced settings are active."** (PL: "Aktywne są niestandardowe ustawienia zaawansowane.") and offers **Show them in Advanced mode**, which opens the sections that hold the changed values. The chosen view is a preference of the window (local storage), not part of the settings.

## Help

Every drawn option has a `?` button next to its name. Hover or keyboard focus shows the short explanation (tooltip); Enter / Space / click opens the full panel (what it does, what it affects, limits and dependencies, an example where one helps, an explanation of **every choice** of a dropdown, the one currently selected marked, and a note when the evidence is only partial). The `?` stays usable on disabled options; the panel then names the reason. A select also shows the explanation of the selected choice under itself. Escape closes the panel. Coverage is enforced: 59 / 59 drawn options, 15 selects with all their choices, both languages (`src/ui/settingsCoverage.test.mjs`).

## Dependencies

Nothing is hidden without a word. An option that cannot be used now is **disabled and says why** in the row (`help.reasons.*`), and keeps its value (switching Starting equipment off does not reset Starting weapon types). The test walks the configuration space and checks that every disabled state carries a reason that is written in both languages and that each of the 19 reasons is reachable. Options that cannot do anything in this version are **not drawn at all** (see below), and are not counted as custom values.

## Options that are not drawn

`items.guaranteeEarlyWeapon` ("Guarantee early weapon") and `items.balancedEarlyLoot` ("Balanced early loot") have **no effect** and never had any with real game data (not features of the original randomizers either: `docs/EARLY_OPTIONS_AUDIT.md`): the extracted-data path of the combined engine never reads them; the real pipeline produces identical files with them on and off (`apps/backend/scripts/settingEffectReal.mjs`). `items.randomizeProtectedItems` is refused by validation. The fields stay in the configuration, in presets and in Seed Codes, so an old code means what it always meant; only the control is not drawn. The audit also found that "Progressive" enemy scaling produces the same files as "By area"; the choice stays and says so.

## Build channels

`build-info.json` carries a `channel`: `public` (what a player gets; a `--release` build is always public) or `internal` (testers). The internal test worlds are shown only on the internal channel; in a public build the optional shortcut **Ctrl+Shift+Alt+F12** shows them for the session. This only decides what the interface shows; it is not a security boundary. Hint sheet, Diagnostics, Support bundle and Run history are public. `release-verify.mjs` refuses a signed public release that is not a public-channel build. A packaged app that cannot read its build information is treated as public.

## Accessibility

Switches are native checkboxes with `role="switch"` and labels; help buttons have labelled names (`aria-label`, `aria-expanded`, `aria-controls`, `aria-describedby`); accordions are buttons with `aria-expanded` / `aria-controls`; mode buttons use `aria-pressed`; reasons and warnings are `role="note"` and linked with `aria-describedby`; focus is always visible (gold outline); no text is below 11 px (tested); layout reflows at 1180 / 1000 / 760 px, the help panel becomes a bottom sheet on narrow windows; there is no horizontal scrolling at 1366×768 (100 %, 125 %, 150 %) or 1920×1080.

## Terminology (before → after)

| English before | English now | Polish now |
|---|---|---|
| Randomize | Generate world | Generuj świat |
| World items | Items lying in the world | Przedmioty leżące w świecie |
| Protect selected gift/shop progression entries | Protect progression gifts and shop goods | Chroń dary i towary sklepowe ważne dla postępu |
| Starting class | Starting class stats | Statystyki klasy startowej |
| Starting items | Starting weapon types | Rodzaje broni startowej |
| Fashion Souls | Split armor sets | Rozdzielaj zestawy zbroi |
| Laundromat Mixup | NPC outfits | Stroje NPC |
| Soul items | Soul items (choices spell out what happens) | Przedmioty-dusze |
| Difficulty | Item difficulty | Trudność przedmiotów |
| Placement algorithm: Sampling / Classic permutation | Placement method: Pick a replacement for every spot / Shuffle existing enemies | Metoda rozmieszczenia: Wybierz zamiennik dla każdego miejsca / Przetasuj istniejących wrogów |
| Scaling | Enemy strength scaling | Skalowanie siły wrogów |
| Normal enemy replacement | Replace regular enemies with | Zwykłych wrogów zastępuj |
| Placement (fit) | Size of replacements | Rozmiar zamienników |
| Difficulty mode / strictness | Difficulty matching / Matching strictness | Dopasowanie trudności / Ścisłość dopasowania |
| Type replacement | Replace by kind | Zamiana według rodzaju |
| Prevent same enemy | No replacement by the same kind | Bez zamiany na ten sam rodzaj |
| Pinwheel Chaos | Keep Pinwheel, replace its clones | Zostaw Pinwheel, zastąp jego klony |
| Replace second Gargoyle | Second Bell Gargoyle | Drugi Bell Gargoyle |
| Gwyn spawn nerf | Lord Gwyn as a replacement boss | Lord Gwyn jako boss zamiennik |
| Traversable non-boss fogs | Ordinary fog gates | Zwykłe bramy mgły |
| Warps between areas | Warps between locations | Teleporty między lokacjami |
| Lordvessel gates | Golden Lordvessel fog gates | Złote bramy mgły Lordvessel |
| Random start outside Asylum | Random start | Losowy start |
| Scale enemies and bosses (fog) | Strength by distance from the start | Siła według odległości od startu |
| Glitched logic | Allow skips and glitches | Dopuść skipy i glitche |
| In-game text language | Language of fog messages | Język komunikatów mgieł |
| Generate spoiler log | Save the spoiler file | Zapisz plik ze spoilerami |

Config keys, enums and serialized values did not change.

## Verified meanings (what the help says, and where it comes from)

* **Split armor sets (Fashion Souls)**: on = each armor piece is shuffled on its own; off = regular armor sets found in the world are exchanged as whole sets (engine config and generator).
* **Soul items**: *Shuffled* like any item; *Replaced* turns the smaller souls (Soul of a Lost Undead … Soul of a Great Hero) into other consumables before shuffling; *Transposed* adds the boss souls to the pool and each has a 75 % chance to become a weapon made from that soul.
* **Keep Pinwheel, replace its clones (Pinwheel Chaos)**: on = the main Pinwheel in the Catacombs stays and is not used as a boss elsewhere, its fourteen one-hit clones are replaced by regular enemies with normal health.
* **Replace respawning mosquitoes**: off = the six respawning Blighttown swamp mosquitoes stay and are not used elsewhere.
* **No replacement by the same kind (Prevent same enemy)**: a regular enemy is never replaced by an enemy of its own kind.
* **Pacifist mode**: in the warps made for boss-arena exits the two flags that tie leaving to defeating the boss are cleared (read from FogMod's own code, `GameDataWriter.cs`; the exact in-game effect was not observed: the help says so).
* **Guaranteed early weapon / Balanced early loot**: no effect (see above).
* **Roaming boss souls**: the share of the souls a boss gives in its own arena that it drops in a regular spot. **Roaming bosses stay dead**: on = no respawn (played in the game).
* **Lord Gwyn as a replacement boss (Gwyn weakening)**: the chance Gwyn is rejected when a boss is drawn (Strong 85 %, Medium 60 %, None 0); it changes nothing about Gwyn himself.
* **Fog**: ordinary gates, boss gates, warps, major / minor PvP gates (the gates the fog data tags as PvP; partial evidence), golden Lordvessel gates, random start (sixteen start points), disconnected gates (the two sides of a gate randomized independently), strength by distance from the start, skips and glitches counted as usable, the Kiln of the First Flame kept at the end.

## Differences from the mockup (`docs/ui-redesign/ds1tr-ui-redesign-mockup.png`)

* Basic and Advanced are separate views (as the specification says), not one scrolling page.
* "Guarantee early weapon" and "Balanced early loot" are not shown in Basic (they do nothing in this version).
* The window keeps the native frame (no custom title bar); the icon-size preview block of the mockup is presentation, not implemented.
* The game folder / readiness card sits above the settings, the run panel and the help panel share a sticky right column; on narrow windows the help panel is a bottom sheet.
