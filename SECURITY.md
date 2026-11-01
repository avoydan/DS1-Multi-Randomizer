# Security policy

## Supported versions

Only the **latest published release** of DS1 Multi Randomizer receives security fixes. Older releases are not patched: a problem is fixed in a new, higher version (a published release is never edited or replaced). Check the [releases page](https://github.com/avoydan/DS1-Multi-Randomizer/releases) for the current version.

## Reporting a vulnerability

Please report suspected vulnerabilities **privately**, not in a public issue.

1. Open the repository's **Security** tab and look for **Report a vulnerability** (GitHub private vulnerability reporting). Use it if it is shown.
2. If that button is not available, open a public issue titled **"Security contact request"** that contains **no technical details, no exploit and no file attachments**, only that you have a security report and how you prefer to be reached. The maintainer will arrange a private channel.

No security mailbox is published for this project; do not send reports to addresses that are not listed here.

Please include, as far as you can:

- the application version and where you downloaded it (GitHub release, Nexus Mods);
- what you observed and what you expected, with minimal steps to reproduce;
- the impact you believe it has (for example arbitrary file write outside the game folder, code execution, a tampered release file);
- for a suspected tampered download: the file name, its SHA-256 (`Get-FileHash <file> -Algorithm SHA256`) and the SHA-256 listed for that version.

Do not include saves, private game files or personal paths unless you have removed or reviewed them.

## What to expect

This is a volunteer-maintained hobby project. There is no service-level agreement. Reports are acknowledged and triaged as time allows. Please give the maintainer reasonable time to investigate and release a fix before you disclose details or an exploit publicly; coordinated disclosure after a fix is released is welcome and will be credited if you wish.

## Scope

In scope: the application and helper executables built from this repository, the release workflow and the published release files. Out of scope: the Dark Souls games themselves, Windows, Electron/Chromium vulnerabilities that are not specific to how this project uses them (report those upstream), and antivirus false positives (see [docs/ANTIVIRUS_FALSE_POSITIVES.md](docs/ANTIVIRUS_FALSE_POSITIVES.md)).

## Verifying what you downloaded

See [Verify a download](README.md#verify-a-download) and [docs/SECURITY_AND_PRIVACY.md](docs/SECURITY_AND_PRIVACY.md). Releases are currently **unsigned**; checksums and (from the release after v0.40.0) GitHub build provenance attestations let you check that a file is the one the project built, not that it is free of defects.
