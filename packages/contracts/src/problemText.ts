/**
 * The validators of the backend write their problems as English sentences. The interface shows them in the language of the person: this maps the sentences
 * that a person can meet (an invalid preset, a Seed Code with settings this version cannot use) to a resource key under `errors.problems`, with the one
 * variable some of them carry. A sentence that is not listed here stays as the validator wrote it (it is shown as technical text under a localized lead).
 */

export interface ProblemKey {
  key: string;
  params?: Record<string, string>;
}

const patterns: Array<[RegExp, (match: RegExpExecArray) => ProblemKey]> = [
  [/^Preset is not valid JSON\.$/u, () => ({ key: 'notJson' })],
  [/^Preset must be a JSON object\.$/u, () => ({ key: 'notObject' })],
  [/^Preset format must be /u, () => ({ key: 'format' })],
  [/^Preset version must be (\d+)/u, (match) => ({ key: 'version', params: { version: match[1] } })],
  [/^(?:preset|config)\.([\w.]+) is not supported(?: in version \d+)?\.$/u, (match) => ({ key: 'unsupportedOption', params: { option: match[1] } })],
  [/^(?:preset|config)\.([\w.]+) is required\.$/u, (match) => ({ key: 'missingOption', params: { option: match[1] } })],
  [/^config\.([\w.]+) must be (?:a boolean|an object|a string)\.$/u, (match) => ({ key: 'wrongType', params: { option: match[1] } })],
  [/^Master seed is required\.$/u, () => ({ key: 'masterSeedRequired' })],
  [/^(Soul items|Starting items|Key placement|Item difficulty|Gwyn nerf|Second Gargoyle|Enemy scaling|Game language) mode is invalid\.$/u, () => ({ key: 'invalidChoice' })],
  [/^Game language is invalid\.$/u, () => ({ key: 'invalidChoice' })],
  [/^Race Mode \+ is only available with Fair item difficulty\.$/u, () => ({ key: 'racePlusFair' })],
  [/^Key placement needs the world items/u, () => ({ key: 'keysNeedWorldItems' })],
  [/^Key placement and protected-item randomization cannot be combined\.$/u, () => ({ key: 'keysProtected' })],
  [/^The Lordvessel and Lord Souls switches need key placement/u, () => ({ key: 'lordNeedsKeys' })],
  [/^Protected-item randomization is blocked/u, () => ({ key: 'protectedBlocked' })],
  [/^Excluding DLC content needs the fog randomizer to be off/u, () => ({ key: 'dlcWithFog' })],
  [/^A random start requires boss or traversable fog gates\.$/u, () => ({ key: 'randomStart' })],
  [/^Enable at least one supported randomization capability\.$/u, () => ({ key: 'nothingEnabled' })],
];

/** The resource key (under `errors.problems`) and variables of a validator sentence, or null when it has no translation. */
export function problemKey(problem: string): ProblemKey | null {
  for (const [pattern, build] of patterns) {
    const match = pattern.exec(problem);
    if (match) return build(match);
  }
  return null;
}

export const PROBLEM_KEYS = [
  'notJson', 'notObject', 'format', 'version', 'unsupportedOption', 'missingOption', 'wrongType', 'masterSeedRequired', 'invalidChoice', 'racePlusFair',
  'keysNeedWorldItems', 'keysProtected', 'lordNeedsKeys', 'protectedBlocked', 'dlcWithFog', 'randomStart', 'nothingEnabled',
] as const;
