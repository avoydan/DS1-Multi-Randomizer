/**
 * The build channel (docs/RELEASE_TRUST.md, "Public and internal builds"). A `public` build is what a player gets: the internal test worlds are not shown. An `internal`
 * build is for testers and developers. The channel only decides what the interface shows; it is not a security boundary (everything an internal build can do is
 * ordinary use of the same backend), and it never reaches a world, a Seed Code or a preset.
 */
export type BuildChannel = 'public' | 'internal';

/** `build-info.json` is written by the packaging script. A packaged app that cannot read it is treated as public: the safe side is to show less. */
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
