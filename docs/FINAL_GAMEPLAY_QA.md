# Final gameplay QA

A checklist for the owner. **The application has not been played for this document**: the automated checks (golden fixtures, real-pipeline sweeps, static verification of the generated files) are in `docs/IMPLEMENTATION_STATUS.md`. Everything below that is not marked as verified by you is **GAMEPLAY UNVERIFIED**.

Play **offline**. Use a copy of the game or a save you do not mind losing; the app can always put the original files back with **Restore original game**.

## How to start any world

1. Open DS1 Multi Randomizer. Nothing needs to be changed in the settings for these tests.
2. **Test worlds**: open **App settings** (left, below the menu) -> **Tools** -> **Test worlds for playtesting** (these internal test worlds are shown only in an INTERNAL build, or in a public build after the shortcut **Ctrl+Shift+Alt+F12**; a normal public build hides them), open the **Test world** list, choose the title written in the tables below *exactly*, scroll down and press **Create this test world**. The app creates that world (it uses the same Load seed path as a shared Seed Code).
3. **Full playthrough worlds** (section 4): press **Load seed** (when a world is already applied it is under **...** next to **Copy seed**, and the button there becomes **Restore and create shared world**), paste the Seed Code into **Shared seed code** and press **Load**.
4. Press **Apply to game**, wait for "Randomized game applied successfully.", press **Launch game** (or start the game from Steam).
5. Afterwards press **Restore original game** (or **Restore and create shared world** to go straight to the next world).
6. If something is wrong: **App settings** -> **Tools** -> **Diagnostics and support**, or the **...** menu of the world in the **Run history** -> **Export support bundle**, and note the Seed Code of the world (shown above the buttons).

Polish interface: **Generuj świat**, **Zastosuj do gry**, **Przywróć oryginalne pliki**, **Przywróć i utwórz nowy świat**, **Wczytaj**, **Uruchom grę**; the test worlds are in **Ustawienia aplikacji** -> **Narzędzia** -> **Światy testowe do grania** -> **Świat testowy** (their titles are translated there; the English titles below are the ones in the English interface).

## 1. Quick smoke tests (about 5 minutes each)

Each one is a world in which the thing under test is next to where you wake up (the Undead Asylum cell). The time is the app's estimate to the target (about a minute) plus about 2-3 minutes for creating the world, Apply and starting the game. The panel shows the exact target and the expected result.

| # | Test world (exact title) | Time | Pass when | Status |
|---|---|---|---|---|
| 1 | Roaming boss - basic | about 3 min | the **Expected** line of the panel is what you see | GAMEPLAY VERIFIED (2026-10-06) |
| 2 | Roaming boss - souls and respawn | about 3 min | the **Expected** line of the panel is what you see | GAMEPLAY VERIFIED (2026-10-06) |
| 3 | First boss fight - a normal enemy | about 4 min | the **Expected** line of the panel is what you see | GAMEPLAY VERIFIED (2026-10-07) |
| 4 | Roaming boss - respawns | about 3 min | the **Expected** line of the panel is what you see | GAMEPLAY UNVERIFIED |
| 5 | Roaming boss - three at once | about 4 min | the **Expected** line of the panel is what you see | GAMEPLAY VERIFIED (2026-10-06) |
| 6 | Regular enemies - first three change | about 4 min | the **Expected** line of the panel is what you see | GAMEPLAY VERIFIED (2026-10-06) |
| 7 | Hostile NPC - Andre as an enemy | about 3 min | the **Expected** line of the panel is what you see | GAMEPLAY UNVERIFIED |
| 8 | Soul items Transposed - a boss weapon in your cell | about 3 min | the **Expected** line of the panel is what you see | GAMEPLAY UNVERIFIED |
| 9 | Key placement - a key in your cell | about 3 min | the **Expected** line of the panel is what you see | GAMEPLAY UNVERIFIED |
| 10 | Lordvessel placement - the Lordvessel in your cell | about 3 min | the **Expected** line of the panel is what you see | GAMEPLAY UNVERIFIED |
| 11 | Lord Soul placement - a Lord Soul in your cell | about 3 min | the **Expected** line of the panel is what you see | GAMEPLAY UNVERIFIED |
| 12 | Race Mode - a key in your cell | about 3 min | the **Expected** line of the panel is what you see | GAMEPLAY UNVERIFIED |
| 13 | Race Mode + - a key in your cell, a world that needs a skip | about 3 min | the **Expected** line of the panel is what you see | GAMEPLAY UNVERIFIED |
| 14 | Item difficulty Fair - what lies in your cell | about 3 min | the **Expected** line of the panel is what you see | GAMEPLAY UNVERIFIED |
| 15 | Item difficulty Unfair - what lies in your cell | about 3 min | the **Expected** line of the panel is what you see | GAMEPLAY UNVERIFIED |
| 16 | Item difficulty Very Unfair - what lies in your cell | about 3 min | the **Expected** line of the panel is what you see | GAMEPLAY UNVERIFIED |
| 17 | Boss sampling - the first boss | about 4 min | the **Expected** line of the panel is what you see | GAMEPLAY VERIFIED (2026-10-06) |

