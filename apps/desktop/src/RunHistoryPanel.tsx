import { useState } from 'react';
import type { GameStateReport, RunHistoryEntry } from '@ds1tr/contracts';
import { SummaryChips } from './SeedPanel';

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
  onRecreate: (entry: RunHistoryEntry) => void;
  onCopy: (entry: RunHistoryEntry) => void;
  onLaunch: () => void;
}

/** Persistent run history: every world that was generated, with what a person wants to do with it. */
export function RunHistoryPanel({ entries, gameState, busy, onApply, onRecreate, onCopy, onLaunch }: Props) {
  const [viewing, setViewing] = useState<string | null>(null);
  if (entries.length === 0) return <p className="field-note">Your randomized worlds will be listed here.</p>;
  return (
    <div className="history-list">
      {entries.map((entry) => {
        const canApply = Boolean(entry.packageExists) && gameState?.state === 'vanilla';
        return (
          <div className="history-row" key={entry.id}>
            <div className="history-main">
              <strong>{formatRunTime(entry.createdAt)}</strong>
              <span className={`history-status ${entry.status}`}>{statusText[entry.status]}</span>
              <SummaryChips settings={entry.settings} />
            </div>
            <div className="run-actions">
              {entry.status === 'applied'
                ? <button className="primary" disabled={busy} onClick={onLaunch}>Launch game</button>
                : <button className="primary" disabled={busy || !canApply} title={entry.packageExists ? '' : 'The generated files were cleaned up. Use Recreate.'} onClick={() => onApply(entry)}>Apply / Play</button>}
              <button className="secondary" disabled={busy} onClick={() => onRecreate(entry)}>Recreate</button>
              <button className="secondary" onClick={() => onCopy(entry)}>Copy seed</button>
              <button className="secondary" onClick={() => setViewing(viewing === entry.id ? null : entry.id)}>{viewing === entry.id ? 'Hide settings' : 'View settings'}</button>
            </div>
            {viewing === entry.id && <pre className="run-logs" aria-label="Settings of this world">{JSON.stringify(entry.settings, null, 2)}</pre>}
          </div>
        );
      })}
    </div>
  );
}
