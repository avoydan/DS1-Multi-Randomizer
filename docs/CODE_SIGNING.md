# Code signing policy and SignPath readiness

## Status and scope

This is a preparation document. The project has **not** applied to or been accepted by SignPath Foundation, has no SignPath organization/project/policy, holds no signing certificate, and has no SignPath token or GitHub App installed. No release is signed through SignPath. Do not show SignPath attribution on the README or release page until the Foundation approves the project and issues a certificate.

SignPath Foundation's current published conditions are the source of truth: [Foundation terms](https://signpath.org/terms.html) and [GitHub trusted build system](https://docs.signpath.io/trusted-build-systems/github). Acceptance is discretionary; this checklist is not a claim of eligibility.

## Eligibility preflight

| Requirement | Current evidence | Status |
| --- | --- | --- |
| Public open-source project and OSI-approved license | Public repository `avoydan/DS1-Multi-Randomizer`; GPL-3.0-only project license; Apache-2.0 and MIT dependencies are noticed | Appears met; Foundation decides |
| Active, released, documented software | Maintained code, public release history, README and technical documentation | Appears met |
| Own source and binaries | The app is a combined project with selectively vendored third-party source; not every shipped executable is first-party | Needs Foundation review by artifact |
| Build from source in a verifiable way | Manual GitHub Actions Windows workflow, locked npm install, pinned action SHAs, commit/version/hash manifest, package verification | Partial; not a byte-for-byte reproducible build, no NuGet `packages.lock.json` is present, and the manifest does not record the exact .NET SDK patch |
| GitHub trusted build system | Current release workflow already uses GitHub-hosted Windows runners | Runner requirement appears met |
| GitHub App/audit evidence | No SignPath GitHub App installed | Owner action after acceptance/configuration |
| MFA | Not checked from this local repository | Each team member must enable MFA for GitHub and SignPath |
| Code signing policy and privacy statement | This page plus [`SECURITY_AND_PRIVACY.md`](SECURITY_AND_PRIVACY.md); README links to both | Prepared; role members still need assignment |
| Manual approval per release | No signing request is submitted today | Future workflow must pause for a human approver on every release |
| Modified-upstream lineage | Combined engine is a selective vendor; its pin document says a public project-controlled GitHub fork/source tag is still outstanding. FogHost includes modified FogMod source. | **Not ready to sign these helpers** until Foundation conditions are demonstrated |

The release workflow has been pinned to full action commit SHAs. It remains manual, uses GitHub-hosted runners, and grants `contents: write` only to its release job. Its unsigned public build remains unchanged in purpose and has no SignPath dependency.

## Artifact policy

This project will only request signatures for artifacts that SignPath Foundation explicitly accepts as built from project-maintained source. Preserve upstream signatures where present. Never sign a random third-party binary with this project's certificate.

| Artifact/component | Source and license | Proposed treatment |
| --- | --- | --- |
| Electron main executable (`DS1 Multi Randomizer.exe`) | Electron upstream executable with project resources/name; Electron is third-party OSS | Do not treat as first-party without explicit SignPath approval. Prefer an upstream signature if the packaging process can preserve it; otherwise disclose that it remains unsigned. |
| `DsrDataTool.exe` | Compiled from the GPL-3.0-only selective vendor of `kellernz1/ds-randomizer`, with project changes; uses GPL SoulsFormats | Do not submit now. First establish the project-controlled public fork/tag lineage and verify the upstream publishes signed builds and that required upstream reviews can be met. Preserve GPL notices/source. |
| `FogHost.exe` | Project host plus modified FogMod (Apache-2.0), SoulsIds (Apache-2.0), JuicerMV.SoulsFormats (GPL-3.0-only), YamlDotNet/Newtonsoft.Json (MIT) | Do not submit now. Verify the Foundation's modified-upstream conditions and upstream signed-build history, then ask for approval. Preserve licenses and change notice. |
| NSIS Setup and portable wrappers | Built by electron-builder from this project's packaging configuration, containing the app and upstream components | Possible project package candidates only after SignPath accepts the artifact configuration and the embedded-file policy. Signing the outer wrapper does not mean each embedded executable is signed. |
| Generated uninstaller | NSIS/electron-builder-generated executable | Approval-dependent. Determine whether the approved build configuration can sign it before embedding and verify an installed copy. |
| Electron/Chromium DLLs, Microsoft `d3dcompiler_47.dll`, npm/.NET runtime components | Third-party files, with their own license and signature status | Never sign with this project's certificate. Keep upstream signatures and notices intact. |

