import { useState } from 'react';
import type { GenerationSettings } from '@ds1tr/contracts';
import { api } from './api';
import { summarize } from './summary';

export interface LoadedSeed { code: string; settings: GenerationSettings }

export const SummaryChips = ({ settings }: { settings: GenerationSettings }) => {
  const summary = summarize(settings);
  const chip = (label: string, on: boolean) => <span key={label} className={`summary-chip ${on ? 'on' : 'off'}`}>{label} {on ? '✓' : '–'}</span>;
  return <div className="summary-row">{chip('Items', summary.items)}{chip('Enemies', summary.enemies)}{chip('Bosses', summary.bosses)}{chip('World', summary.world)}</div>;
};

interface Props {
  busy: boolean;
  onLoaded: (seed: LoadedSeed) => void;
  onClose: () => void;
}

/** "Load seed": paste a Seed Code; the app checks it and takes over the settings stored inside. */
export function SeedPanel({ busy, onLoaded, onClose }: Props) {
  const [text, setText] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [checking, setChecking] = useState(false);

  const load = async () => {
    setError(null);
    setChecking(true);
    try {
      const result = await api.decodeSeed(text);
      if (!result.ok) { setError(result.message); return; }
      onLoaded({ code: result.seedCode, settings: result.settings });
      setText('');
    } catch {
      setError('The seed code could not be checked. Restart the app and try again.');
    } finally {
      setChecking(false);
    }
  };

  return (
    <div className="seed-panel">
      <label className="field-label" htmlFor="seed-code-input">Shared seed code</label>
      <textarea id="seed-code-input" className="run-logs seed-input" rows={3} value={text} onChange={(event) => setText(event.target.value)} placeholder="Paste a seed code (DS1TR1-...)" />
      {error && <div className="notice error" role="alert">{error}</div>}
      <div className="run-actions">
        <button className="primary" disabled={busy || checking || !text.trim()} onClick={() => void load()}>Load</button>
        <button className="secondary" onClick={onClose}>Cancel</button>
      </div>
    </div>
  );
}

export function LoadedSeedSummary({ settings, modified, busy, onGenerate }: { settings: GenerationSettings; modified: boolean; busy: boolean; onGenerate: () => void }) {
  return (
    <div className={`loaded-seed ${modified ? 'modified' : ''}`}>
      <strong>{modified ? 'Settings changed' : 'Shared world loaded'}</strong>
      <SummaryChips settings={settings} />
      {modified
        ? <p>You changed the settings, so this is no longer the shared world. Randomize will create a new world with a new seed code.</p>
        : <><p>The settings below are the ones stored in the seed code.</p><button className="primary" disabled={busy} onClick={onGenerate}>Generate this world</button></>}
    </div>
  );
}
