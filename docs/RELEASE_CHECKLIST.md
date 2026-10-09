# Release checklist

The short version. The reasons are in [AUTO_UPDATE.md](AUTO_UPDATE.md) (updates, workflow) and [RELEASE_TRUST.md](RELEASE_TRUST.md) (signing, trust).
The workflow stops at a draft. Publish as a **public prerelease** by default; publish stable only when explicitly requested.

## Once, before the first release

- [ ] The update source is set: owner and repository in `apps/desktop/electron/updateSource.ts` **and** `apps/desktop/electron-builder.yml` (`node tools/release/update-source.mjs` prints `owner/repo`).
- [ ] The repository is pushed, Actions are enabled, workflow permissions are "read" (the workflow asks for `contents: write` itself). Two-factor authentication is on.
- [ ] Separate release repository only: secret `RELEASE_REPO_TOKEN` (fine-grained, that repository only, Contents read/write) and variable `RELEASE_REPOSITORY`.
- [ ] Record the N to N+1 QA from [AUTO_UPDATE.md](AUTO_UPDATE.md), section 11 before calling the updater end-to-end verified.

## Every release

1. [ ] Bump the versions (desktop, and backend/contracts when they changed), update the notes in `docs/IMPLEMENTATION_STATUS.md`; notes file `docs/release-notes/vX.Y.Z.md` (copy the previous one: it opens with the Recommended = Setup / Portable names and one line on each). Commit.
2. [ ] Working tree clean; `npm test -w @ds1tr/backend`, `npm test -w @ds1tr/desktop`, `npm run typecheck` pass locally.
3. [ ] Golden fixtures on your computer: `node apps/backend/scripts/goldenCompatibility.mjs verify` is all PASS (a clean game copy; never the live Steam folder).
4. [ ] Push the commit.
5. [ ] GitHub > Actions > **Release (draft)** > Run workflow: `version` = `X.Y.Z` (must equal `apps/desktop/package.json`), `golden_attestation` = first 7 characters of the commit from step 3.
6. [ ] The run is green. Download the draft's files: `DS1-Multi-Randomizer-X.Y.Z-Setup-x64.exe`, its `.blockmap`, `DS1-Multi-Randomizer-X.Y.Z-Portable-x64.exe`, `latest.yml`, `release-manifest-X.Y.Z.json`, `SHA256SUMS.txt`; compare the hashes.
7. [ ] Install the draft's installer on a test machine or scratch folder; generate, check About shows X.Y.Z; try the portable once.
8. [ ] Read the release notes on the draft and confirm the Setup / Portable links and changelog are correct.
9. [ ] Publish the draft with GitHub's **Set as a pre-release** option enabled. Stable users continue to ignore it.
10. [ ] Verify the public release page lists the six expected assets and the release is marked **Pre-release**. Stable releases are published only after explicit authorization.
11. [ ] Later: check updates from an internal N build as described in [AUTO_UPDATE.md](AUTO_UPDATE.md), section 11. A bad release is never edited; publish a fixed version with a higher number.