Previously known runtime results are the owner's earlier sessions, recorded in the app (`gameplay` of each world). A world marked GAMEPLAY UNVERIFIED has only been checked statically (the files contain what the panel promises).

## 2. Targeted extended tests

Played from a new game through the first areas; only the parts named are checked. Times are estimates, not measured.

| # | Test world (exact title) | Time | What to look for | Status |
|---|---|---|---|---|
| 1 | Classic full Triple | 20-30 min | Everything on with the older classic placement, plus the fog randomizer: the first areas and the first boss. | GAMEPLAY UNVERIFIED |
| 2 | Roaming bosses with the fog randomizer | 20-30 min | Bosses in regular enemy slots together with the fog randomizer. | GAMEPLAY UNVERIFIED |
| 3 | A normal enemy as a boss body with the fog randomizer | 20-30 min | A normal enemy as the body of a boss encounter, with the fog randomizer. | GAMEPLAY UNVERIFIED |
| 4 | Hostile NPCs with the fog randomizer | 20-30 min | Hostile NPCs with the fog randomizer: friendly NPCs, dialogue and fog gates must keep working. | GAMEPLAY UNVERIFIED |
| 5 | Second Bell Gargoyle | 10-15 min | The Bell Gargoyles rooftop fight with a replaced second body. | GAMEPLAY UNVERIFIED |
| 6 | Boss sampling (every arena) | 30-45 min | Every boss arena draws its own replacement (play the first few arenas). | GAMEPLAY UNVERIFIED |
| 7 | Laundromat Mixup - NPCs in other armor | 15-20 min | NPCs wear other NPCs' armor; nothing you pick up changes. | GAMEPLAY UNVERIFIED |
| 8 | DLC content left out - items, enemies and bosses | 15-20 min | The DLC left out of the randomization; the DLC itself must be intact. | GAMEPLAY UNVERIFIED |

Pass: nothing crashes or freezes, every enemy and boss that appears fights and dies normally, fog gates and NPCs behave as the world description says. Fail: a crash, an invisible or passive enemy, a boss that cannot be killed or does not give its reward, a soft-lock. The panel of each world lists the specific checks under **In this world**.

The other extended worlds (the list under "Extended gameplay tests") are lower priority: *Regular enemies, sampling (Asylum to Parish)*, *Undead Parish with many enemy types*, *The same boss in several places*, *Unique bosses*, *Some bosses stay as they are*, *Type replacement + roaming bosses (Undead Burg and later)*, *Roaming bosses with the second Bell Gargoyle*, *Undead Parish full of bosses*, *A normal enemy in the Taurus Demon and Capra Demon fights*, *Boss bodies and roaming bosses together*, *Bosses or normal enemies in the boss fights*, *Boss bodies with the second Bell Gargoyle*, *Hostile NPCs across the world*, *Hostile NPCs and roaming bosses together*, *Hostile NPCs with Type replacement*, *Hostile NPCs with boss bodies and boss sampling*.

## 3. DLC content

| Check | How | Pass when |
|---|---|---|
| Switch | Main screen, **DLC content** toggle (Polish: **Zawartość DLC**) | On by default; turning the fog randomizer on locks it on with a note |
| Test world | **DLC content left out - items, enemies and bosses** | The base game is randomized and nothing from Artorias of the Abyss shows up in it |
| DLC intact | With the DLC installed, reach it in that world | The DLC area, enemies, bosses and items are exactly as in the normal game |
| DLC on | Any world with **DLC content** on | As before: the DLC enemies, bosses and items take part in the randomization |

## 4. V3 owner gameplay checklist

These six Seed Codes target the current `ds1tr-gen-v3` profile and cover the highest-risk combined settings. Load each code, apply it to the QA game copy, start a new game, and restore the original files before the next world. The exact recipes are in `apps/backend/scripts/qaWorlds.mjs`; `finalQaDoc.test.mjs` checks that each displayed code matches its recipe. These worlds are generated recipes, not gameplay evidence. **Every world below is GAMEPLAY UNVERIFIED.** Estimated times are minimum useful checks, not full playthrough times.

### Recommended Triple v3

