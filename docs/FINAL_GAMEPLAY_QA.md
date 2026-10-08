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

## 4. Full playthrough worlds

Press **Load seed**, paste the Seed Code into **Shared seed code**, press **Load**, then **Apply to game**. Each is meant to be played from a new game as far as the "Stop at" point; a full run to the end is welcome but not required. Times are rough estimates. **Static check (AUTOMATED VERIFIED):** every world below was generated twice through the real pipeline on a copy of the game (`node apps/backend/scripts/qaWorlds.mjs verify`) and the two packages are identical; the recipes are in `apps/backend/scripts/qaWorlds.mjs` and a test fails if a code in this document is not the code of its recipe.

### Recommended Triple

* **World:** The recommended settings (fresh-install defaults plus Soul items **Transposed**): items, regular enemies and bosses with the fog randomizer.
* **Time:** About 2-3 h.
* **Stop at:** The Bell of Awakening in the Undead Parish.
* **Look for:** Fog gates and warps lead somewhere and the game never traps you; bosses appear and reward their souls; weapons made from boss souls can be forged.
* **Status:** GAMEPLAY UNVERIFIED

```
DS1TR1-06NJDCP6-556D8KEB-SH5WSM9D-996WXKYD-9Q6MPJAD-T4THSBBX-0TKFCA99-9B62PBP8-5WFATZT8-NNZS0JR2-S8N1YW1H-GASY4G99-5132Q891-AG4G204R-25WWT
```

### Chaotic Triple

* **World:** Everything high: all item pools, bosses in regular slots and normal enemies in boss slots, hostile NPCs, fog randomizer.
* **Time:** About 2-3 h.
* **Stop at:** The first bell.
* **Look for:** No crash, no passive or invisible enemy, no boss that cannot be killed; friendly NPCs still talk.
* **Status:** GAMEPLAY UNVERIFIED

```
DS1TR1-06NJDCP6-556D8KEB-SH5WSMAD-SS4CRBY9-9KB3A77H-6M7SNEE8-EM5SMEW9-N5VJ0PG8-AMXJAHC9-F72GBYE5-MCAGZA8N-1XS1305N-TRRWEMF9-4KJSES0C-BJ6G7AA2-P2X0G901-E6Y6E7H8-NT177WJ1-S79CRG27-54HVZE14-6CKXBBR0-2RST658Q-9MNJW06A-HCKME
```

### Fog with shuffled keys

* **World:** Fog randomizer with **Key placement: Shuffled**, Lordvessel and Lord Souls shuffled.
* **Time:** About 3-4 h.
* **Stop at:** Anor Londo, or the point where a key is needed.
* **Look for:** The keys can be found; no area needs a key you cannot reach; Firelink Altar stays reachable (the hint sheet shows where the keys are).
* **Status:** GAMEPLAY UNVERIFIED

```
DS1TR1-06NJDCP6-556D8KEB-SH5WSMAD-SD7XFKAE-NMPDCD8W-NNX0CWEN-MEHS97AT-36059AH1-X61DQTR3-5X9300G9-5P7RZNK4-30THJWMS-89CKA1HE-J1R5CR98-8Q76GX9P-GJN3606Y-Z4DBT
```

### Race Mode

* **World:** **Race Mode** without fog or enemy randomization: keys and embers only in boss rewards, chests and key places.
* **Time:** About 3-4 h.
* **Stop at:** The first three keys or the first bell.
* **Look for:** The hint sheet (**...** -> **View hints** on the world, Progression level) matches what you find; no key is unreachable.
* **Status:** GAMEPLAY UNVERIFIED

```
DS1TR1-06NJDCP6-556D8KEB-SH5WSM9D-9964XN9N-3JPQC1N6-V8MJJJPC-5CQCGBS6-A3RYGA35-MXB0C84N-D52S0RGS-DTNGZF1G-R4425D6W-HPHGWGZQ-RT4ANH4G-HC9WMTHG-E1CR9CZP-88MPTT2Z-JD0M6TDM-5AHM4N86-03X62DVN
```

### Race Mode +

