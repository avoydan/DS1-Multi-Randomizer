# DS1 Multi Randomizer

DS1 Multi Randomizer is a Windows desktop application for generating one
deterministic Dark Souls Remastered randomization from item, enemy/boss, and
FogMod settings.

## For players

1. Download the **installer** (`DS1-Multi-Randomizer-Setup-<version>.exe`) and run it. It installs for your user only and never asks for administrator rights. A **portable** build (`DS1-Multi-Randomizer-<version>-x64.exe`) is also offered: it unpacks itself to a temporary folder every time it starts, which some antivirus products dislike, so the installer is the recommended one.
2. Choose your settings. New settings start with the recommended **Sampling** enemy placement; older Seed Codes and saved settings keep the algorithm they were made with.
3. Click **Randomize**, then **Apply to game**.
4. Click **Launch game** (or start Dark Souls Remastered from Steam) and play offline.

Every run gets a **Seed Code** (it starts with `DS1TR`). Share it with a friend: they click **Load seed**, paste it, and get
exactly the same world. Seed Codes include the randomizer settings needed to reproduce the same world, and they keep working in
later versions of the app. The app also remembers your settings and lists your earlier runs in **History**, where you can apply,
copy any of them, or create their files again. After **Apply to game** you can start Dark Souls Remastered with **Launch game**.

**Language.** The app speaks **English** and **Polish**. It follows your Windows language by default (*Automatic*); change it any time with **Language** in the header (Automatic / English / Polski). The choice is remembered and never changes a world: the same Seed Code gives the same world in either language. The installer also follows your Windows language.

**Hints.** After a world exists, **… → View hints** opens a read-only **hint sheet** with three spoiler levels (*Low spoilers*, *Progression*, *Full spoilers*); you can switch the level, search, and export it as **HTML**, **TXT** or **JSON** in English or Polish. It works for any world in **Run history** too (the **…** menu of its row) and changes nothing in the game.

**DLC.** **DLC content** (on by default) decides whether the Artorias of the Abyss areas, enemies, bosses and items take part in the randomization. Turn it off and they stay out of it: the DLC itself stays in your game exactly as it is, it is only not randomized (see `docs/DLC_AUDIT.md`). The fog randomizer always includes the DLC areas, so the switch is locked on while it is on.

**Something went wrong?** Open **Advanced → Diagnostics and support**. **Copy diagnostics** gives a short summary; **Export support bundle** saves one ZIP (summary, diagnostics, manifest, the newest log lines) to a place you choose. It contains **no game files** and no personal folders: your Windows user name, computer name, folders and any password or token are removed before it is saved. Nothing is sent anywhere; you decide who gets the file.

**Builds are not code-signed yet** (`docs/RELEASE_TRUST.md`): Windows SmartScreen and some antivirus products may warn about an unrecognised publisher. Check the SHA-256 of your download against the one published with the release. Nothing is uploaded or collected by the app (`docs/SECURITY_AND_PRIVACY.md`).

You need Windows and your own copy of Dark Souls Remastered on Steam. Nothing else has to be installed or downloaded by
hand: the app finds the game, prepares everything it needs (the world randomizer fetches a small set of data files once
and then works offline) and can put your original game files back at any time with **Restore original game**.

## Architecture

```text
Electron + React renderer
        |
        v
typed local HTTP API
        |
        v
Node/TypeScript backend
   |             |
   v             v
combined DSR   FogMod host
items/enemies  world/fog
        \       /
         workspace
            |
      validation/package
            |
        activation
```

The renderer never modifies game files and never imports randomizer code.
Generation, workspace management, validation, activation, restore, and run
manifests belong to the backend.

## Engine strategy

The primary Items + Enemies candidate is a pinned GPL-3.0-only fork of
[kellernz1/ds-randomizer](https://github.com/kellernz1/ds-randomizer).

Fog generation will use a dedicated headless host around
[thefifthmatt/FogMod](https://github.com/thefifthmatt/FogMod).

The legacy Python randomizers are behavior references only. Their repositories
do not currently declare redistribution licenses, so their source and binaries
must not be included without written permission.

See [the option matrix](docs/OPTION_MATRIX.md) for native settings and planned
independent compatibility extensions.

## Current v0.2 status

Implemented:

- Electron, React, TypeScript, and Vite desktop scaffold;
- separate Node/TypeScript backend;
- shared typed contracts;
- Dark Souls Remastered installation validation;
- deterministic combined/Fog seed derivation;
- one combined Items + Enemies pipeline stage;
- executable settings model for combined/FogMod plus a visible, disabled
  catalog of planned legacy-parity extensions;
- GPL-3.0-only project licensing and third-party policy.

Not implemented yet:

- pinned combined-engine fork;
- transactional workspace and pristine snapshot;
- combined adapter execution;
- FogMod compatibility bridge and headless host;
- final validation, activation, and restore;
- independent implementation of legacy-only option behavior.

## Development

Requirements:

- Node.js 22 or newer;
- npm;
- Windows for Electron and Dark Souls Remastered integration;
- .NET tooling for the future FogMod host.

```bash
npm install
npm run dev
```

Build and type-check:

```bash
npm run build
npm run typecheck
```

## Legal boundaries

The application operates on a user's own game installation. Do not commit or
redistribute Dark Souls binaries, assets, extracted catalogs, or generated game
packages. The project is unofficial and is not affiliated with FromSoftware or
Bandai Namco Entertainment.

See [LICENSE](LICENSE), [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md), and
[docs/LICENSING.md](docs/LICENSING.md).