* **Seed Code:** `DS1TR1-06NWCQQ3-32HXEE45-H6X6BHNT-8PMWKYDS-Q6MQJAD9-56X2AHCS-0MWTKEM6-MCTX1G6M-80JMB2BS-RM2ZKHD3-EM8AJX8G-E9MM0P85-0GQ4MW2P-7RM8BHB8-8MT88AH2-01GQ92F8`
* **Critical settings:** fresh-install defaults, all three randomizers enabled; Soul items **Transposed**.
* **Check:** Undead Asylum to Undead Parish; first boss gate and reward; follow a randomized fog gate and one Area Warp when found; verify the first boss-soul weapon can be forged if its soul is obtained.
* **Pass:** game starts, transitions complete, enemies fight and die, rewards and item pickups work, and no route soft-locks.
* **Minimum time:** 30 minutes.

### Heavy Fog v3: bosses, PvP, Lordvessel and Warps

* **Seed Code:** `DS1TR1-06NWCQQ3-32HXEE45-H6X6BHNT-36MRJSCN-Q9MZKTDT-GTHHBKS0-NSW7955J-ACNG746A-6GJ5TW3V-FQG0A2J6-42GYBW6H-F86A67J3-5S8JHAY4-R32H3KGA-1JJW06AT-JMCN8355-5T1RGRAM-CQ44JSAR-THJ00MF3-JXV0`
* **Critical settings:** Fog on; traversable fogs, boss fogs, major/minor PvP fogs, Lordvessel gates, Area Warps and Require Lord Souls on; Shuffled keys with Lordvessel/Lord Souls; bosses randomized.
* **Check:** one boss gate, one major and one minor PvP gate, a Lordvessel gate, and the Crow, Painted World and Catacombs/Tomb warp pairs as they are encountered.
* **Pass:** every gate opens under its intended condition, boss encounters start and award correctly, both ends of each used warp return to a playable area.
* **Minimum time:** 45 minutes.

### Random Start + Shuffled Progression v3

* **Seed Code:** `DS1TR1-06NWCQQ3-32HXEE45-H6X6BHNT-T1R035VR-Q941B0XE-W5M3TD48-XCGHV9FC-TK501912-1S4HFQ2Y-3XWFK0GH-91M1JZNM-BB1THNMG-1CCCNTHM-E3MR9CSY-8CMQYHJR-VM332X48-ASPG1V4V-HH80`
* **Critical settings:** random start, traversable/boss fogs and Area Warps on; Shuffled key placement with Lordvessel and Lord Souls shuffled; Require Lord Souls on.
* **Check:** note the actual starting location; verify the first required key is reachable, use the hint sheet to find progression items, and confirm the Firelink Altar/Lordvessel route remains reachable.
* **Pass:** the start is playable, required items are not behind their own gates, the hint sheet agrees with pickups, and a route to required areas exists.
* **Minimum time:** 45 minutes.

### Disconnected Fog + Require Lord Souls v3

* **Seed Code:** `DS1TR1-06NWCQQ3-32HXEE45-H6X6BHNT-S3MMM5WM-41TM2SB2-P6QE2T1N-6J4AP88X-MQPD9JG0-MG90WJ8Q-VHF1YY7H-G88MGT7S-FPT5DGX8-TP8CP5YA-N8T71RC4-PCX46ABY-GSRXB1MW-B04DAV0G-PP1VPRG3-038BRT8J`
* **Critical settings:** disconnected fog gates and Require Lord Souls on, with traversable/boss fogs and Lordvessel gates; Shuffled keys, Lordvessel and Lord Souls.
* **Check:** inspect both sides of disconnected gates; collect Lord Souls and confirm their gates change as intended; verify each required key and the Lordvessel can be reached without crossing a gate that requires it.
* **Pass:** disconnected areas stay isolated until the intended progression condition is met and every required branch can be completed.
* **Minimum time:** 45 minutes.

### Heavy Roaming Bosses + Fog v3

* **Seed Code:** `DS1TR1-06NWCQQ3-32HXEE45-H6X6BHK8-KV84VJTF-TWTHSBC1-8SNT18AM-0CGSE1D4-AMZMKY95-37RBM1TW-HBGARXTG-S84YNNJ1-J150BRJR-SWY3ZC1V-GDSZ4G99-27P74T0Y-0N2HMD7D-M05TE5G7-JWJ5DEN4-4TKC0JJY-VBB6MM5H-25DAJ080-WGSK3XR`
* **Critical settings:** `sampling-v1`; regular enemy replacements include bosses at 100%; roaming boss souls 100% and stay-dead on; Fog, boss/traversable gates, Area Warps and major PvP fogs on.
* **Check:** several regular enemy slots in early areas, one roaming boss death/rest cycle, one arena boss, then one fog gate and one Area Warp.
* **Pass:** roaming bosses spawn, fight, drop the expected souls and respect stay-dead; arena bosses and Fog transitions still work.
* **Minimum time:** 45 minutes.

