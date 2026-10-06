/**
 * Which Dark Souls Remastered installation the app uses at start. A copy the person chose by hand is machine-local state and wins
 * over automatic detection as long as it still passes the installation check; Steam detection is only the fallback. It is never part
 * of a Seed Code, a preset or a generation configuration.
 */
export interface StartupInstallationInput {
  /** The folder the person chose last time ('' when none). */
  saved: string;
  /** Whether that folder still passes the installation check (false when it is gone or no longer valid). */
  savedValid: boolean;
  /** Valid installations found automatically (Steam libraries). */
  detected: readonly string[];
}

export type StartupInstallationDecision =
  | { kind: 'saved'; path: string }
  | { kind: 'detected'; path: string }
  | { kind: 'choose'; options: readonly string[] }
  | { kind: 'missing'; savedProblem: boolean };

export function decideStartupInstallation(input: StartupInstallationInput): StartupInstallationDecision {
  const saved = input.saved.trim();
  if (saved !== '' && input.savedValid) return { kind: 'saved', path: saved };
  const savedProblem = saved !== '';
  if (input.detected.length === 1) return { kind: 'detected', path: input.detected[0] };
  if (input.detected.length > 1) return { kind: 'choose', options: input.detected };
  return { kind: 'missing', savedProblem };
}