Full pins and license evidence: [`THIRD_PARTY_NOTICES.md`](../THIRD_PARTY_NOTICES.md), [`engines/combined/vendor/UPSTREAM_PIN.md`](../engines/combined/vendor/UPSTREAM_PIN.md), and [`tools/fog-host/vendor/UPSTREAM_PIN.md`](../tools/fog-host/vendor/UPSTREAM_PIN.md). Legacy Python randomizers are behavior references only and are not redistributed.

## Proposed signing flow (not implemented)

The current `electron-builder` packaging is one pass: it produces the app folder, installer, portable executable, installer blockmap, and `latest.yml`. Signing a finished Setup executable changes its bytes, so its `latest.yml` SHA-512/size and blockmap must be regenerated from the final signed bytes. The current release output must not be signed after its hashes and update metadata have been staged.

After acceptance, configure and test a separate signing path that:

1. Builds the exact tagged commit on a GitHub-hosted Windows runner with source and build inputs recorded.
2. Produces a workflow artifact containing only the PE files the Foundation approved. Do not include game files or third-party executables for signing.
3. Submits that artifact to SignPath using the accepted organization ID, project slug, signing-policy slug, artifact configuration, and a protected API token; stores the unsigned artifact as a GitHub workflow artifact first. Install the SignPath GitHub App for audit evidence and keep a named human approver for every release request.
4. Reassembles the app using the exact signed inner executable bytes. Prove the packaging step did not rebuild or overwrite them. Sign the uninstaller before it is embedded if it is approved.
5. Produces Setup and portable from that signed payload. If the outer packages are approved for signing, sign them as their own final artifacts. Do not sign third-party files.
6. After all approved PE signatures are final, regenerate the installer blockmap and `latest.yml` from the final Setup bytes; then regenerate the release manifest and `SHA256SUMS.txt` from the final deliverables.
7. Run the release verifier against the final files. Require valid Authenticode signatures, timestamps, and the expected publisher for each approved path; verify explicitly that unsigned files are not labelled signed. Compare the Setup metadata, blockmap, `latest.yml`, manifest, and SHA-256 sums to the exact final bytes before publishing.

The repository already has `release-verify.mjs --require-signature --expected-publisher ...` checks for signature validity, timestamp, publisher, and the current configured executable/artifact list. That list currently reflects the existing local-certificate pipeline; it is **not** proof of SignPath eligibility and must be reconciled with the Foundation-approved artifact configuration before SignPath is enabled. The current public workflow uses `--unsigned-release` and publishes unsigned output.

SignPath's required public attribution, once and only once the Foundation project/certificate is approved, is:

> Free code signing provided by SignPath.io, certificate by SignPath Foundation

At that time the project home page and download/release pages must also link to the signing policy, identify author/reviewer/approver roles and their members, and link to the privacy statement. This required wording is intentionally absent from the current public pages because it would falsely imply sponsorship today.

## Owner checklist

The repository cannot complete these account-controlled steps:

1. Review the current [SignPath Foundation conditions](https://signpath.org/terms.html) and submit the public project for acceptance. Disclose the selective engine vendor and modified FogMod sources; ask which artifacts, if any, they will accept for signing.
2. Enable MFA on the GitHub and SignPath accounts for every person who will participate.
3. After acceptance, create the SignPath organization/project and signing policy, assign author, reviewer, and approver roles to named people, and install the SignPath GitHub App with access to this repository.
4. Provide the actual organization ID, project slug, signing-policy slug, artifact configuration and API token through the approved secret store. Never commit credentials or add placeholder secrets.
5. Agree with SignPath on eligible PE paths and metadata rules. In particular, reconcile the version metadata of the desktop app and the separately versioned helper executables with the Foundation's product/version restrictions.
6. Test the proposed signing sequence on a non-public draft and verify every nested signature and the final update metadata. Keep the existing unsigned workflow until this review passes.

No application, account setting, GitHub App, secret, certificate, or signing workflow was changed as part of this preparation.

## Owner decision brief: signing options (researched 2026-11-01)

Nothing was bought, no account was created and nothing was applied for. Prices and eligibility are from the vendor/Microsoft pages named below on that date and change; re-check before acting. Microsoft's own current statements (Learn, "SmartScreen reputation for Windows app developers", updated 2026-08) say: a valid OV or EV certificate still shows "unrecognized app" warnings until reputation accumulates; "EV certificates no longer bypass SmartScreen" and paying a premium for EV solely to avoid warnings "is no longer justified"; reputation builds from download volume and behaviour ("several weeks and hundreds of clean installs" is Microsoft's rough guide, not a threshold); for Artifact Signing signed apps the first downloads may also see a prompt. **No option below removes the first-release warning.** Smart App Control (on some Windows 11 PCs) blocks unsigned apps and gives no per-app bypass, so signing matters most for users who have it enabled.