### Hard Combined Configuration v3

* **Seed Code:** `DS1TR1-06NWCQQ3-32HXEE45-H6X6BHNT-364MAADT-S7WVJJCS-F6MJKEP6-MEAWZG2M-7RJMB2BS-RM2ZKH84-N8FHV9FC-TK50194J-1PMX50PV-XG1JYNHG-G84J77W3-4BC0AQMW-59CM24KY-D5F5MRK6-26QCM3PX-MWHTNDJ3-5SVJHAZA-R22HDKKA-1TJNG6JT-AMF58355-RT1RGRAM-F344J1AT-VX0R8C5B-7E43GSKD-GYBASC2P-ETR48HAC-BM3QAAF9-AMEG04XS-37Q0`
* **Critical settings:** Items on with Shuffled keys, randomized Lordvessel and Lord Souls, **Very Unfair** placement and **Transposed** soul items; Enemies off; Fog on with traversable/boss gates, Warps, major/minor PvP, Lordvessel gates, random start, disconnected/glitched logic, Require Lord Souls, No Fall and Pacifist.
* **Check:** note the random start; use the hint sheet to find the first key; check a Lord Soul gate, the Crow/Painted World/Catacombs warp pairs as found, and a fall-sensitive/Pacifist route. Watch for progression locks, unsafe falls or invalid warp returns.
* **Pass:** required keys remain reachable, the Lord Soul gate opens under its condition, every used warp returns to a playable area, and the hard item mix does not destabilize the world.
* **Minimum time:** 60 minutes.

## 5. Report

For each test write: the title (or the Seed Code), PASS or FAIL, and for a FAIL what happened, where, and the support bundle. A PASS is only reported for the part that was actually played ("partly passed: first bell").

## 6. What this document does not claim

* Nothing here was played by the author of the application; every world is **GAMEPLAY UNVERIFIED** unless the table says otherwise.
* The installer and the portable build are **UNSIGNED**; Windows SmartScreen may warn.

## "Every enemy is a boss" (Bosses only at 100%): a one-minute check (added with backend 0.45.5)

**GAMEPLAY UNVERIFIED** until the owner has played it. It is not a test world of the internal list (nothing is added to the public build); it is a Seed Code and the exact settings.

* **Seed Code** (paste it into **Seed Code** -> **Load a Seed Code**; it makes the world at once):
  `DS1TR1-06NWCQQ3-32GXEE45-H6XAJR3F-G85B8JPX-SHHDTH5G-AXT0T2EP-83M0BVB8-2MT59AS0-YJS6526P-874AA0B9-2M839WRE-NBX0E6HR-MCAH059A-400CFWVG-ZM`
* **Or set it by hand** (Advanced mode): Item randomization OFF, Transition and fog randomization OFF, Enemy randomization ON, Regular enemies ON, Boss randomization OFF, Placement method **Pick a replacement for every spot**, Replacement chance **100%**, Replace regular enemies with **Only bosses**, Size of replacements **Anywhere**, Difficulty matching **Random** (not "Random, easy Asylum"), then Generate world.
* **What you should see:** start a new game: in the Undead Asylum the Hollows and the other ordinary enemies of the first rooms are bosses (Capra, Taurus, Asylum Demon, Pinwheel, Moonlight Butterfly, Smough, Artorias ... the same boss several times is expected); in Firelink, the Burg, the Parish nearly every ordinary enemy is a boss. Not every single spot changes: the few enemies that are scripted, passive or ghosts stay (about 150 of 1,700 in a whole world). Bosses in ordinary spots have no boss bar and no fog gate, drop 50% of their souls and (stay dead OFF) come back after a rest.
* **Planned by the engine for this exact Seed Code** (v2 roaming-boss profile): 1,716 roaming bosses across all eligible ordinary destinations, including the Asylum; 0 ordinary-enemy replacements and no unexplained unchanged destinations. Asylum passive behavior remains until the player attacks.
* **What would be a bug:** a map in which only about one enemy in ten became a boss; an ordinary enemy standing where "Only bosses" should have put a boss (Titanite Demons and Drakes included).


## Nexus candidate automation note (2026-10-14)

The six v3 worlds above were generated by the fresh local Nexus Setup and Portable packages against the safe QA copy. Apply and Restore returned all 73 generated file paths to the pristine snapshot hashes. This verifies package generation and file activation only; it does not mark any of the six worlds as played or gameplay-verified.
