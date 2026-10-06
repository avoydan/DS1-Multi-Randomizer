import { useState } from 'react';
import type { GenerationSettings } from '@ds1tr/contracts';
import { api, type SmokeTestList } from './api';
import type { LoadedSeed } from './SeedPanel';

interface Props {
  busy: boolean;
  onCreate: (seed: LoadedSeed) => void;
}

/**
 * For people who test the randomizer in the game: fixed worlds with a short list of what to look at. It lives in Advanced and
 * creates the world through the normal Load seed path. Nothing here is checked automatically.
 */
export function SmokeTestPanel({ busy, onCreate }: Props) {
  const [list, setList] = useState<SmokeTestList | null>(null);
  const [selected, setSelected] = useState('');
  const [error, setError] = useState<string | null>(null);

  const load = async () => {
    if (list) return;
    try {
      const result = await api.smokeTests();
      setList(result);
      setSelected(result.worlds[0]?.id ?? '');
    } catch { setError('The test worlds could not be loaded. Restart the app and try again.'); }
  };

  const world = list?.worlds.find((entry) => entry.id === selected) ?? null;
  return (
    <details className="log-panel" onToggle={(event) => { if ((event.currentTarget as HTMLDetailsElement).open) void load(); }}>
      <summary>Test worlds for playtesting</summary>
      <p className="field-note">Fixed worlds for checking risky parts of the randomizer in the game. They are made with the normal Load seed path; nothing here is tested for you.</p>
      {error && <div className="notice error" role="alert">{error}</div>}
      {list && (
        <>
          <label className="field-label" htmlFor="smoke-world">Test world</label>
          <select id="smoke-world" value={selected} onChange={(event) => setSelected(event.target.value)}>
            {list.worlds.map((entry) => <option key={entry.id} value={entry.id}>{entry.title}</option>)}
          </select>
          {world && (
            <div className="smoke-details">
              <p>{world.purpose}</p>
              <strong>In this world</strong>
              <ul>{world.observe.map((line) => <li key={line}>{line}</li>)}</ul>
              <strong>Check in the game</strong>
              <ul>{list.checklist.map((line) => <li key={line}>{line}</li>)}</ul>
              <label className="field-label" htmlFor="smoke-code">Seed code</label>
              <textarea id="smoke-code" className="run-logs seed-output" readOnly rows={2} value={world.seedCode} onFocus={(event) => event.currentTarget.select()} />
              <div className="run-actions"><button className="secondary" disabled={busy} onClick={() => onCreate({ code: world.seedCode, settings: world.settings as GenerationSettings })}>Create this test world</button></div>
            </div>
          )}
        </>
      )}
    </details>
  );
}
