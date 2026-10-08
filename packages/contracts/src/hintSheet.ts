import type { GenerationSettings } from './generation.js';

/**
 * Hint sheet (docs/HINT_SHEET.md): a read-only report of where the randomizer put things in one world. It is built from the placement data the
 * generator already produced (`HintDocument`), never from the game files, and it never influences generation. Everything here is pure: the spoiler
 * level filter, the sections, the search, the world summary, the three exports (HTML, TXT, JSON) and the safe file names. Texts shown to a person
 * come from the application's translation resources through an injected `translate`; machine-readable output (JSON) carries stable ids only.
 */

export const HINT_DATA_SCHEMA_VERSION = 1;
export const HINT_EXPORT_SCHEMA_VERSION = 1;

export type HintLocationAcquisition = 'world pickup' | 'NPC gift' | 'boss reward' | 'enemy drop' | 'shop';
export type HintRecordImportance = 'key' | 'lord' | 'race item' | 'progression' | 'boss soul' | 'boss soul weapon' | 'normal';

export interface HintLocation {
  /** Human-readable area or owner ("Undead Burg / Undead Parish", "Boss reward: Gaping Dragon", "Shop inventory: Andre"). Game content: English. */
  area: string;
  /** The map, or the parameter table for a gift, shop or drop. */
  map: string;
  acquisition: HintLocationAcquisition;
  /** A world pickup: the area. A boss reward, gift, drop or shop slot: the engine names the slot by the item that lies there in the clean game (it does not record the boss or the NPC). */
  owner: string;
  /** Diagnostics only: the item lot (or shop) row. Never shown and never exported. */
  lot: number | null;
}

/** One placement as the generator recorded it (the data layer; see `apps/backend/src/core/hintData.ts`). */
export interface HintRecord {
  itemId: number;
  itemName: string;
  itemKind: 'weapon' | 'armor' | 'ring' | 'goods' | 'spell';
  importance: HintRecordImportance;
  original: HintLocation;
  randomized: HintLocation;
  keyName: string | null;
  transposedFrom: string | null;
  /** The item that lay at the destination in the clean game (null when the generator did not record it). */
  placedAt?: string | null;
}

/** The persisted hint data of one world (`hints.json` next to the run, outside the package). */
export interface HintDocument {
  format: 'ds1tr-hint-data';
  schemaVersion: number;
  seedCode: string;
  generationProfile: string;
  /** When the world was generated (ISO 8601). */
  createdAt: string;
  appVersion: string;
  settings: GenerationSettings;
  records: HintRecord[];
  /** Placements that lacked what a hint needs (counted, never half-described). */
  unhintable: number;
}

export type HintLevel = 'low' | 'progression' | 'full';
export const HINT_LEVELS: readonly HintLevel[] = ['low', 'progression', 'full'];
export const DEFAULT_HINT_LEVEL: HintLevel = 'progression';

/** Stable ids (never translated) for the machine-readable forms. */
export type HintAcquisition = 'world-pickup' | 'npc-gift' | 'boss-reward' | 'enemy-drop' | 'shop';
export type HintImportance = 'key' | 'lord' | 'race-item' | 'progression' | 'boss-soul' | 'boss-soul-weapon' | 'normal';
export type HintSectionId = 'keys' | 'lord' | 'progression' | 'bossSouls' | 'other';

const ACQUISITION: Record<HintLocationAcquisition, HintAcquisition> = {
  'world pickup': 'world-pickup', 'NPC gift': 'npc-gift', 'boss reward': 'boss-reward', 'enemy drop': 'enemy-drop', shop: 'shop',
};
const IMPORTANCE: Record<HintRecordImportance, HintImportance> = {
  key: 'key', lord: 'lord', 'race item': 'race-item', progression: 'progression', 'boss soul': 'boss-soul', 'boss soul weapon': 'boss-soul-weapon', normal: 'normal',
};

/** What one level may say about a place; fields a level does not allow are absent, not blanked. */
export interface HintPlace {
  acquisition: HintAcquisition;
  /** A world pickup: the map-level area ("Depths"). */
  area?: string;
  /** Every other acquisition: the slot, named by the item that lies there in the clean game. */
  owner?: string;
  /** The item that lies there in the clean game, so the pickup can be recognized. */
  formerItem?: string;
  /** Full only: the map id of a world pickup. */
  map?: string;
}

