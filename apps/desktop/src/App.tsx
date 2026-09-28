import { useEffect, useMemo, useState } from 'react';
import type { FogLanguage, GameStateReport, GenerationResult, InstallationValidation, PipelinePlan, RandomizerConfig } from '@ds1tr/contracts';
import { api } from './api';

const initialConfig: RandomizerConfig = {
  gamePath: '',
  masterSeed: String(Math.floor(Math.random() * 4_294_967_295)),
  items: {
    enabled: true,
    randomizeWorldItems: true,
    randomizeProtectedItems: false,
    progressionLogic: true,
    guaranteeEarlyWeapon: true,
    balancedEarlyLoot: true,
    randomizeStartingClass: false,
    randomizeStartingEquipment: false,
    randomizeGifts: false,
    randomizeEnemyDrops: false,
    guaranteedEnemyDrops: false,
    randomizeShops: false,
  },
  enemies: {
    enabled: true,
    randomizeRegularEnemies: true,
    randomizeBosses: true,
    scaling: 'area',
  },
  fog: {
    enabled: true,
    traversableFogs: true,
    bossFogs: true,
    areaWarps: true,
    majorPvpFogs: false,
    minorPvpFogs: false,
    lordvesselGates: false,
    randomStart: false,
    disconnectedFogs: false,
    noFallBedOfChaos: false,
    scaling: true,
    glitchedLogic: false,
    requireLordSouls: true,
    pacifistMode: false,
    language: 'ENGLISH',
  },
  output: {
    includeDlc: true,
    generateSpoilerLog: true,
  },
};

interface ToggleProps {
  checked: boolean;
  label: string;
  onChange: (value: boolean) => void;
  disabled?: boolean;
}

function Toggle({ checked, onChange, label, disabled = false }: ToggleProps) {
  return (
    <label className={`toggle-row ${disabled ? 'planned-option' : ''}`}>
      <span>{label}{disabled && <small>Planned extension</small>}</span>
      <input type="checkbox" checked={checked} disabled={disabled} onChange={(event) => onChange(event.target.checked)} />
    </label>
  );
}