* **World:** **Race Mode +**: the world may need a skip or glitch (the Capra skip, the Seal skip).
* **Time:** About 3-4 h.
* **Stop at:** The first skip the hint sheet names.
* **Look for:** Where a skip is needed, it works as the hint sheet says; a needed skip is a property of this mode, not a defect.
* **Status:** GAMEPLAY UNVERIFIED

```
DS1TR1-06NJDCP6-556D8KEB-SH5WSM9D-9964WBE8-54PXCD8W-NNX0CWEN-MEHS97AT-36059AM1-W96HE53H-9941DYY0-SCA8RM22-SFJ3HAHJ-EJFRSA5T-1JWN98DC-481HEJ9R-5CRS8GM7-YRAHMD4M-8SNK8APM-D00AWDAD-CW
```

### DLC content left out

* **World:** **DLC content** off with all item pools and Soul items **Transposed**, no fog randomizer.
* **Time:** About 2 h to the first bell, plus 20 min in the DLC if you own it.
* **Stop at:** The first bell; then the DLC.
* **Look for:** No DLC item or enemy appears in the base game; the DLC itself is untouched.
* **Status:** GAMEPLAY UNVERIFIED

```
DS1TR1-06NJDCP6-556D8KEB-SH5WSMAD-S54XDKAF-9F9KA77H-6M7SNEE8-EM5SMEW9-N5VJ0PG8-AMXJAHC9-F72GBYE5-MCAGZA8N-1XS12G2S-NM1JWFVG-TRW2G984-XNNHYT18-HNB3Z52M-7XG2VDDN-PM0DB5YR-78
```

## 5. Report

For each test write: the title (or the Seed Code), PASS or FAIL, and for a FAIL what happened, where, and the support bundle. A PASS is only reported for the part that was actually played ("partly passed: first bell").

## 6. What this document does not claim

* Nothing here was played by the author of the application; every world is **GAMEPLAY UNVERIFIED** unless the table says otherwise.
* The installer and the portable build are **UNSIGNED**; Windows SmartScreen may warn.

## "Every enemy is a boss" (Bosses only at 100%): a one-minute check (added with backend 0.45.5)

**GAMEPLAY UNVERIFIED** until the owner has played it. It is not a test world of the internal list (nothing is added to the public build); it is a Seed Code and the exact settings.

* **Seed Code** (paste it into **Seed Code** -> **Load a Seed Code**; it makes the world at once):
  `DS1TR1-06NJDCP6-556D8K85-FC9N982N-X9K1DTS6-GAYT4TT8-P0344BV8-8YNT18AT-0PGSE5A3-PG744BGD-92NR184S-EX8DAFYG-R0CTV058-A00G2073-EA43C`
* **Or set it by hand** (Advanced mode): Item randomization OFF, Transition and fog randomization OFF, Enemy randomization ON, Regular enemies ON, Boss randomization OFF, Placement method **Pick a replacement for every spot**, Replacement chance **100%**, Replace regular enemies with **Only bosses**, Size of replacements **Anywhere**, Difficulty matching **Random** (not "Random, easy Asylum"), then Generate world.
* **What you should see:** start a new game: in the Undead Asylum the Hollows and the other ordinary enemies of the first rooms are bosses (Capra, Taurus, Asylum Demon, Pinwheel, Moonlight Butterfly, Smough, Artorias ... the same boss several times is expected); in Firelink, the Burg, the Parish nearly every ordinary enemy is a boss. Not every single spot changes: the few enemies that are scripted, passive or ghosts stay (about 150 of 1,700 in a whole world). Bosses in ordinary spots have no boss bar and no fog gate, drop 50% of their souls and (stay dead OFF) come back after a rest.
* **Planned by the engine for this exact Seed Code** (real catalog, checked before it was written down): 1,559 roaming bosses in the world, 26 of them in the Undead Asylum, 0 ordinary-enemy replacements, nothing left unchanged for a counted reason; the first Asylum enemies are bosses such as the Pinwheel, the Asylum Demon variants, Artorias, Smough and the Moonlight Butterfly.
* **What would be a bug:** a map in which only about one enemy in ten became a boss; an ordinary enemy standing where "Only bosses" should have put a boss (Titanite Demons and Drakes included).