export interface HintEntry {
  itemName: string;
  itemKind: HintRecord['itemKind'];
  importance: HintImportance;
  /** Whether the item is needed to progress (keys, the Lordvessel, Lord Souls, race items, fog-tracked progression items). */
  progression: boolean;
  destination: HintPlace;
  /** Full only: where the item lay in the clean game. */
  source?: HintPlace;
  /** Full only: the key name used by the key planner, and the soul a transposed weapon was made from. */
  keyName?: string;
  transposedFrom?: string;
}

const PROGRESSION_IMPORTANCE = new Set<HintRecordImportance>(['key', 'lord', 'race item', 'progression']);
const LEVEL_IMPORTANCE: Record<HintLevel, ReadonlySet<HintRecordImportance>> = {
  // Important progression items: keys, the Lordvessel and Lord Souls, the race items.
  low: new Set(['key', 'lord', 'race item']),
  // Every progression item, including the fog-tracked and other progression-critical ones.
  progression: PROGRESSION_IMPORTANCE,
  // Every hintable placement.
  full: new Set(['key', 'lord', 'race item', 'progression', 'boss soul', 'boss soul weapon', 'normal']),
};
const IMPORTANCE_RANK: Record<HintRecordImportance, number> = { key: 0, 'race item': 0, lord: 1, progression: 2, 'boss soul': 3, 'boss soul weapon': 3, normal: 4 };

function placeOf(location: HintLocation, level: HintLevel, formerItem: string | null | undefined): HintPlace {
  const acquisition = ACQUISITION[location.acquisition];
  const world = location.acquisition === 'world pickup';
  if (level === 'low') return world ? { acquisition, area: location.area } : { acquisition };
  const place: HintPlace = world ? { acquisition, area: location.area } : { acquisition, owner: location.owner };
  if (formerItem && (world || level === 'full')) place.formerItem = formerItem;
  if (level === 'full' && world) place.map = location.map;
  return place;
}

/** The records a level may show, as the entries it may show, in a stable order. Switching levels only filters: nothing is generated again. */
export function filterHints(records: readonly HintRecord[], level: HintLevel): HintEntry[] {
  const allowed = LEVEL_IMPORTANCE[level];
  return records
    .filter((record) => allowed.has(record.importance))
    .map((record, index) => ({ record, index }))
    .sort((a, b) => IMPORTANCE_RANK[a.record.importance] - IMPORTANCE_RANK[b.record.importance]
      || a.record.itemName.localeCompare(b.record.itemName, 'en') || a.index - b.index)
    .map(({ record }): HintEntry => {
      const entry: HintEntry = {
        itemName: record.itemName, itemKind: record.itemKind, importance: IMPORTANCE[record.importance], progression: PROGRESSION_IMPORTANCE.has(record.importance),
        destination: placeOf(record.randomized, level, record.placedAt),
      };
      if (level === 'full') {
        entry.source = placeOf(record.original, 'full', null);
        if (record.keyName) entry.keyName = record.keyName;
        if (record.transposedFrom) entry.transposedFrom = record.transposedFrom;
      }
      return entry;
    });
}

export function sectionOf(importance: HintImportance): HintSectionId {
  switch (importance) {
    case 'key': case 'race-item': return 'keys';
    case 'lord': return 'lord';
    case 'progression': return 'progression';
    case 'boss-soul': case 'boss-soul-weapon': return 'bossSouls';
    default: return 'other';
  }
}

const SECTION_ORDER: readonly HintSectionId[] = ['keys', 'lord', 'progression', 'bossSouls', 'other'];

export function hintSections(entries: readonly HintEntry[]): Array<{ id: HintSectionId; entries: HintEntry[] }> {
  return SECTION_ORDER
    .map((id) => ({ id, entries: entries.filter((entry) => sectionOf(entry.importance) === id) }))
    .filter((section) => section.entries.length > 0);
}

/** Case-insensitive search over the visible names of an entry (the viewer's filter; an empty query keeps everything). */
export function searchHints(entries: readonly HintEntry[], query: string): HintEntry[] {
  const needle = query.trim().toLowerCase();
  if (needle === '') return [...entries];
  return entries.filter((entry) => [entry.itemName, entry.destination.area, entry.destination.owner, entry.destination.formerItem, entry.source?.area, entry.source?.owner, entry.transposedFrom]
    .some((text) => typeof text === 'string' && text.toLowerCase().includes(needle)));
}

// --- the world in a few lines -------------------------------------------------------------------------------------------------------------------------------