function RangeOption({ label, value, onChange, disabled = false }: {
  label: string;
  value: number;
  onChange: (value: number) => void;
  disabled?: boolean;
}) {
  return (
    <label className={`range-option ${disabled ? 'planned-option' : ''}`}>
      <span>{label}{disabled && <small>Planned extension</small>}</span>
      <div><input type="range" min="0" max="100" value={value} disabled={disabled} onChange={(event) => onChange(Number(event.target.value))} /><strong>{value}%</strong></div>
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
  const [gameState, setGameState] = useState<GameStateReport | null>(null);
  const [generation, setGeneration] = useState<GenerationResult | null>(null);
  const [progress, setProgress] = useState(0);
  const [progressStage, setProgressStage] = useState('');
  const [logs, setLogs] = useState<string[]>([]);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [generatedConfig, setGeneratedConfig] = useState<string | null>(null);

  useEffect(() => {
    api.health().then(setBackendOnline);
  }, []);

  useEffect(() => {
    setPlan(null);
  }, [config]);

  const enabledCount = useMemo(
    () => Number(config.items.enabled || config.enemies.enabled) + Number(config.fog.enabled),
    [config],
  );
  const generationMatchesConfig = generatedConfig === JSON.stringify(config);

  const reportError = (error: unknown) => {
    const detail = error instanceof Error ? error.message : String(error);
    setErrorMessage(detail);
    setMessage(detail);
  };

  const setItem = <K extends keyof RandomizerConfig['items']>(key: K, value: RandomizerConfig['items'][K]) =>
    setConfig((current) => ({ ...current, items: { ...current.items, [key]: value } }));
  const setEnemy = <K extends keyof RandomizerConfig['enemies']>(key: K, value: RandomizerConfig['enemies'][K]) =>
    setConfig((current) => ({ ...current, enemies: { ...current.enemies, [key]: value } }));
  const setFog = <K extends keyof RandomizerConfig['fog']>(key: K, value: RandomizerConfig['fog'][K]) =>
    setConfig((current) => ({ ...current, fog: { ...current.fog, [key]: value } }));

  const selectGame = async () => {
    const selected = await window.desktop?.selectGameDirectory();
    if (selected) {
      setConfig((current) => ({ ...current, gamePath: selected }));
      setValidation(null);
      setPlan(null);
      setGameState(null);
      setGeneration(null);
      setGeneratedConfig(null);
      setErrorMessage(null);
    }
  };

  const validate = async () => {
    setBusy(true);
    setProgressStage('');
    setErrorMessage(null);
    try {
      const result = await api.validateInstallation(config.gamePath);
      setValidation(result);
      setGameState(await api.state(config.gamePath));
      setMessage(result.valid ? 'Dark Souls Remastered installation looks valid.' : 'Installation needs attention.');
    } catch (error) {
      reportError(error);
    } finally {
      setBusy(false);
    }
  };

  const importPristine = async () => {
    setBusy(true);
    setProgressStage('');
    setErrorMessage(null);
    try {
      const result = await api.importPristine(config.gamePath);
      setMessage(result.ok ? 'Pristine snapshot imported.' : `${result.code}: ${result.message}`);
      if (!result.ok) setErrorMessage(`${result.code}: ${result.message}${result.problems.length ? ` ${result.problems.join(' ')}` : ''}`);
      setGameState(await api.state(config.gamePath));
    } catch (error) { reportError(error); }
    finally { setBusy(false); }
  };

  const generateRun = async () => {
    setBusy(true);
    setProgress(0);
    setProgressStage('Preparing generation');
    setLogs([]);
    setGeneration(null);
    setGeneratedConfig(null);
    setErrorMessage(null);
    const submittedConfig = JSON.stringify(config);
    try {
      const result = await api.generate(config, (event) => {
        if (event.type === 'progress') { setProgress(event.progress); setProgressStage(event.stage); setMessage(event.message); }
        else setLogs((current) => [...current, `[${event.stage}] ${event.level}: ${event.message}`]);
      });
      setGeneration(result);
      if (result.ok) setGeneratedConfig(submittedConfig);
      else setErrorMessage(`Generation failed at ${result.stage}: ${result.code}: ${result.message}`);
      setMessage(result.ok ? `Package generated: ${result.packagePath}` : `${result.code}: ${result.message}`);
    } catch (error) { reportError(error); }
    finally { setBusy(false); }
  };

  const activate = async () => {
    if (!generation?.ok || !generationMatchesConfig) return;
    setBusy(true);
    setProgressStage('');
    setErrorMessage(null);
    try {
      const result = await api.activate(config.gamePath, generation.packagePath);
      setMessage(result.ok ? 'Generated package activated.' : `${result.code}: ${result.message}`);
      if (!result.ok) setErrorMessage(`${result.code}: ${result.message}${result.problems.length ? ` ${result.problems.join(' ')}` : ''}`);
      setGameState(await api.state(config.gamePath));
    } catch (error) { reportError(error); }
    finally { setBusy(false); }
  };

  const restore = async () => {
    setBusy(true);
    setProgressStage('');
    setErrorMessage(null);
    try {
      const result = await api.restore(config.gamePath);
      setMessage(result.ok ? `Vanilla files restored (${result.restored}).` : `${result.code}: ${result.message}`);
      if (!result.ok) setErrorMessage(`${result.code}: ${result.message}${result.problems.length ? ` ${result.problems.join(' ')}` : ''}`);
      setGameState(await api.state(config.gamePath));
    } catch (error) { reportError(error); }
    finally { setBusy(false); }
  };

  const previewPlan = async () => {
    setBusy(true);
    setProgressStage('');
    setErrorMessage(null);
    try {
      setPlan(await api.plan(config));
      setMessage('Pipeline plan generated. Planned extensions remain disabled until implemented.');
    } catch (error) {
      reportError(error);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="app-shell">
      <header>
        <div><div className="eyebrow">DARK SOULS REMASTERED</div><h1>Triple Randomizer</h1><p>Items + Enemies + Fog Gates, one deterministic pipeline.</p></div>
        <div className={`status ${backendOnline ? 'online' : 'offline'}`}><span /> Backend {backendOnline === null ? 'checking' : backendOnline ? 'online' : 'offline'}</div>
      </header>

      <main>
        <section className="card setup-card">
          <div className="section-title"><span>01</span><h2>Game, seed & output</h2></div>
          <label className="field-label">Dark Souls Remastered directory</label>
          <div className="path-row">
            <input value={config.gamePath} onChange={(event) => { setConfig({ ...config, gamePath: event.target.value }); setGameState(null); setGeneration(null); setGeneratedConfig(null); setValidation(null); }} placeholder="C:\\Program Files (x86)\\Steam\\steamapps\\common\\DARK SOULS REMASTERED" />
            <button className="secondary" onClick={selectGame}>Browse</button>
            <button className="secondary" onClick={validate} disabled={busy || !config.gamePath}>Validate</button>
          </div>
          {validation && <div className={validation.valid ? 'notice success' : 'notice error'}>{validation.valid ? 'Installation validated.' : validation.problems.join(' ')}</div>}
          <label className="field-label">Master seed</label>
          <div className="seed-row">
            <input value={config.masterSeed} onChange={(event) => setConfig({ ...config, masterSeed: event.target.value })} />
            <button className="secondary" onClick={() => setConfig({ ...config, masterSeed: String(Math.floor(Math.random() * 4_294_967_295)) })}>Random</button>
          </div>
          <div className="inline-options">
            <Toggle label="Include DLC" checked={config.output.includeDlc} onChange={(value) => setConfig({ ...config, output: { ...config.output, includeDlc: value } })} />
            <Toggle label="Generate spoiler log" checked={config.output.generateSpoilerLog} onChange={(value) => setConfig({ ...config, output: { ...config.output, generateSpoilerLog: value } })} />
          </div>
        </section>

        <div className="grid-3">
          <section className={`card engine-card ${config.items.enabled ? '' : 'disabled-card'}`}>
            <div className="engine-heading"><div><span className="engine-index">ITEMS</span><h2>Item settings</h2></div><input type="checkbox" checked={config.items.enabled} onChange={(event) => setItem('enabled', event.target.checked)} /></div>
            <div className="option-group"><h3>Combined engine</h3>
              <Toggle label="World items" checked={config.items.randomizeWorldItems} onChange={(value) => setItem('randomizeWorldItems', value)} />
              <Toggle disabled label="Protected items (unsafe: may softlock)" checked={config.items.randomizeProtectedItems} onChange={(value) => setItem('randomizeProtectedItems', value)} />
              <Toggle label="Protect selected gift/shop progression entries" checked={config.items.progressionLogic} onChange={(value) => setItem('progressionLogic', value)} />
              <Toggle label="Guarantee early weapon" checked={config.items.guaranteeEarlyWeapon} onChange={(value) => setItem('guaranteeEarlyWeapon', value)} />
              <Toggle label="Balanced early loot" checked={config.items.balancedEarlyLoot} onChange={(value) => setItem('balancedEarlyLoot', value)} />
              <Toggle label="Starting class" checked={config.items.randomizeStartingClass} onChange={(value) => setItem('randomizeStartingClass', value)} />
              <Toggle label="Starting equipment" checked={config.items.randomizeStartingEquipment} onChange={(value) => setItem('randomizeStartingEquipment', value)} />
              <Toggle label="NPC gifts" checked={config.items.randomizeGifts} onChange={(value) => setItem('randomizeGifts', value)} />
              <Toggle label="Enemy drops" checked={config.items.randomizeEnemyDrops} onChange={(value) => setItem('randomizeEnemyDrops', value)} />
              <Toggle label="Guaranteed enemy drops" checked={config.items.guaranteedEnemyDrops} onChange={(value) => setItem('guaranteedEnemyDrops', value)} />
              <Toggle label="Shops" checked={config.items.randomizeShops} onChange={(value) => setItem('randomizeShops', value)} />
            </div>
            <div className="option-group planned-group"><h3>Legacy parity <span>planned</span></h3>
              <label className="field-label">Difficulty</label><select defaultValue="fair" disabled><option value="fair">Fair</option><option value="unfair">Unfair</option><option value="very-unfair">Very Unfair</option></select>
              <label className="field-label">Key placement</label><select defaultValue="shuffled" disabled><option value="vanilla">Not Shuffled</option><option value="shuffled">Shuffled</option><option value="race">Race Mode</option><option value="race-plus">Race Mode+</option></select>
              <label className="field-label">Soul items</label><select defaultValue="shuffled" disabled><option value="shuffled">Shuffled</option><option value="replaced">Replaced</option><option value="transposed">Transposed</option></select>
              <label className="field-label">Starting items</label><select defaultValue="shield-1h" disabled><option value="shield-1h">Shield + 1H weapon</option><option value="shield-1h-2h">Shield + 1H/2H weapon</option><option value="mixed-pool">Shield/weapon + weapon</option></select>
              {['Fashion Souls', 'Laundromat Mixup', 'Randomize Lordvessel', 'Randomize Lord Souls'].map((label) => <Toggle key={label} disabled label={label} checked={false} onChange={() => undefined} />)}
            </div>
          </section>

          <section className={`card engine-card ${config.enemies.enabled ? '' : 'disabled-card'}`}>
            <div className="engine-heading"><div><span className="engine-index">ENEMIES</span><h2>Enemy settings</h2></div><input type="checkbox" checked={config.enemies.enabled} onChange={(event) => setEnemy('enabled', event.target.checked)} /></div>
            <div className="option-group"><h3>Combined engine</h3>
              <Toggle label="Regular enemies" checked={config.enemies.randomizeRegularEnemies} onChange={(value) => setEnemy('randomizeRegularEnemies', value)} />
              <Toggle label="Bosses" checked={config.enemies.randomizeBosses} onChange={(value) => setEnemy('randomizeBosses', value)} />
              <label className="field-label">Scaling</label><select value={config.enemies.scaling} onChange={(event) => setEnemy('scaling', event.target.value as RandomizerConfig['enemies']['scaling'])}><option value="vanilla">Vanilla</option><option value="area">Area</option><option value="progressive">Progressive</option></select>
            </div>
            <div className="option-group planned-group"><h3>Legacy parity <span>planned</span></h3>
              {['Boss replacement', 'Normal enemy replacement', 'NPC replacement'].map((label) => <div key={label}><label className="field-label">{label}</label><select defaultValue="disabled" disabled><option value="disabled">Disabled</option><option value="bosses">Only bosses</option><option value="enemies">Only normal enemies</option><option value="all">Bosses or normal enemies</option></select></div>)}
              <label className="field-label">Placement</label><select defaultValue="fit" disabled><option value="fit">Only where they fit</option><option value="anywhere">Anywhere</option><option value="fit-npcs">Anywhere, fit NPCs</option></select>
              <label className="field-label">Difficulty mode</label><select defaultValue="random" disabled><option value="curve-easy-asylum">Curve + easy Asylum</option><option value="curve">Difficulty curve</option><option value="random-easy-asylum">Random + easy Asylum</option><option value="random">Fully random</option></select>
              <RangeOption disabled label="Replacement chance" value={100} onChange={() => undefined} />
              <RangeOption disabled label="Boss chance: normal slots" value={10} onChange={() => undefined} />
              <RangeOption disabled label="Boss chance: boss slots" value={90} onChange={() => undefined} />
              <RangeOption disabled label="Roaming boss souls" value={100} onChange={() => undefined} />
              {['Replace mimics', 'Replace second Gargoyle', 'Allow T-pose behavior', 'Pinwheel Chaos', 'Prevent same enemy', 'Unique bosses', 'Respawning roaming bosses', 'Hostile NPCs', 'Replace respawning mosquitoes'].map((label) => <Toggle key={label} disabled label={label} checked={false} onChange={() => undefined} />)}
              <label className="field-label">Difficulty strictness</label><select defaultValue="loose" disabled><option value="strict">Strict</option><option value="loose">A bit loose</option><option value="very-loose">Very loose</option></select>
              <label className="field-label">Type replacement</label><select defaultValue="disabled" disabled><option value="enabled">Enabled</option><option value="exclude-roaming-bosses">Except roaming bosses</option><option value="disabled">Disabled</option></select>
              <label className="field-label">Gwyn spawn nerf</label><select defaultValue="medium" disabled><option value="strong">Strong</option><option value="medium">Medium</option><option value="none">None</option></select>
              <label className="field-label">Custom enemy pool</label><input value="Default" disabled readOnly />
            </div>
          </section>

          <section className={`card engine-card ${config.fog.enabled ? '' : 'disabled-card'}`}>
            <div className="engine-heading"><div><span className="engine-index">WORLD</span><h2>FogMod settings</h2></div><input type="checkbox" checked={config.fog.enabled} onChange={(event) => setFog('enabled', event.target.checked)} /></div>
            <div className="option-group"><h3>Entrances</h3>
              <Toggle label="Traversable non-boss fogs" checked={config.fog.traversableFogs} onChange={(value) => setFog('traversableFogs', value)} />
              <Toggle label="Boss fog gates" checked={config.fog.bossFogs} onChange={(value) => setFog('bossFogs', value)} />
              <Toggle label="Warps between areas" checked={config.fog.areaWarps} onChange={(value) => setFog('areaWarps', value)} />
              <Toggle label="Major PvP fog gates" checked={config.fog.majorPvpFogs} onChange={(value) => setFog('majorPvpFogs', value)} />
              <Toggle label="Minor PvP fog gates" checked={config.fog.minorPvpFogs} onChange={(value) => setFog('minorPvpFogs', value)} />
              <Toggle label="Lordvessel gates" checked={config.fog.lordvesselGates} onChange={(value) => setFog('lordvesselGates', value)} />
            </div>
            <div className="option-group"><h3>Logic & gameplay</h3>
              <Toggle label="Random start outside Asylum" checked={config.fog.randomStart} onChange={(value) => setFog('randomStart', value)} />
              <Toggle label="Disconnected fog gates" checked={config.fog.disconnectedFogs} onChange={(value) => setFog('disconnectedFogs', value)} />
              <Toggle label="No-Fall Bed of Chaos" checked={config.fog.noFallBedOfChaos} onChange={(value) => setFog('noFallBedOfChaos', value)} />
              <Toggle label="Scale enemies and bosses" checked={config.fog.scaling} onChange={(value) => setFog('scaling', value)} />
              <Toggle label="Glitched logic" checked={config.fog.glitchedLogic} onChange={(value) => setFog('glitchedLogic', value)} />
              <Toggle label="Require Lord Souls" checked={config.fog.requireLordSouls} onChange={(value) => setFog('requireLordSouls', value)} />
              <Toggle label="Pacifist Mode" checked={config.fog.pacifistMode} onChange={(value) => setFog('pacifistMode', value)} />
              <label className="field-label">Game language</label><select value={config.fog.language} onChange={(event) => setFog('language', event.target.value as FogLanguage)}><option value="ENGLISH">English</option><option value="FRENCH">French</option><option value="GERMAN">German</option><option value="ITALIAN">Italian</option><option value="JAPANESE">Japanese</option><option value="KOREAN">Korean</option><option value="NSPANISH">Latin American Spanish</option><option value="POLISH">Polish</option><option value="PORTUGUESE">Portuguese</option><option value="RUSSIAN">Russian</option><option value="SCHINESE">Simplified Chinese</option><option value="SPANISH">Spanish</option><option value="TCHINESE">Traditional Chinese</option></select>
            </div>
          </section>
        </div>

        <section className="card action-card"><div><div className="eyebrow">PIPELINE</div><h2>{enabledCount} engines enabled</h2><p role="status">{message}</p>{errorMessage && <div className="notice error" role="alert">{errorMessage}</div>}</div><button className="primary" onClick={previewPlan} disabled={busy || !config.masterSeed.trim()}>Preview randomization plan</button></section>

        <section className="card run-card">
          <div className="section-title"><span>02</span><h2>Run and game state</h2></div>
          <p>Installation: <strong>{gameState?.state ?? 'not checked'}</strong>{gameState?.problems.length ? ` — ${gameState.problems.join(' ')}` : ''}</p>
          {gameState?.activePackage && <p>Active package: {gameState.activePackage}</p>}
          <div className="run-actions">
            <button className="secondary" onClick={importPristine} disabled={busy || !config.gamePath || gameState?.state !== 'no-snapshot'}>Import pristine data</button>
            <button className="primary" onClick={generateRun} disabled={busy || !config.gamePath || !config.masterSeed.trim() || gameState?.state !== 'vanilla' || enabledCount === 0}>Generate run</button>
            <button className="secondary" onClick={activate} disabled={busy || !generation?.ok || !generationMatchesConfig || gameState?.state !== 'vanilla'}>Activate generated run</button>
            <button className="secondary" onClick={restore} disabled={busy || !gameState || !['active', 'interrupted'].includes(gameState.state)}>Restore vanilla</button>
          </div>
          {busy && progressStage && <div className="progress-status"><span>{progressStage}: {Math.round(progress * 100)}%</span><progress value={progress} max={1} aria-label="Generation progress" /></div>}
          {generation?.ok && <div className="generated-status" role="status"><strong>Generated run ready</strong><span>Seed: {generation.manifest.masterSeed} · {generation.manifest.files.length} files</span><code>{generation.packagePath}</code>{!generationMatchesConfig && <span className="stale-warning">Settings changed since generation. Regenerate to activate these settings.</span>}</div>}
          <details className="log-panel" open={logs.length > 0}><summary>Generation logs ({logs.length})</summary><pre className="run-logs" aria-label="Generation logs">{logs.length ? logs.join('\n') : 'No generation logs yet.'}</pre></details>
        </section>

        {plan && <section className="card plan-card"><div className="section-title"><span>02</span><h2>Pipeline preview</h2></div><div className="seed-grid">{plan.stages.map((stage) => <div key={stage.engine}><span>{stage.engine}</span><strong>{stage.enabled ? stage.seed : 'disabled'}</strong></div>)}</div><div className="engine-status-list">{plan.engineStatus.map((status) => <div key={status.name} className="engine-status"><strong>{status.name}</strong><span className={`pill ${status.ready ? 'ready' : ''}`}>{status.mode}</span><p>{status.detail}</p></div>)}</div></section>}

        <section className="card legal-card">
          <div><div className="eyebrow">ABOUT & LEGAL</div><h2>Free software under GPL-3.0-only</h2></div>
          <p>Copyright © 2026 DS1 Triple Randomizer contributors. This program comes with absolutely no warranty. You may redistribute and modify it under GNU GPL version 3. Corresponding source and the full license accompany every release.</p>
          <p>This unofficial project is not affiliated with FromSoftware or Bandai Namco Entertainment. Use only with your own game installation and play modified game data offline.</p>
          <a href="https://www.gnu.org/licenses/gpl-3.0.html" target="_blank" rel="noreferrer">Read the GNU GPL v3 license</a>
        </section>
      </main>
    </div>
  );
}
