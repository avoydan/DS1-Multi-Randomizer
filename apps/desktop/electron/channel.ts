/**
 * The build channel (docs/RELEASE_TRUST.md, "Public and internal builds"). A `public` build is what a player gets: the internal test worlds are not shown. An `internal`
 * build is for testers and developers. The channel only decides what the interface shows; it is not a security boundary (everything an internal build can do is
 * ordinary use of the same backend), and it never reaches a world, a Seed Code or a preset.
 */
export type BuildChannel = 'public' | 'internal';

export type BuildDistribution = 'github' | 'nexus' | 'unavailable';

export interface RuntimeBuildInfo {
  channel: BuildChannel;
  distribution: BuildDistribution;
  nexusPageUrl: string | null;
}

/** Nexus links are a build-time capability. Accept only the canonical DSR mod page form on the official HTTPS host. */
export function parseNexusPageUrl(value: unknown): string | null {
  if (typeof value !== 'string' || value.length === 0) return null;
  try {
    const url = new URL(value);
    if (url.protocol !== 'https:' || url.origin !== 'https://www.nexusmods.com' || url.username || url.password || url.search || url.hash) return null;
    if (!/^\/darksoulsremastered\/mods\/\d+\/?$/u.test(url.pathname)) return null;
    return url.href;
  } catch { return null; }
}

/**
 * Reads the immutable build flavor. A schema-valid legacy record with no distribution is a GitHub build;
 * malformed metadata or an explicit unknown distribution disables update behavior.
 */
export function parseRuntimeBuildInfo(buildInfoText: string | null): RuntimeBuildInfo {
  const safe: RuntimeBuildInfo = { channel: 'public', distribution: 'unavailable', nexusPageUrl: null };
  if (buildInfoText === null) return safe;
  try {
    const parsed = JSON.parse(buildInfoText) as { schema?: unknown; channel?: unknown; distribution?: unknown; nexusPageUrl?: unknown };
    if (!parsed || typeof parsed !== 'object' || parsed.schema !== 'ds1tr-build-info-1') return safe;
    if (parsed.channel !== undefined && parsed.channel !== 'public' && parsed.channel !== 'internal') return safe;
    const channel = parsed.channel === 'internal' ? 'internal' : 'public';
    if (parsed.distribution === undefined) return { channel, distribution: 'github', nexusPageUrl: null };
    if (parsed.distribution === 'github') return { channel, distribution: 'github', nexusPageUrl: null };
    if (parsed.distribution === 'nexus') return { channel, distribution: 'nexus', nexusPageUrl: parseNexusPageUrl(parsed.nexusPageUrl) };
    return { ...safe, channel };
  } catch { return safe; }
}

/** Legacy channel-only parser. Runtime startup also uses parseRuntimeBuildInfo, which separately fails updater behavior closed on invalid metadata. */
export function parseBuildChannel(buildInfoText: string | null): BuildChannel {
  if (buildInfoText === null) return 'public';
  try {
    const parsed = JSON.parse(buildInfoText) as { channel?: unknown };
    return parsed.channel === 'internal' ? 'internal' : 'public';
  } catch { return 'public'; }
}

/** The optional shortcut that shows the internal tools in a public build for the current session (Ctrl+Shift+Alt+F12). */
export function isDevToolsShortcut(input: { type: string; key: string; control: boolean; shift: boolean; alt: boolean; meta?: boolean }): boolean {
  return input.type === 'keyDown' && input.key === 'F12' && input.control && input.shift && input.alt && !input.meta;
}
