import { readFileSync } from 'node:fs';
import path from 'node:path';

/**
 * Internal QA mode (docs/INTERNAL_QA.md). The solver route is a complete route to Gwyn: a spoiler of the world and a debugging aid for us, not a feature of the
 * product. It exists only when BOTH hold:
 *   - the process was started with `DS1TR_INTERNAL_QA=1` (exactly `1`), and
 *   - the build is not a public one: a packaged build whose `build-info.json` says `channel: "public"` (or is missing, unreadable or malformed) never enables it,
 *     whatever the environment says. A backend without a runtime root (development from source, the test suites) has no build to ask and follows the variable.
 *
 * The decision is made by the backend alone. The desktop shell and the renderer have no setting for it, and FogHost is told by the backend (never inherited
 * from the player's environment). Nothing this mode switches on can change a generated world: it only decides whether diagnostics that describe the route are
 * written and served.
 */

export const INTERNAL_QA_VARIABLE = 'DS1TR_INTERNAL_QA';

/** `internal` only for a build-info that says so; every doubt is `public`. */
export function buildChannelOf(runtimeRoot: string | null | undefined): 'internal' | 'public' | 'unpackaged' {
  if (!runtimeRoot) return 'unpackaged';
  try {
    const parsed = JSON.parse(readFileSync(path.join(runtimeRoot, 'build-info.json'), 'utf8')) as { schema?: unknown; channel?: unknown };
    return parsed.schema === 'ds1tr-build-info-1' && parsed.channel === 'internal' ? 'internal' : 'public';
  } catch { return 'public'; }
}

export function internalQaEnabled(env: NodeJS.ProcessEnv, runtimeRoot: string | null | undefined): boolean {
  if (env[INTERNAL_QA_VARIABLE] !== '1') return false;
  return buildChannelOf(runtimeRoot) !== 'public';
}

/** The environment FogHost is started with: the player's own value of the variable is replaced by the backend's decision. */
export function hostEnvironment(env: NodeJS.ProcessEnv, enabled: boolean): NodeJS.ProcessEnv {
  const result: NodeJS.ProcessEnv = { ...env };
  if (enabled) result[INTERNAL_QA_VARIABLE] = '1';
  else delete result[INTERNAL_QA_VARIABLE];
  return result;
}