| Option | Cost model | Who can get it | Setup / CI | Publisher identity | SmartScreen / Smart App Control |
|---|---|---|---|---|---|
| **A. Stay unsigned** | Free | Anyone | Nothing to do | None ("unknown publisher") | Warnings on every new file until reputation accumulates (it starts from zero per file); Smart App Control blocks it. Mitigate with checksums, public source, GitHub attestation (GitHub edition), Nexus scan. |
| **B. Trusted OV / individual-validation certificate** (for example SSL.com IV listed at about US$129 per year; Certum "Open Source Code Signing" listed from about EUR 69 including a hardware card, stock and eligibility unconfirmed) | Yearly; CA/B Forum rules cap validity at 460 days since 2026-03-01 and require hardware-protected keys (since 2022-05-09), so a USB token (SSL.com lists +US$379) or a paid cloud-signing service is usually needed | Individuals via IV products; organizations via OV | Medium to hard: token signing is manual; CI needs a cloud signer (price of such services not confirmed here) | Your validated name (individual) or organization | Valid signature satisfies Smart App Control; SmartScreen still warns until reputation builds, per Microsoft. |
| **C. Microsoft Artifact Signing** (formerly Trusted Signing) | Microsoft lists "starts at $9.99/month" (Basic); the Azure pricing page did not render prices, so treat as unconfirmed; billing starts at account creation, paid subscription required | **Public Trust: organizations in the US, Canada, EU, UK, Australia, New Zealand, Japan, South Korea, Singapore, Switzerland, Norway, Israel; individual developers only in the US or Canada** (Microsoft quickstart). Check your own country and legal status; whether a sole proprietorship counts as an organization was not confirmed | Azure portal setup, identity validation (1 to 20 business days), role assignments; GitHub Action `azure/artifact-signing-action@v2` with OIDC login (Windows runners) fits the existing workflow; certificates are short-lived, so every signature must be timestamped | Identity-validated name, not customizable | Valid signature (satisfies Smart App Control); reputation still accumulates; Microsoft does not say warnings vanish. |
| **D. Microsoft Store** (future) | Microsoft states no registration fee for new individual or company developer accounts | Individuals allowed for non-business hobby projects (ID and selfie check) | MSIX packaging is a different build path (MSIX is signed by the Store); an EXE/MSI submission must be signed by you, be silent-installable and have a versioned HTTPS URL | Store-listed publisher | Store apps are not subject to SmartScreen download warnings. Store policy text does not address game-modding tools, so acceptance is at reviewer discretion; the Store build would also be a separate release. Not a near-term plan. |

SignPath Foundation (free open-source signing) is described above; its website could not be re-verified in this research pass (a TLS certificate error), so confirm its current terms yourself.

**Recommendation.** *Now:* ship unsigned for the first Nexus release, say so plainly, and rely on checksums, the public source tag, Nexus's scan and (for the GitHub edition) build provenance; do not buy anything for the first release. *Later, in this order:* (1) Artifact Signing if the owner's country and legal status are eligible, because it is cheap and fits CI; (2) otherwise apply to SignPath Foundation (discretionary, see the preflight above); (3) otherwise an individual OV/IV certificate with a cloud signer. Do not choose EV for SmartScreen reasons. Whatever is chosen, expect warnings on early downloads and never describe the build as "trusted" or "safe" because it is signed.
