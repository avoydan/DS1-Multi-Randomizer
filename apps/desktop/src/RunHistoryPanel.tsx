import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { GameStateReport, RunHistoryEntry } from '@ds1tr/contracts';
import { SummaryChips, shortSeedCode } from './SeedPanel';
import { currentLanguage } from './i18n';
import { formatDateTime } from './i18n/format';

/** The creation time in the current app language (display only; the entry keeps the ISO time). */
export const formatRunTime = (iso: string): string => formatDateTime(iso, currentLanguage());

interface Props {
  entries: RunHistoryEntry[];
  gameState: GameStateReport | null;
  busy: boolean;
  onApply: (entry: RunHistoryEntry) => void;
  onRegenerate: (entry: RunHistoryEntry) => void;
  onCopy: (entry: RunHistoryEntry) => void;
  onLaunch: () => void;
  onDelete: (entry: RunHistoryEntry) => void;
  onSpoiler: (entry: RunHistoryEntry) => Promise<string>;
  onHints: (entry: RunHistoryEntry, exportFirst: boolean) => void;
}

/** Persistent run history: collapsed to one row until opened; each world shows only its quick actions, the rest is in the menu. */
export function RunHistoryPanel({ entries, gameState, busy, onApply, onRegenerate, onCopy, onLaunch, onDelete, onSpoiler, onHints }: Props) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [menuFor, setMenuFor] = useState<string | null>(null);
  const [details, setDetails] = useState<{ id: string; kind: 'settings' | 'spoiler'; text: string } | null>(null);
  const vanilla = gameState?.state === 'vanilla';

  const showSettings = (entry: RunHistoryEntry) => { setMenuFor(null); setDetails({ id: entry.id, kind: 'settings', text: JSON.stringify(entry.settings, null, 2) }); };
  const showSpoiler = async (entry: RunHistoryEntry) => {
    setMenuFor(null);
    setDetails({ id: entry.id, kind: 'spoiler', text: await onSpoiler(entry) });
  };

  return (
    <div className="history">
      <button className="history-toggle" aria-expanded={open} aria-controls="run-history-list" onClick={() => setOpen((value) => !value)}>
        <span>{t('history.title')}</span><span className="history-count">{entries.length}</span><span className="history-caret" aria-hidden="true">{open ? '▲' : '▼'}</span>
      </button>
      {open && (entries.length === 0
        ? <p className="field-note" id="run-history-list">{t('history.empty')}</p>
        : <div className="history-list" id="run-history-list">
          {entries.map((entry) => {
            const files = Boolean(entry.packageExists);
            return (
              <div className="history-row" key={entry.id}>
                <div className="history-main">
                  <strong>{formatRunTime(entry.createdAt)}</strong>
                  <span className={`history-status ${entry.status}`}>{t(`history.status.${entry.status}`)}</span>
                  <code className="history-seed" title={entry.seedCode}>{shortSeedCode(entry.seedCode)}</code>
                  <SummaryChips settings={entry.settings} />
                </div>
                <div className="history-actions">
                  {!files
                    ? <button className="primary" disabled={busy || !vanilla} title={t('history.regenerateHint')} onClick={() => onRegenerate(entry)}>{t('history.regenerate')}</button>
                    : entry.status === 'applied'
                      ? <button className="primary" disabled={busy} onClick={onLaunch}>{t('common.launchGame')}</button>
                      : <button className="primary" disabled={busy || !vanilla} title={vanilla ? '' : t('history.restoreFirst')} onClick={() => onApply(entry)}>{t('history.applyPlay')}</button>}
                  <button className="secondary" onClick={() => onCopy(entry)}>{t('common.copySeed')}</button>
                  <div className="menu-anchor">
                    <button className="secondary" aria-label={t('common.moreActions')} aria-haspopup="menu" aria-expanded={menuFor === entry.id} onClick={() => setMenuFor(menuFor === entry.id ? null : entry.id)}>…</button>
                    {menuFor === entry.id && (
                      <div className="menu" role="menu">
                        <button role="menuitem" disabled={!entry.hintsAvailable} title={entry.hintsAvailable ? '' : t('hints.action.unavailable')} onClick={() => { setMenuFor(null); onHints(entry, false); }}>{t('hints.action.view')}</button>
                        <button role="menuitem" disabled={!entry.hintsAvailable} title={entry.hintsAvailable ? '' : t('hints.action.unavailable')} onClick={() => { setMenuFor(null); onHints(entry, true); }}>{t('hints.action.export')}</button>
                        <button role="menuitem" onClick={() => showSettings(entry)}>{t('history.viewSettings')}</button>
                        {entry.spoilerPath && <button role="menuitem" onClick={() => void showSpoiler(entry)}>{t('history.viewSpoiler')}</button>}
                        <button role="menuitem" className="danger" onClick={() => { setMenuFor(null); onDelete(entry); }}>{t('history.delete')}</button>
                      </div>
                    )}
                  </div>
                </div>
                {details?.id === entry.id && (
                  <div>
                    <div className="history-details-title"><strong>{t(`history.detailsTitle.${details.kind}`)}</strong><button className="link-button" onClick={() => setDetails(null)}>{t('common.close')}</button></div>
                    <pre className="run-logs" aria-label={t(`history.detailsTitle.${details.kind}`)}>{details.text}</pre>
                  </div>
                )}
              </div>
            );
          })}
        </div>)}
    </div>
  );
}
