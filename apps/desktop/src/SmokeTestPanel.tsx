import { useState } from 'react';
import type { GenerationSettings } from '@ds1tr/contracts';
import { api, type SmokeTestList } from './api';
import type { LoadedSeed } from './SeedPanel';

interface Props {
  busy: boolean;
  onCreate: (seed: LoadedSeed) => void;
}

const estimate = (seconds: number) => (seconds < 90 ? `about ${seconds} seconds` : `about ${Math.round(seconds / 30) / 2} minutes`);

/**
 * For people who test the randomizer in the game: fixed worlds with a short list of what to look at. It lives in Advanced and
 * creates the world through the normal Load seed path. Quick smoke tests show one feature within about a minute of gaining
 * control, at a place stated exactly; extended gameplay tests need progression or exercise a whole system. Nothing here is
 * checked automatically.
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
  const quick = list?.worlds.filter((entry) => entry.category === 'quick') ?? [];
  const extended = list?.worlds.filter((entry) => entry.category === 'extended') ?? [];
  return (
    <details className="log-panel" onToggle={(event) => { if ((event.currentTarget as HTMLDetailsElement).open) void load(); }}>
      <summary>Test worlds for playtesting</summary>
      <p className="field-note">Fixed worlds for checking risky parts of the randomizer in the game. They are made with the normal Load seed path; nothing here is tested for you. Play offline.</p>
      {error && <div className="notice error" role="alert">{error}</div>}
      {list && (
        <>
          <label className="field-label" htmlFor="smoke-world">Test world</label>
          <select id="smoke-world" value={selected} onChange={(event) => setSelected(event.target.value)}>
            <optgroup label="Quick smoke tests (about a minute to the target)">
              {quick.map((entry) => <option key={entry.id} value={entry.id}>{entry.title}</option>)}
            </optgroup>
            <optgroup label="Extended gameplay tests (travel or progression needed)">
              {extended.map((entry) => <option key={entry.id} value={entry.id}>{entry.title}</option>)}
            </optgroup>
          </select>
          {world && (
            <div className="smoke-details">
              <strong className={`smoke-category ${world.category}`}>{world.category === 'quick' ? 'Quick smoke test' : 'Extended gameplay test'}</strong>
              <p>{world.purpose}</p>
              <p className={`smoke-result ${world.gameplay ? 'passed' : 'unplayed'}`}>{world.gameplay ? `Played in the real game (${world.gameplay.date}): ${world.gameplay.result === 'passed' ? 'passed' : 'partly passed'}. ${world.gameplay.note}` : 'Not played in the real game yet.'}</p>
              {world.category === 'quick' ? (
                <>
                  <strong>Target</strong>
                  <ul>{world.targets.map((line) => <li key={line}>{line}</li>)}</ul>
                  <strong>Expected</strong>
                  <ul>{world.expected.map((line) => <li key={line}>{line}</li>)}</ul>
                  {world.estimateSeconds !== null && <p><strong>Estimated time to the target:</strong> {estimate(world.estimateSeconds)} after you gain control (estimated from the distance, not timed in the game).</p>}
                  <strong>Check</strong>
                  <ul>{world.observe.map((line) => <li key={line}>{line}</li>)}</ul>
                  {world.afterwards.length > 0 && (<><strong>Afterwards</strong><ul>{world.afterwards.map((line) => <li key={line}>{line}</li>)}</ul></>)}
                  <p className="field-note">Everything outside the Undead Asylum is randomized too; ignore it. {list.start}.</p>
                </>
              ) : (
                <>
                  <strong>In this world</strong>
                  <ul>{world.observe.map((line) => <li key={line}>{line}</li>)}</ul>
                  <strong>Check in the game</strong>
                  <ul>{list.checklist.map((line) => <li key={line}>{line}</li>)}</ul>
                </>
              )}
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
