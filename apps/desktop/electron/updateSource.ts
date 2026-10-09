import type { UpdateSource } from './updater.js';

/**
 * The ONE place installed clients get updates from (docs/AUTO_UPDATE.md, "The update source"): the public GitHub Releases of this repository, named explicitly so that
 * no packaged client ever depends on repository auto-detection. `electron-builder.yml` (`publish`) carries the same three values and `tools/release/release-verify.mjs`
 * fails when they differ; the release workflow refuses to upload anywhere else.
 *
 * The owner is the GitHub account that publishes the releases. While the value is still `REPLACE_WITH_GITHUB_OWNER` (the "not chosen" placeholder) a build has NO update source,
 * never contacts GitHub, and a public release cannot be made (release-verify.mjs refuses it). Treat this as release infrastructure: installed
 * clients keep following this repository for the life of the product (renaming, transferring or deleting it strands them; see the document above).
 */
export const UPDATE_SOURCE: UpdateSource = {
  provider: 'github',
  owner: 'remohomer',
  repo: 'DS1-Multi-Randomizer',
};
