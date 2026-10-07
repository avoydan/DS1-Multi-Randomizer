import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import { resources } from './resources';

/** The two languages the application ships; English is the canonical source and the fallback (docs/I18N.md). */
export type AppLanguage = 'en' | 'pl';
export type LanguageSetting = 'auto' | 'en' | 'pl';

/**
 * Initialises i18next with the bundled resources (no network, no file access). Called once before the first render, with the language
 * the main process resolved, so the interface never appears in the wrong language first.
 */
export async function initI18n(language: AppLanguage): Promise<void> {
  await i18n.use(initReactI18next).init({
    resources,
    lng: language,
    fallbackLng: 'en',
    defaultNS: 'translation',
    interpolation: { escapeValue: false },
    returnNull: false,
  });
  if (typeof document !== 'undefined') document.documentElement.lang = language;
}

export async function applyLanguage(language: AppLanguage): Promise<void> {
  await i18n.changeLanguage(language);
  if (typeof document !== 'undefined') document.documentElement.lang = language;
}

export const currentLanguage = (): AppLanguage => (i18n.language === 'pl' ? 'pl' : 'en');

export { i18n };
