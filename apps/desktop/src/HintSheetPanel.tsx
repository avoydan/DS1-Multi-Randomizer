import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  DEFAULT_HINT_LEVEL, HINT_LEVELS, buildHintExport, describeEntry, filterHints, hintFileName, hintSections, searchHints, summarizeWorld,
  type HintDocument, type HintExportFormat, type HintExportLanguage, type HintLevel, type Translate,
} from '@ds1tr/contracts';
import { api } from './api';
import { currentLanguage, i18n } from './i18n';
import { formatDateTime } from './i18n/format';
import { shortSeedCode } from './SeedPanel';

interface Props {
  /** The history entry whose hint sheet is shown. */
  entryId: string;
  /** Open with the export options expanded (the "Export hints" action). */
  exportFirst?: boolean;
  onClose: () => void;
}

type ExportChoice = 'current' | HintExportLanguage;
type Status = { kind: 'loading' } | { kind: 'unavailable' } | { kind: 'ready'; document: HintDocument };

/**
 * The hint sheet (docs/HINT_SHEET.md): a read-only report of where this world put things, at one of three spoiler levels. It is built from the data the
 * generator already recorded; switching the level only filters, and nothing here can change a world. Exports (HTML, TXT, JSON) are made in the language
 * chosen for the export, with the level shown on screen.
 */
