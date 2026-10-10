import { isUpdateSourceConfigured, type UpdateSource } from './updater.js';

const CANONICAL_REPOSITORY = 'avoydan/DS1-Multi-Randomizer';
const ACCEPTED_SOURCE_OWNERS = new Set(['avoydan', 'remohomer', 'adamwojdan']);
const API_VERSION = '2026-03-10';
const REQUEST_TIMEOUT_MS = 5_000;

type FetchLike = (input: string | URL | Request, init?: RequestInit) => Promise<Response>;

function isCanonicalMetadataUrl(value: string): boolean {
  try {
    const url = new URL(value);
    if (url.protocol !== 'https:' || url.origin !== 'https://api.github.com' || url.search !== '') return false;
    const path = url.pathname.toLowerCase();
    return path === `/repos/${CANONICAL_REPOSITORY}`.toLowerCase() || /^\/repositories\/\d+$/u.test(path);
  } catch {
    return false;
  }
}

function isCanonicalLatestUrl(value: string): boolean {
  try {
    const url = new URL(value);
    return url.protocol === 'https:'
      && url.origin === 'https://api.github.com'
      && url.pathname.toLowerCase() === `/repos/${CANONICAL_REPOSITORY}/releases/latest`.toLowerCase()
      && url.search === '';
  } catch {
    return false;
  }
}

function apiRequestOptions(): RequestInit {
  return {
    headers: { Accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': API_VERSION },
    redirect: 'follow',
    signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
  };
}

/**
 * Confirms that an electron-updater latest-release 404/406 is only the absence of a stable release.
 * Legacy owner paths are accepted only when GitHub's redirect resolves to the canonical public project.
 * Any network, API, identity, visibility, or response-shape uncertainty fails closed.
 */
export async function confirmNoStableReleaseViaGitHubApi(source: UpdateSource, request: FetchLike = fetch): Promise<boolean> {
  if (!isUpdateSourceConfigured(source)
    || source.repo.toLowerCase() !== 'ds1-multi-randomizer'
    || !ACCEPTED_SOURCE_OWNERS.has(source.owner.toLowerCase())) return false;

  const headers = { Accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': API_VERSION };
  try {
    const sourceMetadataUrl = `https://api.github.com/repos/${source.owner}/${source.repo}`;
    const metadataResponse = await request(sourceMetadataUrl, { ...apiRequestOptions(), headers });
    if (metadataResponse.status !== 200 || !isCanonicalMetadataUrl(metadataResponse.url)) return false;

    const metadata: unknown = await metadataResponse.json();
    if (!metadata || typeof metadata !== 'object') return false;
    const repository = metadata as { full_name?: unknown; private?: unknown };
    if (typeof repository.full_name !== 'string'
      || repository.full_name.toLowerCase() !== CANONICAL_REPOSITORY.toLowerCase()
      || repository.private !== false) return false;

    const latestUrl = `https://api.github.com/repos/${CANONICAL_REPOSITORY}/releases/latest`;
    const latestResponse = await request(latestUrl, apiRequestOptions());
    return latestResponse.status === 404 && isCanonicalLatestUrl(latestResponse.url);
  } catch {
    return false;
  }
}
