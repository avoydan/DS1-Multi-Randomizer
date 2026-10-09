import type { UpdateSource } from './updater.js';

/**
 * The ONE place installed clients get updates from (docs/AUTO_UPDATE.md, "The update source"): the public GitHub Releases of this repository, named explicitly so that
 * no packaged client ever depends on repository auto-detection. `electron-builder.yml` (`publish`) carries the same three values and `tools/release/release-verify.mjs`
 * fails when they differ; the release workflow refuses to upload anywhere else.
 *
 * The current public release destination is avoydan/DS1-Multi-Randomizer. Older installed builds may still contain the previous owner, so a repository transfer
 * and an application update-source change must be coordinated; see docs/AUTO_UPDATE.md.
 */
export const UPDATE_SOURCE: UpdateSource = {
  provider: 'github',
  owner: 'avoydan',
  repo: 'DS1-Multi-Randomizer',
};