export interface WorldSummaryLine {
  id: 'items' | 'enemies' | 'bosses' | 'fog' | 'keyPlacement' | 'itemDifficulty' | 'soulItems' | 'laundromat' | 'enemyAlgorithm' | 'gameLanguage';
  /** A switch, or the id of an option (translated through `optionPath`). */
  value: boolean | string;
  /** The settings resource whose `options.<value>` names the value (`items.keyPlacement`), for enumerated settings. */
  optionPath?: string;
}

/** The major settings of a world, structured: the interface names them in its own language. */
export function summarizeWorld(settings: GenerationSettings): WorldSummaryLine[] {
  const lines: WorldSummaryLine[] = [
    { id: 'items', value: settings.items.enabled },
    { id: 'enemies', value: settings.enemies.enabled && settings.enemies.randomizeRegularEnemies },
    { id: 'bosses', value: settings.enemies.enabled && settings.enemies.randomizeBosses },
    { id: 'fog', value: settings.fog.enabled },
  ];
  if (settings.items.enabled) {
    lines.push({ id: 'keyPlacement', value: settings.items.keyPlacement ?? 'vanilla', optionPath: 'items.keyPlacement' });
    lines.push({ id: 'itemDifficulty', value: settings.items.itemDifficulty ?? 'fair', optionPath: 'items.difficulty' });
    lines.push({ id: 'soulItems', value: settings.items.soulItems, optionPath: 'items.soulItems' });
    lines.push({ id: 'laundromat', value: settings.items.laundromat ?? false });
  }
  if (settings.enemies.enabled) lines.push({ id: 'enemyAlgorithm', value: settings.enemies.placement.algorithm, optionPath: 'enemies.algorithm' });
  if (settings.fog.enabled) lines.push({ id: 'gameLanguage', value: settings.fog.language, optionPath: 'fog.gameLanguage' });
  return lines;
}

// --- exports -----------------------------------------------------------------------------------------------------------------------------------------------

export type HintExportLanguage = 'en' | 'pl';
export type HintExportFormat = 'html' | 'txt' | 'json';
export type Translate = (key: string, params?: Record<string, string | number>) => string;

export interface HintExportInput {
  document: HintDocument;
  level: HintLevel;
  language: HintExportLanguage;
  translate: Translate;
  /** Filter text of the viewer; the exports always contain the whole level. */
}

