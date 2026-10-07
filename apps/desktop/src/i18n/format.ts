import type { AppLanguage } from './index';

/**
 * Presentation formatting in the current app language. These helpers are for display only: Seed Codes, hashes, versions, ids and enum
 * values are never formatted, and none of this is an input of generation.
 */
const localeOf = (language: AppLanguage): string => (language === 'pl' ? 'pl-PL' : 'en-GB');

export function formatDateTime(iso: string, language: AppLanguage): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return new Intl.DateTimeFormat(localeOf(language), { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }).format(date);
}

export function formatDate(iso: string, language: AppLanguage): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return new Intl.DateTimeFormat(localeOf(language), { day: 'numeric', month: 'long', year: 'numeric' }).format(date);
}

export function formatNumber(value: number, language: AppLanguage, maximumFractionDigits = 1): string {
  return new Intl.NumberFormat(localeOf(language), { maximumFractionDigits }).format(value);
}

/** Bytes as a short size ("1.4 MB" / "1,4 MB"). */
export function formatBytes(bytes: number, language: AppLanguage): string {
  if (bytes < 1024) return `${formatNumber(bytes, language, 0)} B`;
  if (bytes < 1024 * 1024) return `${formatNumber(bytes / 1024, language)} KB`;
  return `${formatNumber(bytes / (1024 * 1024), language)} MB`;
}
