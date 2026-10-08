import os from 'node:os';
import path from 'node:path';

/**
 * Redaction for everything that leaves the application as a support document (docs/SUPPORT_BUNDLE.md). Two layers:
 *
 *  1. patterns: secret-looking key/value pairs, tokens in well-known formats, credentials in URLs, personal and machine identifiers, signing files;
 *  2. known values: the Windows user name, the home / user-data / temp / game / runtime folders (in every spelling a log may use: backslash,
 *     forward slash, JSON-escaped double backslash, file:// URL), the host name and the exact values of secrets in the environment.
 *
 * Seed Codes and SHA-256 hashes are protected: a world must stay reproducible from the bundle. `findLeaks` is the independent check the bundle runs
 * over its finished content; a bundle that still contains a known personal value is refused instead of sent.
 */

export interface RedactionDirectory { path: string; label: string }

export interface RedactionContext {
  userName?: string;
  hostName?: string;
  directories: RedactionDirectory[];
  /** Exact secret values (the localhost session token, secret-looking environment values). Redacted wherever they appear. */
  secrets: string[];
}

const SECRET_ENV_NAME = /(?:key|token|secret|password|passwd|pwd|credential|cookie|auth|csc_link|thumbprint|private)/iu;

const escapeRegExp = (value: string): string => value.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&');

/** The context of the running process; `directories` adds the folders the caller knows (game, user data, runtime). */
export function redactionContext(extra: { directories?: RedactionDirectory[]; secrets?: string[]; env?: NodeJS.ProcessEnv } = {}): RedactionContext {
  const env = extra.env ?? process.env;
  let userName = env.USERNAME ?? env.USER;
  try { userName = os.userInfo().username || userName; } catch { /* keep the environment's */ }
  const directories: RedactionDirectory[] = [...(extra.directories ?? [])];
  const known: Array<[string | undefined, string]> = [[os.tmpdir(), '<TEMP>'], [os.homedir(), 'C:\\Users\\<USER>']];
  for (const [directory, label] of known) if (directory) directories.push({ path: directory, label });
  const secrets = [...(extra.secrets ?? [])];
  for (const [name, value] of Object.entries(env)) if (value && value.length >= 6 && SECRET_ENV_NAME.test(name) && !/^(path|pathext)$/iu.test(name)) secrets.push(value);
  return { userName, hostName: env.COMPUTERNAME ?? os.hostname(), directories, secrets: [...new Set(secrets)] };
}

