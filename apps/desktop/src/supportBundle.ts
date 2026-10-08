import { buildSupportReadme, buildSupportSummary, type Translate } from '@ds1tr/contracts';
import { api } from './api';
import { currentLanguage } from './i18n';
import { formatBytes, formatDateTime } from './i18n/format';

export type BundleOutcome =
  | { kind: 'saved'; name: string; count: number; size: string }
  | { kind: 'cancelled' }
  | { kind: 'refused'; code: string }
  | { kind: 'failed' };

/**
 * "Export support bundle": the diagnostics are collected (sanitized by the backend), summary.txt and README.txt are written in the app language, the backend
 * builds the ZIP from its allowlist, and the main process lets the person choose where to save it. Nothing is sent anywhere.
 */
export async function exportSupportBundle(gamePath: string, worldId: string | undefined, translate: Translate): Promise<BundleOutcome> {
  try {
    const language = currentLanguage();
    const { diagnostics } = await api.diagnostics(gamePath, language, worldId);
    const formatTime = (iso: string): string => formatDateTime(iso, language);
    const result = await api.supportBundle({
      gamePath, language, worldId,
      summaryText: buildSupportSummary({ diagnostics, language }, translate, { formatTime }), readmeText: buildSupportReadme(translate),
    });
    if (!result.ok) return { kind: 'refused', code: result.code };
    const saved = await window.desktop?.saveFile({ kind: 'zip', defaultName: result.fileName, content: result.contentBase64 });
    if (saved?.ok) return { kind: 'saved', name: saved.name ?? result.fileName, count: result.report.entries.length, size: formatBytes(result.report.totalBytes, language) };
    return saved?.canceled ? { kind: 'cancelled' } : { kind: 'failed' };
  } catch { return { kind: 'failed' }; }
}
