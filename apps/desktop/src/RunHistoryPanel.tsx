import { useState } from 'react';
import type { GameStateReport, RunHistoryEntry } from '@ds1tr/contracts';
import { SummaryChips, shortSeedCode } from './SeedPanel';

const statusText: Record<RunHistoryEntry['status'], string> = { generated: 'Generated', applied: 'Applied', restored: 'Restored' };

export const formatRunTime = (iso: string): string => {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleString(undefined, { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
};

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
}

/** Persistent run history: collapsed to one row until opened; each world shows only its quick actions, the rest is in the menu. */
export function RunHistoryPanel({ entries, gameState, busy, onApply, onRegenerate, onCopy, onLaunch, onDelete, onSpoiler }: Props) {
  const [open, setOpen] = useState(false);
  const [menuFor, setMenuFor] = useState<string | null>(null);
  const [details, setDetails] = useState<{ id: string; title: string; text: string } | null>(null);
  const vanilla = gameState?.state === 'vanilla';

  const showSettings = (entry: RunHistoryEntry) => { setMenuFor(null); setDetails({ id: entry.id, title: 'Settings of this world', text: JSON.stringify(entry.settings, null, 2) }); };
  const showSpoiler = async (entry: RunHistoryEntry) => {
    setMenuFor(null);
    setDetails({ id: entry.id, title: 'Spoiler log', text: await onSpoiler(entry) });
  };

  return (
    <div className="history">
      <button className="history-toggle" aria-expanded={open} aria-controls="run-history-list" onClick={() => setOpen((value) => !value)}>
        <span>Run history</span><span className="history-count">{entries.length}</span><span className="history-caret" aria-hidden="true">{open ? '▲' : '▼'}</span>
      </button>
      {open && (entries.length === 0
        ? <p className="field-note" id="run-history-list">Your randomized worlds will be listed here.</p>
        : <div className="history-list" id="run-history-list">
          {entries.map((entry) => {
            const files = Boolean(entry.packageExists);
            return (
              <div className="history-row" key={entry.id}>
                <div className="history-main">
                  <strong>{formatRunTime(entry.createdAt)}</strong>
                  <span className={`history-status ${entry.status}`}>{statusText[entry.status]}</span>
                  <code className="history-seed" title={entry.seedCode}>{shortSeedCode(entry.seedCode)}</code>
                  <SummaryChips settings={entry.settings} />
                </div>
                <div className="history-actions">
                  {!files
                    ? <button className="primary" disabled={busy || !vanilla} title="The generated files were cleaned up. This creates the same world again." onClick={() => onRegenerate(entry)}>Regenerate files</button>
                    : entry.status === 'applied'
                      ? <button className="primary" disabled={busy} onClick={onLaunch}>Launch game</button>
                      : <button className="primary" disabled={busy || !vanilla} title={vanilla ? '' : 'Restore the original game first.'} onClick={() => onApply(entry)}>Apply / Play</button>}
                  <button className="secondary" onClick={() => onCopy(entry)}>Copy seed</button>
                  <div className="menu-anchor">
                    <button className="secondary" aria-label="More actions" aria-haspopup="menu" aria-expanded={menuFor === entry.id} onClick={() => setMenuFor(menuFor === entry.id ? null : entry.id)}>…</button>
                    {menuFor === entry.id && (
                      <div className="menu" role="menu">
                        <button role="menuitem" onClick={() => showSettings(entry)}>View settings</button>
                        {entry.spoilerPath && <button role="menuitem" onClick={() => void showSpoiler(entry)}>View spoiler log</button>}
                        <button role="menuitem" className="danger" onClick={() => { setMenuFor(null); onDelete(entry); }}>Delete from history</button>
                      </div>
                    )}
                  </div>
                </div>
                {details?.id === entry.id && (
                  <div>
                    <div className="history-details-title"><strong>{details.title}</strong><button className="link-button" onClick={() => setDetails(null)}>Close</button></div>
                    <pre className="run-logs" aria-label={details.title}>{details.text}</pre>
                  </div>
                )}
              </div>
            );
          })}
        </div>)}
    </div>
  );
}
