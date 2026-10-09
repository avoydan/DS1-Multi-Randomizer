// Prints the GitHub repository installed clients follow for updates, as "owner/repo" (docs/AUTO_UPDATE.md). The release workflow compares it with the repository it is
// about to upload to and refuses to continue when they differ; it exits with 1 while the owner is still the placeholder.
//
//   node tools/release/update-source.mjs
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { repositoryRoot } from './release-lib.mjs';
import { isConfiguredSource, parseUpdateSourceTs } from './update-lib.mjs';

const source = parseUpdateSourceTs(await readFile(path.join(repositoryRoot, 'apps', 'desktop', 'electron', 'updateSource.ts'), 'utf8'));
if (!isConfiguredSource(source)) {
  console.error(`The update source is not set (owner "${source.owner}"): write the owner and the repository of the public releases into apps/desktop/electron/updateSource.ts and apps/desktop/electron-builder.yml first.`);
  process.exit(1);
}
console.log(`${source.owner}/${source.repo}`);