export function escapeHtml(value: unknown): string {
  return String(value).replace(/[&<>"'`]/gu, (character) => (({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;', '`': '&#96;' } as Record<string, string>)[character]));
}

/** `DS1TR1-06NJDCP6-...` -> `06NJDCP6`: the start of the code is enough to recognize a world, and only letters and digits reach a file name. */
export function shortSeed(seedCode: string): string {
  const groups = seedCode.split('-');
  const candidate = (groups[1] ?? groups[0] ?? '').replace(/[^A-Za-z0-9]/gu, '').toUpperCase().slice(0, 12);
  return candidate === '' ? 'WORLD' : candidate;
}

/** `DS1TR-<short-seed>-hints-<level>.<extension>`. */
export function hintFileName(seedCode: string, level: HintLevel, format: HintExportFormat): string {
  return `DS1TR-${shortSeed(seedCode)}-hints-${level}.${format}`;
}

const locale = (language: HintExportLanguage): string => (language === 'pl' ? 'pl-PL' : 'en-GB');
function dateText(iso: string, language: HintExportLanguage): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return new Intl.DateTimeFormat(locale(language), { day: 'numeric', month: 'long', year: 'numeric', hour: '2-digit', minute: '2-digit' }).format(date);
}

/** The words for where an entry lies, at the precision its level allows. Game names (areas, bosses, NPCs, items) stay as the game writes them. */
export function describePlace(place: HintPlace, translate: Translate, exact: boolean): string {
  const owner = place.owner ?? '';
  switch (place.acquisition) {
    case 'world-pickup': {
      if (!exact || !place.formerItem) return translate('hints.where.world.broad', { area: place.area ?? '' });
      return translate('hints.where.world.exact', { area: place.area ?? '', former: place.formerItem });
    }
    case 'boss-reward': return translate(exact && owner ? 'hints.where.boss.exact' : 'hints.where.boss.broad', { owner });
    case 'npc-gift': return translate(exact && owner ? 'hints.where.gift.exact' : 'hints.where.gift.broad', { owner });
    case 'enemy-drop': return translate(exact && owner ? 'hints.where.enemy.exact' : 'hints.where.enemy.broad', { owner });
    case 'shop': return translate(exact && owner ? 'hints.where.shop.exact' : 'hints.where.shop.broad', { owner });
    default: return '';
  }
}

export function describeEntry(entry: HintEntry, level: HintLevel, translate: Translate): { item: string; where: string; before: string | null; note: string | null } {
  const exact = level !== 'low';
  const where = describePlace(entry.destination, translate, exact);
  const before = entry.source ? describePlace(entry.source, translate, true) : null;
  const note = entry.transposedFrom ? translate('hints.note.transposed', { soul: entry.transposedFrom }) : null;
  return { item: entry.itemName, where, before, note };
}

function summaryText(line: WorldSummaryLine, translate: Translate): { label: string; value: string } {
  const label = translate(`hints.summary.${line.id}`);
  if (typeof line.value === 'boolean') return { label, value: translate(line.value ? 'hints.on' : 'hints.off') };
  return { label, value: line.optionPath ? translate(`settings.${line.optionPath}.options.${line.value}`) : line.value };
}

/** The JSON form: a schema version, the Seed Code, the generation profile, the level, the language, the settings and the placements; stable ids only. */
export function buildHintJson(input: HintExportInput): string {
  const { document, level, language } = input;
  const body = {
    format: 'ds1tr-hint-sheet',
    schemaVersion: HINT_EXPORT_SCHEMA_VERSION,
    seedCode: document.seedCode,
    generationProfile: document.generationProfile,
    spoilerLevel: level,
    language,
    createdAt: document.createdAt,
    appVersion: document.appVersion,
    settings: document.settings,
    placements: filterHints(document.records, level),
  };
  return `${JSON.stringify(body, null, 2)}\n`;
}

/** The plain-text form: readable top to bottom, no markup. */
export function buildHintText(input: HintExportInput): string {
  const { document, level, language, translate: t } = input;
  const entries = filterHints(document.records, level);
  const lines: string[] = [];
  lines.push(t('hints.export.title'), '='.repeat(t('hints.export.title').length), '');
  lines.push(`${t('hints.export.seedCode')}: ${document.seedCode}`);
  lines.push(`${t('hints.export.profile')}: ${document.generationProfile}`);
  lines.push(`${t('hints.export.created')}: ${dateText(document.createdAt, language)}`);
  lines.push(`${t('hints.export.level')}: ${t(`hints.levels.${level}.name`)}`);
  lines.push(`${t('hints.export.language')}: ${t(`hints.languages.${language}`)}`, '');
  lines.push(t('hints.export.settings'), '-'.repeat(t('hints.export.settings').length));
  for (const line of summarizeWorld(document.settings)) { const text = summaryText(line, t); lines.push(`${text.label}: ${text.value}`); }
  lines.push('');
  if (entries.length === 0) lines.push(t('hints.empty'), '');
  for (const section of hintSections(entries)) {
    const title = t(`hints.sections.${section.id}`);
    lines.push(title, '-'.repeat(title.length));
    for (const entry of section.entries) {
      const text = describeEntry(entry, level, t);
      lines.push(`* ${text.item}: ${text.where}`);
      if (text.before) lines.push(`    ${t('hints.cleanGame', { place: text.before })}`);
      if (text.note) lines.push(`    ${text.note}`);
    }
    lines.push('');
  }
  lines.push(t('hints.export.footer'));
  return `${lines.join('\n')}\n`;
}

const HTML_STYLE = `
:root { color-scheme: light dark; --bg: #ffffff; --fg: #1d2230; --muted: #5d6677; --line: #d9dde5; --accent: #8a5a12; --card: #f5f6f8; }
@media (prefers-color-scheme: dark) { :root { --bg: #11151c; --fg: #e8ecf3; --muted: #9aa3b2; --line: #2b3240; --accent: #e0b36a; --card: #181d27; } }
* { box-sizing: border-box; }
body { margin: 0; padding: 24px 16px 48px; background: var(--bg); color: var(--fg); font: 16px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; }
main { max-width: 860px; margin: 0 auto; }
h1 { font-size: 26px; margin: 0 0 4px; }
h2 { font-size: 19px; margin: 28px 0 8px; padding-bottom: 4px; border-bottom: 2px solid var(--accent); }
.meta, .summary { display: grid; grid-template-columns: max-content 1fr; gap: 4px 16px; margin: 12px 0; padding: 12px 14px; background: var(--card); border: 1px solid var(--line); border-radius: 8px; }
.meta dt, .summary dt { color: var(--muted); }
.meta dd, .summary dd { margin: 0; overflow-wrap: anywhere; }
.badge { display: inline-block; padding: 1px 10px; border-radius: 999px; background: var(--accent); color: var(--bg); font-weight: 700; font-size: 14px; }
ul.hints { list-style: none; margin: 0; padding: 0; }
ul.hints li { padding: 8px 2px; border-bottom: 1px solid var(--line); }
ul.hints .item { font-weight: 650; }
ul.hints .where { display: block; }
ul.hints .before, ul.hints .note { display: block; color: var(--muted); font-size: 14px; }
.empty, footer { color: var(--muted); }
footer { margin-top: 32px; font-size: 14px; }
@media print { body { background: #fff; color: #000; padding: 0; } .meta, .summary { background: #fff; } h2 { break-after: avoid; } ul.hints li { break-inside: avoid; } }
`;

/**
 * The HTML form: one self-contained file. No script, no remote resource and no network: a Content-Security-Policy that allows nothing but inline
 * style states that, and every dynamic value is escaped. Responsive and printable.
 */
export function buildHintHtml(input: HintExportInput): string {
  const { document, level, language, translate: t } = input;
  const entries = filterHints(document.records, level);
  const e = escapeHtml;
  const out: string[] = [];
  out.push('<!doctype html>');
  out.push(`<html lang="${e(language)}">`);
  out.push('<head>');
  out.push('<meta charset="utf-8">');
  out.push('<meta name="viewport" content="width=device-width, initial-scale=1">');
  out.push('<meta http-equiv="Content-Security-Policy" content="default-src \'none\'; style-src \'unsafe-inline\'; img-src data:">');
  out.push(`<meta name="generator" content="DS1 Triple Randomizer ${e(document.appVersion)}">`);
  out.push(`<title>${e(t('hints.export.title'))} - ${e(shortSeed(document.seedCode))}</title>`);
  out.push(`<style>${HTML_STYLE}</style>`);
  out.push('</head>', '<body>', '<main>');
  out.push(`<h1>${e(t('hints.export.title'))}</h1>`);
  out.push(`<p><span class="badge">${e(t(`hints.levels.${level}.name`))}</span> ${e(t(`hints.levels.${level}.description`))}</p>`);
  out.push('<dl class="meta">');
  out.push(`<dt>${e(t('hints.export.seedCode'))}</dt><dd><code>${e(document.seedCode)}</code></dd>`);
  out.push(`<dt>${e(t('hints.export.profile'))}</dt><dd><code>${e(document.generationProfile)}</code></dd>`);
  out.push(`<dt>${e(t('hints.export.created'))}</dt><dd>${e(dateText(document.createdAt, language))}</dd>`);
  out.push(`<dt>${e(t('hints.export.level'))}</dt><dd>${e(t(`hints.levels.${level}.name`))}</dd>`);
  out.push(`<dt>${e(t('hints.export.language'))}</dt><dd>${e(t(`hints.languages.${language}`))}</dd>`);
  out.push('</dl>');
  out.push(`<h2>${e(t('hints.export.settings'))}</h2>`, '<dl class="summary">');
  for (const line of summarizeWorld(document.settings)) { const text = summaryText(line, t); out.push(`<dt>${e(text.label)}</dt><dd>${e(text.value)}</dd>`); }
  out.push('</dl>');
  if (entries.length === 0) out.push(`<p class="empty">${e(t('hints.empty'))}</p>`);
  for (const section of hintSections(entries)) {
    out.push(`<h2>${e(t(`hints.sections.${section.id}`))}</h2>`, '<ul class="hints">');
    for (const entry of section.entries) {
      const text = describeEntry(entry, level, t);
      out.push(`<li><span class="item">${e(text.item)}</span><span class="where">${e(text.where)}</span>`
        + `${text.before ? `<span class="before">${e(t('hints.cleanGame', { place: text.before }))}</span>` : ''}`
        + `${text.note ? `<span class="note">${e(text.note)}</span>` : ''}</li>`);
    }
    out.push('</ul>');
  }
  out.push(`<footer>${e(t('hints.export.footer'))}</footer>`, '</main>', '</body>', '</html>');
  return `${out.join('\n')}\n`;
}

export function buildHintExport(format: HintExportFormat, input: HintExportInput): string {
  if (format === 'json') return buildHintJson(input);
  return format === 'txt' ? buildHintText(input) : buildHintHtml(input);
}
