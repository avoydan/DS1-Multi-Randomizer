import enApp from '../locales/en/app.json';
import enCommon from '../locales/en/common.json';
import enErrors from '../locales/en/errors.json';
import enGame from '../locales/en/game.json';
import enHints from '../locales/en/hints.json';
import enHistory from '../locales/en/history.json';
import enPlan from '../locales/en/plan.json';
import enPool from '../locales/en/pool.json';
import enSeed from '../locales/en/seed.json';
import enSettings from '../locales/en/settings.json';
import enSmoke from '../locales/en/smoke.json';
import plApp from '../locales/pl/app.json';
import plCommon from '../locales/pl/common.json';
import plErrors from '../locales/pl/errors.json';
import plGame from '../locales/pl/game.json';
import plHints from '../locales/pl/hints.json';
import plHistory from '../locales/pl/history.json';
import plPlan from '../locales/pl/plan.json';
import plPool from '../locales/pl/pool.json';
import plSeed from '../locales/pl/seed.json';
import plSettings from '../locales/pl/settings.json';
import plSmoke from '../locales/pl/smoke.json';

/**
 * The resource files live in `locales/<language>/<area>.json`, one file per area ("namespace" in the project's sense). They are bundled as
 * the groups of one i18next namespace, so every key is `area.path.to.text` and the whole key set is type-checked from the English files.
 * Both languages must provide every area (checked by i18n/locales.test.mjs).
 */
export const en = { common: enCommon, app: enApp, settings: enSettings, game: enGame, hints: enHints, history: enHistory, seed: enSeed, smoke: enSmoke, pool: enPool, plan: enPlan, errors: enErrors } as const;
export const pl = { common: plCommon, app: plApp, settings: plSettings, game: plGame, hints: plHints, history: plHistory, seed: plSeed, smoke: plSmoke, pool: plPool, plan: plPlan, errors: plErrors } as const;

export const resources = { en: { translation: en }, pl: { translation: pl } } as const;