export function HintSheetPanel({ entryId, exportFirst = false, onClose }: Props) {
  const { t } = useTranslation();
  const [status, setStatus] = useState<Status>({ kind: 'loading' });
  const [level, setLevel] = useState<HintLevel>(DEFAULT_HINT_LEVEL);
  const [query, setQuery] = useState('');
  const [exportLanguage, setExportLanguage] = useState<ExportChoice>('current');
  const [exportMessage, setExportMessage] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setStatus({ kind: 'loading' });
    api.hints(entryId)
      .then((result) => { if (!cancelled) setStatus(result.ok ? { kind: 'ready', document: result.document } : { kind: 'unavailable' }); })
      .catch(() => { if (!cancelled) setStatus({ kind: 'unavailable' }); });
    return () => { cancelled = true; };
  }, [entryId]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  const translate: Translate = (key, params) => t(key as never, params as never) as unknown as string;
  const document = status.kind === 'ready' ? status.document : null;
  const entries = useMemo(() => (document ? filterHints(document.records, level) : []), [document, level]);
  const shown = useMemo(() => searchHints(entries, query), [entries, query]);
  const sections = useMemo(() => hintSections(shown), [shown]);

  const exportAs = async (format: HintExportFormat) => {
    if (!document) return;
    const language: HintExportLanguage = exportLanguage === 'current' ? currentLanguage() : exportLanguage;
    const exportTranslate: Translate = (key, params) => i18n.getFixedT(language)(key as never, params as never) as unknown as string;
    const content = buildHintExport(format, { document, level, language, translate: exportTranslate });
    try {
      const result = await window.desktop?.saveFile({ kind: format, defaultName: hintFileName(document.seedCode, level, format), content });
      if (result?.ok) setExportMessage(t('hints.viewer.exported', { name: result.name ?? '' }));
      else setExportMessage(result?.canceled ? t('hints.viewer.exportCancelled') : t('hints.viewer.exportFailed'));
    } catch { setExportMessage(t('hints.viewer.exportFailed')); }
  };

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose(); }}>
      <div className="modal hint-sheet" role="dialog" aria-modal="true" aria-labelledby="hint-sheet-title">
        <div className="modal-header">
          <h2 id="hint-sheet-title">{t('hints.viewer.title')}</h2>
          <button className="secondary" onClick={onClose}>{t('hints.viewer.close')}</button>
        </div>
        {status.kind === 'loading' && <p className="field-note">{t('hints.viewer.loading')}</p>}
        {status.kind === 'unavailable' && <div className="notice">{t('hints.viewer.unavailable')}</div>}
        {document && (
          <>
            <p className="hint-warning">{t('hints.viewer.spoilerWarning')}</p>
            <dl className="hint-meta">
              <dt>{t('hints.export.seedCode')}</dt><dd><code title={document.seedCode}>{shortSeedCode(document.seedCode)}</code></dd>
              <dt>{t('hints.export.profile')}</dt><dd><code>{document.generationProfile}</code></dd>
              <dt>{t('hints.export.created')}</dt><dd>{formatDateTime(document.createdAt, currentLanguage())}</dd>
            </dl>
            <details className="hint-world">
              <summary>{t('hints.export.settings')}</summary>
              <dl className="hint-meta">
                {summarizeWorld(document.settings).map((line) => (
                  <div key={line.id} className="hint-meta-row">
                    <dt>{t(`hints.summary.${line.id}`)}</dt>
                    <dd>{typeof line.value === 'boolean' ? t(line.value ? 'hints.on' : 'hints.off') : translate(`settings.${line.optionPath}.options.${line.value}`)}</dd>
                  </div>
                ))}
              </dl>
            </details>
            <div className="hint-levels" role="radiogroup" aria-label={t('hints.viewer.level')}>
              {HINT_LEVELS.map((candidate) => (
                <button key={candidate} role="radio" aria-checked={candidate === level} className={candidate === level ? 'primary' : 'secondary'} onClick={() => setLevel(candidate)}>
                  {t(`hints.levels.${candidate}.name`)}
                </button>
              ))}
            </div>
            <p className="field-note">{t(`hints.levels.${level}.description`)}</p>
            <div className="hint-search">
              <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder={t('hints.viewer.searchPlaceholder')} aria-label={t('hints.viewer.search')} />
              <span className="field-note">{t('hints.viewer.count', { count: shown.length })}</span>
            </div>
            {document.unhintable > 0 && <p className="field-note">{t('hints.viewer.unhintable', { count: document.unhintable })}</p>}
            {entries.length === 0 && <p className="field-note">{document.records.length === 0 ? t('hints.empty') : t('hints.noProgression')}</p>}
            {entries.length > 0 && shown.length === 0 && <p className="field-note">{t('hints.viewer.noMatch')}</p>}
            <div className="hint-list">
              {sections.map((section) => (
                <section key={section.id}>
                  <h3>{t(`hints.sections.${section.id}`)}</h3>
                  <ul>
                    {section.entries.map((entry, index) => {
                      const text = describeEntry(entry, level, translate);
                      return (
                        <li key={`${entry.itemName}-${index}`}>
                          <strong>{text.item}</strong>
                          <span>{text.where}</span>
                          {text.before && <small>{t('hints.cleanGame', { place: text.before })}</small>}
                          {text.note && <small>{text.note}</small>}
                        </li>
                      );
                    })}
                  </ul>
                </section>
              ))}
            </div>
            <details className="hint-export" open={exportFirst}>
              <summary>{t('hints.viewer.exportTitle')}</summary>
              <label className="field-label" htmlFor="hint-export-language">{t('hints.viewer.exportLanguage')}</label>
              <select id="hint-export-language" value={exportLanguage} onChange={(event) => setExportLanguage(event.target.value as ExportChoice)}>
                <option value="current">{t('hints.viewer.exportCurrent', { language: t(`hints.languages.${currentLanguage()}`) })}</option>
                <option value="en">{t('hints.languages.en')}</option>
                <option value="pl">{t('hints.languages.pl')}</option>
              </select>
              <div className="run-actions">
                <button className="secondary" onClick={() => void exportAs('html')}>{t('hints.viewer.exportHtml')}</button>
                <button className="secondary" onClick={() => void exportAs('txt')}>{t('hints.viewer.exportTxt')}</button>
                <button className="secondary" onClick={() => void exportAs('json')}>{t('hints.viewer.exportJson')}</button>
              </div>
              <p className="field-note">{t('hints.viewer.exportNote')}</p>
              {exportMessage && <p className="field-note" role="status">{exportMessage}</p>}
            </details>
          </>
        )}
      </div>
    </div>
  );
}
