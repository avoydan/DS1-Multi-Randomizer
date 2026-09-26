import { useEffect, useMemo, useState } from 'react';
import type { InstallationValidation, PipelinePlan, RandomizerConfig } from '@ds1tr/contracts';
import { api } from './api';

const initialConfig: RandomizerConfig = {
  gamePath: '',
  masterSeed: String(Math.floor(Math.random() * 4_294_967_295)),
  items: {
    enabled: true,
    difficulty: 'medium',
    keyPlacement: 'shuffled',
    fashionSouls: true,
    laundromatMixup: false,
  },
  enemies: {
    enabled: true,
    randomizeBosses: true,
    difficultyCurve: true,
    uniqueBosses: false,
    hostileNpcs: false,
  },
  fog: {
    enabled: true,
    traversableFogs: true,
    bossFogs: true,
    areaWarps: true,
    majorPvpFogs: false,
    minorPvpFogs: false,
    lordvesselGates: false,
    disconnected: false,
    glitchedLogic: false,
    scaling: false,
  },
};

function Toggle({ checked, onChange, label }: { checked: boolean; onChange: (value: boolean) => void; label: string }) {
  return (
    <label className="toggle-row">
      <span>{label}</span>
      <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} />
    </label>
  );
}

export function App() {
  const [config, setConfig] = useState(initialConfig);
  const [backendOnline, setBackendOnline] = useState<boolean | null>(null);
  const [validation, setValidation] = useState<InstallationValidation | null>(null);
  const [plan, setPlan] = useState<PipelinePlan | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('Ready to configure.');

  useEffect(() => {
    api.health().then(setBackendOnline);
  }, []);

  const enabledCount = useMemo(
    () => Number(config.items.enabled) + Number(config.enemies.enabled) + Number(config.fog.enabled),
    [config],
  );

  const selectGame = async () => {
    const selected = await window.desktop?.selectGameDirectory();
    if (selected) {
      setConfig((current) => ({ ...current, gamePath: selected }));
      setValidation(null);
      setPlan(null);
    }
  };

  const validate = async () => {
    setBusy(true);
    try {
      const result = await api.validateInstallation(config.gamePath);
      setValidation(result);
      setMessage(result.valid ? 'Dark Souls Remastered installation looks valid.' : 'Installation needs attention.');
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error));
    } finally {
      setBusy(false);
    }
  };

  const previewPlan = async () => {
    setBusy(true);
    try {
      const result = await api.plan(config);
      setPlan(result);
      setMessage('Pipeline plan generated. Engine bridges are the next milestone.');
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="app-shell">
      <header>
        <div>
          <div className="eyebrow">DARK SOULS REMASTERED</div>
          <h1>Triple Randomizer</h1>
          <p>Items + Enemies + Fog Gates, one deterministic pipeline.</p>
        </div>
        <div className={`status ${backendOnline ? 'online' : 'offline'}`}>
          <span /> Backend {backendOnline === null ? 'checking' : backendOnline ? 'online' : 'offline'}
        </div>
      </header>

      <main>
        <section className="card setup-card">
          <div className="section-title"><span>01</span><h2>Game & Seed</h2></div>
          <label className="field-label">Dark Souls Remastered directory</label>
          <div className="path-row">
            <input value={config.gamePath} onChange={(e) => setConfig({ ...config, gamePath: e.target.value })} placeholder="C:\\Program Files (x86)\\Steam\\steamapps\\common\\DARK SOULS REMASTERED" />
            <button className="secondary" onClick={selectGame}>Browse</button>
            <button className="secondary" onClick={validate} disabled={busy || !config.gamePath}>Validate</button>
          </div>
          {validation && (
            <div className={validation.valid ? 'notice success' : 'notice error'}>
              {validation.valid ? 'Installation validated.' : validation.problems.join(' ')}
            </div>
          )}
          <label className="field-label">Master seed</label>
          <div className="seed-row">
            <input value={config.masterSeed} onChange={(e) => setConfig({ ...config, masterSeed: e.target.value })} />
            <button className="secondary" onClick={() => setConfig({ ...config, masterSeed: String(Math.floor(Math.random() * 4_294_967_295)) })}>Random</button>
          </div>
        </section>

        <div className="grid-3">
          <section className={`card engine-card ${config.items.enabled ? '' : 'disabled-card'}`}>
            <div className="engine-heading"><div><span className="engine-index">ITEMS</span><h2>Item Randomizer</h2></div><input type="checkbox" checked={config.items.enabled} onChange={(e) => setConfig({ ...config, items: { ...config.items, enabled: e.target.checked } })} /></div>
            <label className="field-label">Difficulty</label>
            <select value={config.items.difficulty} onChange={(e) => setConfig({ ...config, items: { ...config.items, difficulty: e.target.value as RandomizerConfig['items']['difficulty'] } })}>
              <option value="easy">Easy / Fair</option><option value="medium">Medium</option><option value="hard">Hard</option>
            </select>
            <label className="field-label">Key placement</label>
            <select value={config.items.keyPlacement} onChange={(e) => setConfig({ ...config, items: { ...config.items, keyPlacement: e.target.value as RandomizerConfig['items']['keyPlacement'] } })}>
              <option value="vanilla">Vanilla</option><option value="shuffled">Shuffled</option><option value="race">Race Mode</option><option value="race-plus">Race Mode+</option>
            </select>
            <Toggle label="Fashion Souls" checked={config.items.fashionSouls} onChange={(v) => setConfig({ ...config, items: { ...config.items, fashionSouls: v } })} />
            <Toggle label="Laundromat Mixup" checked={config.items.laundromatMixup} onChange={(v) => setConfig({ ...config, items: { ...config.items, laundromatMixup: v } })} />
          </section>

          <section className={`card engine-card ${config.enemies.enabled ? '' : 'disabled-card'}`}>
            <div className="engine-heading"><div><span className="engine-index">ENEMIES</span><h2>Enemy Randomizer</h2></div><input type="checkbox" checked={config.enemies.enabled} onChange={(e) => setConfig({ ...config, enemies: { ...config.enemies, enabled: e.target.checked } })} /></div>
            <Toggle label="Randomize bosses" checked={config.enemies.randomizeBosses} onChange={(v) => setConfig({ ...config, enemies: { ...config.enemies, randomizeBosses: v } })} />
            <Toggle label="Difficulty curve" checked={config.enemies.difficultyCurve} onChange={(v) => setConfig({ ...config, enemies: { ...config.enemies, difficultyCurve: v } })} />
            <Toggle label="Unique bosses" checked={config.enemies.uniqueBosses} onChange={(v) => setConfig({ ...config, enemies: { ...config.enemies, uniqueBosses: v } })} />
            <Toggle label="Hostile NPCs" checked={config.enemies.hostileNpcs} onChange={(v) => setConfig({ ...config, enemies: { ...config.enemies, hostileNpcs: v } })} />
          </section>

          <section className={`card engine-card ${config.fog.enabled ? '' : 'disabled-card'}`}>
            <div className="engine-heading"><div><span className="engine-index">WORLD</span><h2>Fog Randomizer</h2></div><input type="checkbox" checked={config.fog.enabled} onChange={(e) => setConfig({ ...config, fog: { ...config.fog, enabled: e.target.checked } })} /></div>
            <Toggle label="Traversable fogs" checked={config.fog.traversableFogs} onChange={(v) => setConfig({ ...config, fog: { ...config.fog, traversableFogs: v } })} />
            <Toggle label="Boss fogs" checked={config.fog.bossFogs} onChange={(v) => setConfig({ ...config, fog: { ...config.fog, bossFogs: v } })} />
            <Toggle label="Area warps" checked={config.fog.areaWarps} onChange={(v) => setConfig({ ...config, fog: { ...config.fog, areaWarps: v } })} />
            <Toggle label="Lordvessel gates" checked={config.fog.lordvesselGates} onChange={(v) => setConfig({ ...config, fog: { ...config.fog, lordvesselGates: v } })} />
            <Toggle label="Glitched logic" checked={config.fog.glitchedLogic} onChange={(v) => setConfig({ ...config, fog: { ...config.fog, glitchedLogic: v } })} />
            <Toggle label="Scaling" checked={config.fog.scaling} onChange={(v) => setConfig({ ...config, fog: { ...config.fog, scaling: v } })} />
          </section>
        </div>

        <section className="card action-card">
          <div><div className="eyebrow">PIPELINE</div><h2>{enabledCount} engines enabled</h2><p>{message}</p></div>
          <button className="primary" onClick={previewPlan} disabled={busy || !config.masterSeed}>Preview randomization plan</button>
        </section>

        {plan && (
          <section className="card plan-card">
            <div className="section-title"><span>02</span><h2>Pipeline preview</h2></div>
            <div className="seed-grid">
              {plan.stages.map((stage) => <div key={stage.engine}><span>{stage.engine}</span><strong>{stage.enabled ? stage.seed : 'disabled'}</strong></div>)}
            </div>
            <div className="engine-status-list">
              {plan.engineStatus.map((status) => (
                <div key={status.name} className="engine-status"><strong>{status.name}</strong><span className={`pill ${status.ready ? 'ready' : ''}`}>{status.mode}</span><p>{status.detail}</p></div>
              ))}
            </div>
          </section>
        )}
      </main>
    </div>
  );
}
