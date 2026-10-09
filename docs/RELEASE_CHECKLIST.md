# Release checklist

The short version. The reasons are in [AUTO_UPDATE.md](AUTO_UPDATE.md) (updates, workflow) and [RELEASE_TRUST.md](RELEASE_TRUST.md) (signing, trust).
Nothing below is automatic past the draft: **publishing is always a human click**.

## Once, before the first release

- [ ] The update source is set: owner and repository in `apps/desktop/electron/updateSource.ts` **and** `apps/desktop/electron-builder.yml` (`node tools/release/update-source.mjs` prints `owner/repo`).
- [ ] The repository is pushed, Actions are enabled, workflow permissions are "read" (the workflow asks for `contents: write` itself). Two-factor authentication is on.
- [ ] Separate release repository only: secret `RELEASE_REPO_TOKEN` (fine-grained, that repository only, Contents read/write) and variable `RELEASE_REPOSITORY`.
- [ ] The N to N+1 QA of [AUTO_UPDATE.md](AUTO_UPDATE.md) section 11 has passed once against a test repository.

## Every release

1. [ ] Bump the versions (desktop, and backend/contracts when they changed), update the notes in `docs/IMPLEMENTATION_STATUS.md`; notes file `docs/release-notes/vX.Y.Z.md` (copy the previous one: it opens with the Recommended = Setup / Portable names and one line on each). Commit.
2. [ ] Working tree clean; `npm test -w @ds1tr/backend`, `npm test -w @ds1tr/desktop`, `npm run typecheck` pass locally.
3. [ ] Golden fixtures on your computer: `node apps/backend/scripts/goldenCompatibility.mjs verify` is all PASS (a clean game copy; never the live Steam folder).
4. [ ] Push the commit.
5. [ ] GitHub > Actions > **Release (draft)** > Run workflow: `version` = `X.Y.Z` (must equal `apps/desktop/package.json`), `golden_attestation` = first 7 characters of the commit from step 3.
6. [ ] The run is green. Download the draft's files: `DS1-Multi-Randomizer-X.Y.Z-Setup-x64.exe`, its `.blockmap`, `DS1-Multi-Randomizer-X.Y.Z-Portable-x64.exe`, `latest.yml`, `release-manifest.json`, `SHA256SUMS.txt`; compare the hashes.
7. [ ] Install the draft's installer on a test machine or scratch folder; generate, check About shows X.Y.Z; try the portable once.
8. [ ] Read the release notes on the draft; the release is **not** marked "pre-release".
9. [ ] Press **Publish release** (this creates the tag `vX.Y.Z` and makes it "Latest").
10. [ ] Later: on an installed older copy, Check for updates shows X.Y.Z. A bad release is never edited: delete it and publish a higher version.
