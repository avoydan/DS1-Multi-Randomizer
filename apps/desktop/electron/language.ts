/**
 * Application language (docs/I18N.md). The language is a presentation setting only: it never reaches the Seed Code, the generation
 * settings, a hash or the run history's world identity.
 */
export const LANGUAGE_SETTINGS = ['auto', 'en', 'pl'] as const;
export type LanguageSetting = (typeof LANGUAGE_SETTINGS)[number];
export type AppLanguage = 'en' | 'pl';

export const DEFAULT_LANGUAGE: AppLanguage = 'en';

export function normalizeLanguageSetting(value: unknown): LanguageSetting | null {
  return typeof value === 'string' && (LANGUAGE_SETTINGS as readonly string[]).includes(value) ? value as LanguageSetting : null;
}

/** `pl`, `pl-PL`, `pl_PL`, `PL` -> `pl`; anything that is not a plain language tag -> null. */
function primaryLanguage(locale: unknown): string | null {
  if (typeof locale !== 'string') return null;
  const match = /^([A-Za-z]{2,3})(?:[-_][A-Za-z0-9]+)*$/u.exec(locale.trim());
  return match ? match[1].toLowerCase() : null;
}

/** The first system language the application has a translation for; English when there is none. */
export function languageFromSystem(systemLocales: readonly unknown[]): AppLanguage {
  for (const locale of systemLocales) {
    const language = primaryLanguage(locale);
    if (language === 'pl') return 'pl';
    if (language === 'en') return 'en';
  }
  return DEFAULT_LANGUAGE;
}

/** A saved explicit choice wins; Auto follows the operating system (Electron's preferred system languages). */
export function resolveLanguage(setting: LanguageSetting | undefined, systemLocales: readonly unknown[]): AppLanguage {
  if (setting === 'en' || setting === 'pl') return setting;
  return languageFromSystem(systemLocales);
}

/** The few strings the main process shows itself (native dialogs, startup errors), in the resolved language. */
const MAIN_MESSAGES = {
  en: {
    selectGameFolder: 'Select the Dark Souls Remastered folder',
    saveFileTitle: 'Save file',
    startupFailedTitle: 'Desktop startup failed',
    backendTimeout: 'Backend startup timed out. Check the packaged runtime files.',
    backendCannotStart: 'Cannot start backend: {{reason}}',
    backendExited: 'Backend exited with code {{code}}.',
  },
  pl: {
    selectGameFolder: 'Wybierz folder Dark Souls Remastered',
    saveFileTitle: 'Zapisz plik',
    startupFailedTitle: 'Nie udało się uruchomić aplikacji',
    backendTimeout: 'Uruchamianie silnika przekroczyło limit czasu. Sprawdź pliki aplikacji.',
    backendCannotStart: 'Nie można uruchomić silnika: {{reason}}',
    backendExited: 'Silnik zakończył pracę z kodem {{code}}.',
  },
} as const;

export type MainMessageKey = keyof (typeof MAIN_MESSAGES)['en'];

export function mainMessage(language: AppLanguage, key: MainMessageKey, params: Record<string, string | number> = {}): string {
  const template: string = MAIN_MESSAGES[language][key] ?? MAIN_MESSAGES.en[key];
  return template.replace(/\{\{(\w+)\}\}/gu, (_whole, name: string) => String(params[name] ?? ''));
}

export const MAIN_MESSAGE_TABLES = MAIN_MESSAGES;