/** Every spelling of a folder a log or a JSON file may contain. */
function spellings(directory: string): string[] {
  const trimmed = directory.replace(/[\\/]+$/u, '');
  if (trimmed.length < 4) return [];
  const forward = trimmed.replace(/\\/gu, '/');
  const backward = trimmed.replace(/\//gu, '\\');
  const variants = new Set([
    backward, forward, backward.replace(/\\/gu, '\\\\'), `file:///${forward}`, `file:///${encodeURI(forward)}`, encodeURI(forward), encodeURIComponent(forward),
  ]);
  return [...variants].filter((variant) => variant.length >= 4);
}

const KEY = '[A-Za-z0-9_.-]{0,40}(?:api[_-]?key|apikey|access[_-]?key|secret|token|authorization|password|passwd|pwd|passphrase|cookie|session[_-]?id|sessionid|credential|private[_-]?key|csc_link|csc_key_password|thumbprint|machine[_-]?guid|hwid|serial[_-]?number)[A-Za-z0-9_.-]{0,40}';
// "key": "any text"   /   key: 'any text'   (the whole quoted value)
const QUOTED_PAIR = new RegExp(`(["']?)(${KEY})\\1(\\s*[:=]\\s*)(["'])((?:\\\\.|(?!\\4).)*)\\4`, 'giu');
// key=value   /   key: value   (a bare value up to a separator)
const BARE_PAIR = new RegExp(`(["']?)(${KEY})\\1(\\s*[:=]\\s*)(?!<REDACTED>|(?:Bearer|Basic|Token)\\s)([^\\s"',;&}\\])<>]+)`, 'giu');
const COOKIE_LINE = /\b((?:set-)?cookie)(\s*:\s*)[^\r\n]+/giu;
const BEARER = /\b(Bearer|Basic|Token)(\s+)[A-Za-z0-9._~+/=-]{6,}/gu;
const TOKEN_FORMATS: RegExp[] = [
  /\bsk-(?:ant-)?[A-Za-z0-9_-]{16,}/gu, /\bgh[pousr]_[A-Za-z0-9]{20,}/gu, /\bgithub_pat_[A-Za-z0-9_]{20,}/gu, /\bxox[abprs]-[A-Za-z0-9-]{10,}/gu,
  /\bAKIA[0-9A-Z]{16}\b/gu, /\bAIza[0-9A-Za-z_-]{30,}/gu, /\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]*/gu, /-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----/gu,
];
const URL_CREDENTIALS = /(\b[a-z][a-z0-9+.-]*:\/\/)[^\s/@:]+:[^\s/@]+@/giu;
const URL_SECRET_QUERY = /([?&](?:token|access_token|api_key|apikey|key|sig|signature|password)=)[^&\s"']+/giu;
const SIGNING_FILE = /(?:[A-Za-z]:)?[\\/][^\s"'<>|]*\.(?:pfx|p12|pem|snk|cer|key)\b/giu;
const EMAIL = /\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b/gu;
const MAC = /\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b/gu;
const IPV4 = /(?<![\d.])(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?![\d.])/gu;
// An IPv6 address has either eight groups or a `::`; a time such as 18:30:00 has neither and is left alone.
const IPV6 = /(?<![\w:])(?:(?:[0-9A-Fa-f]{1,4}:){7}[0-9A-Fa-f]{1,4}|(?:[0-9A-Fa-f]{1,4}:){1,6}:(?:[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){0,5})?|::(?:[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){0,6})?)(?![\w:])/gu;
const BRACED_GUID = /\{[0-9A-Fa-f]{8}-(?:[0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12}\}/gu;
const USER_PROFILE = /([A-Za-z]:)(\\{1,2}|\/)(Users|Documents and Settings)\2((?!<)[^\\/\r\n"'<>:|?*]+)/giu;
const POSIX_HOME = /(\/(?:Users|home))\/((?!<)[^/\s"'<>:|?*]+)/gu;
const SEED_CODE = /DS1TR\d+(?:-[A-Z0-9]+)+/gu;
const HASH = /\b[0-9a-f]{40,64}\b/gu;

export interface Redactor {
  (text: string): string;
  /** Number of replacements made so far (reported in the bundle, never the redacted values). */
  readonly count: () => number;
}

export function createRedactor(context: RedactionContext): Redactor {
  let count = 0;
  const swap = (text: string, pattern: RegExp, replacement: string | ((...groups: string[]) => string)): string =>
    text.replace(pattern, (...args: unknown[]) => {
      count += 1;
      return typeof replacement === 'function' ? replacement(...(args as string[])) : replacement;
    });
  const directories = [...context.directories]
    .flatMap((entry) => spellings(entry.path).map((spelling) => ({ spelling, label: entry.label })))
    .sort((a, b) => b.spelling.length - a.spelling.length);
  const secrets = [...context.secrets].filter((secret) => secret.length >= 6).sort((a, b) => b.length - a.length);
  const wordLike = (value: string | undefined): string | null => (value && value.length >= 3 ? value : null);
  const userName = wordLike(context.userName);
  const hostName = wordLike(context.hostName);

  const redact = ((input: string): string => {
    // Seed Codes and hashes must survive untouched: set them aside, restore them at the end.
    const kept: string[] = [];
    const keep = (match: string): string => { kept.push(match); return `\u0001${kept.length - 1}\u0001`; };
    let text = input.replace(SEED_CODE, keep).replace(HASH, keep);

    for (const secret of secrets) text = swap(text, new RegExp(escapeRegExp(secret), 'giu'), '<REDACTED>');
    text = swap(text, /-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----/gu, '<REDACTED>');
    text = swap(text, URL_CREDENTIALS, (_all, scheme) => `${scheme}<REDACTED>@`);
    text = swap(text, URL_SECRET_QUERY, (_all, prefix) => `${prefix}<REDACTED>`);
    // Before the key/value pairs: `Authorization: Bearer <token>` must lose the token, not the word Bearer.
    text = swap(text, BEARER, (_all, scheme, space) => `${scheme}${space}<REDACTED>`);
    text = swap(text, COOKIE_LINE, (_all, name, separator) => `${name}${separator}<REDACTED>`);
    text = swap(text, QUOTED_PAIR, (_all, quote, key, separator, valueQuote) => `${quote}${key}${quote}${separator}${valueQuote}<REDACTED>${valueQuote}`);
    text = swap(text, BARE_PAIR, (_all, quote, key, separator) => `${quote}${key}${quote}${separator}<REDACTED>`);
    for (const format of TOKEN_FORMATS) text = swap(text, format, '<REDACTED>');
    text = swap(text, SIGNING_FILE, '<SIGNING_FILE>');
    for (const { spelling, label } of directories) {
      // Keep the separator style of the surrounding text: a JSON file (double backslashes) stays valid JSON.
      const doubled = spelling.includes('\\\\');
      text = swap(text, new RegExp(escapeRegExp(spelling), 'giu'), doubled ? label.replace(/\\/gu, '\\\\') : label);
    }
    text = swap(text, USER_PROFILE, (_all, drive, separator, folder) => `${drive}${separator}${folder}${separator}<USER>`);
    text = swap(text, POSIX_HOME, (_all, prefix) => `${prefix}/<USER>`);
    if (userName) text = swap(text, new RegExp(`(?<![A-Za-z0-9])${escapeRegExp(userName)}(?![A-Za-z0-9])`, 'giu'), '<USER>');
    if (hostName) text = swap(text, new RegExp(`(?<![A-Za-z0-9])${escapeRegExp(hostName)}(?![A-Za-z0-9])`, 'giu'), '<HOST>');
    text = swap(text, EMAIL, '<EMAIL>');
    text = swap(text, MAC, '<MAC>');
    text = swap(text, BRACED_GUID, '<ID>');
    text = swap(text, IPV4, '<IP>');
    text = swap(text, IPV6, '<IP>');
    return text.replace(/\u0001(\d+)\u0001/gu, (_all, index: string) => kept[Number(index)]);
  }) as Redactor;
  Object.defineProperty(redact, 'count', { value: () => count });
  return redact;
}

/** Redacts every string of a JSON value (keys included) and returns the new value; version-like strings are exempt from the address patterns. */
export function redactJson<T>(value: T, redact: Redactor): T {
  const visit = (node: unknown, key?: string): unknown => {
    if (typeof node === 'string') {
      if (key && /version/iu.test(key) && /^\d+(?:\.\d+){1,3}(?:[-+][\w.]+)?$/u.test(node)) return node;
      return redact(node);
    }
    if (Array.isArray(node)) return node.map((item) => visit(item, key));
    if (node !== null && typeof node === 'object') return Object.fromEntries(Object.entries(node as Record<string, unknown>).map(([name, child]) => [redact(name), visit(child, name)]));
    return node;
  };
  return visit(value) as T;
}

/** The known personal / secret values that are still in a text (empty = clean). A check over the finished content, independent of the redaction itself. */
export function findLeaks(text: string, context: RedactionContext): string[] {
  const lower = text.toLowerCase();
  const leaks: string[] = [];
  const check = (label: string, value: string | undefined | null): void => {
    if (value && value.length >= 3 && lower.includes(value.toLowerCase())) leaks.push(label);
  };
  const word = (label: string, value: string | undefined): void => {
    if (value && value.length >= 3 && new RegExp(`(?<![A-Za-z0-9])${escapeRegExp(value)}(?![A-Za-z0-9])`, 'iu').test(text)) leaks.push(label);
  };
  word('user name', context.userName);
  word('host name', context.hostName);
  context.directories.forEach((entry) => spellings(entry.path).forEach((spelling) => check(`folder ${entry.label}`, spelling)));
  context.secrets.forEach((secret, index) => check(`secret #${index + 1}`, secret.length >= 6 ? secret : null));
  return [...new Set(leaks)];
}

/** `path.basename` for any separator style, for places that need a file name only. */
export const fileNameOnly = (value: string): string => path.win32.basename(value.replace(/\//gu, '\\'));
