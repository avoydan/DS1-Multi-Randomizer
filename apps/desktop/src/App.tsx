import { useCallback, useEffect, useMemo, useState } from 'react';
import { EnemyPoolEditor } from './EnemyPoolEditor';
import { Readiness } from './Readiness';
import { SeedPanel, SummaryChips, shortSeedCode, type LoadedSeed } from './SeedPanel';
import { RunHistoryPanel } from './RunHistoryPanel';
import { SmokeTestPanel } from './SmokeTestPanel';
import { DEFAULT_GENERATION_SETTINGS, OUTCOME_MESSAGES, deriveRunPhase, isLoadedSeedModified, joinConfig, splitConfig } from '@ds1tr/contracts';
import type { RunPhase, RunHistoryEntry, EnemyPlacementAlgorithm, FogLanguage, GameStateReport, GwynNerfMode, SecondGargoyleMode, SoulItemsMode, StartingItemsMode, GenerationResult, PipelinePlan, RandomizerConfig } from '@ds1tr/contracts';
import { api } from './api';
import ludos from './assets/branding/ludos.png';

// The same defaults the smoke-test worlds are built from (contracts): one definition, so the screen and the tests cannot drift apart.
// The master seed stays empty on purpose: it is made when Randomize is clicked and travels inside the Seed Code.
const initialConfig: RandomizerConfig = {
  ...joinConfig(structuredClone(DEFAULT_GENERATION_SETTINGS), '', '', true),
};

interface ToggleProps {
  checked: boolean;
  label: string;
  onChange: (value: boolean) => void;
  /** A planned extension: not implemented yet, shown with a badge. */
  disabled?: boolean;
  /** Implemented, but unavailable while its parent category is off. */
  inactive?: boolean;
  hint?: string;
}

function Toggle({ checked, onChange, label, disabled = false, inactive = false, hint }: ToggleProps) {
  return (
    <label className={`toggle-row ${disabled ? 'planned-option' : ''}`} title={hint}>
      <span>{label}{disabled && <small>Planned extension</small>}</span>
      <input type="checkbox" checked={checked} disabled={disabled || inactive} onChange={(event) => onChange(event.target.checked)} />
    </label>
  );
}

function RangeOption({ label, value, onChange, disabled = false, inactive = false, hint }: {
  label: string;
  value: number;
  onChange: (value: number) => void;
  disabled?: boolean;
  /** Implemented, but unavailable with the current algorithm. */
  inactive?: boolean;
  hint?: string;
}) {
  return (
    <label className={`range-option ${disabled ? 'planned-option' : ''}`} title={hint}>
      <span>{label}{disabled && <small>Planned extension</small>}</span>
      <div><input type="range" min="0" max="100" value={value} disabled={disabled || inactive} onChange={(event) => onChange(Number(event.target.value))} /><strong>{value}%</strong></div>
    </label>
  );
}

function stateText(state: GameStateReport | null): string {
  switch (state?.state) {
    case 'vanilla': return 'Your game is original and ready to be randomized.';
    case 'active': return 'A randomized game is applied. Restore the original game before making a new one.';
    case 'interrupted': return 'A previous change was interrupted. Click "Restore original game" to repair it.';
    case 'version-changed': return 'Dark Souls Remastered was updated. Restore the original game files, then try again.';
    case 'modified': return 'Some game files were changed by another tool. Verify the game files in Steam first.';
    case 'invalid': return 'This folder is not a Dark Souls Remastered installation.';
    case 'no-snapshot': return 'Checking your game...';
    default: return 'Looking for your game...';
  }
}

function phaseTitle(phase: RunPhase): string {
  switch (phase) {
    case 'generated': return 'Ready to apply';
    case 'applied': return 'Randomized game';
    case 'repair': return 'Repair needed';
    case 'blocked': return 'Your game';
    case 'working': return 'Working';
    default: return 'Randomize';
  }
}

function progressLabel(stage: string): string {
  switch (stage) {
    case 'verify': case 'import': return 'Checking your game';
    case 'combined': return 'Randomizing items and enemies';
    case 'fog': return 'Randomizing fog gates and warps';
    default: return 'Working';
  }
}

export function App() {
  const [config, setConfig] = useState(initialConfig);
  const [backendOnline, setBackendOnline] = useState<boolean | null>(null);
  const [startupError, setStartupError] = useState<string | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const [plan, setPlan] = useState<PipelinePlan | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('Choose your settings, then click Randomize.');
  const [gameState, setGameState] = useState<GameStateReport | null>(null);
  const [generation, setGeneration] = useState<GenerationResult | null>(null);
  const [progress, setProgress] = useState(0);
  const [progressStage, setProgressStage] = useState('');
  const [logs, setLogs] = useState<string[]>([]);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [presetText, setPresetText] = useState('');
  const [presetProblems, setPresetProblems] = useState<string[]>([]);
  const [seedPanelOpen, setSeedPanelOpen] = useState(false);
  const [lastRun, setLastRun] = useState<{ seedCode: string; profile: string; masterSeed: string } | null>(null);
  const [notice, setNotice] = useState<{ text: string; sub: string } | null>(null);
  const [history, setHistory] = useState<RunHistoryEntry[]>([]);
  const [settingsReady, setSettingsReady] = useState(false);
  const [settingsWarning, setSettingsWarning] = useState<string | null>(null);
  const [launchMessage, setLaunchMessage] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  /** A shared world waiting for the game to be original again (restoring is the one thing the person has to do). */
  const [pendingSeed, setPendingSeed] = useState<LoadedSeed | null>(null);
  const [workLabel, setWorkLabel] = useState('Working');
  const [runMenuOpen, setRunMenuOpen] = useState(false);

  useEffect(() => {
    api.health().then(setBackendOnline);
    window.desktop?.connection().then((state) => setStartupError(state.error));
  }, []);

  useEffect(() => {
    setPlan(null);
  }, [config]);

  const refreshHistory = useCallback(async () => {
    try { setHistory((await api.history()).entries); } catch { /* the list is a convenience */ }
  }, []);

  // Settings survive a restart: load them once the backend is up, then save every change (debounced).
  useEffect(() => {
    if (backendOnline !== true) return;
    void (async () => {
      try {
        const loaded = await api.loadSettings();
        if (loaded.found && loaded.persisted) {
          const persisted = loaded.persisted;
          setConfig((current) => joinConfig(persisted.settings, current.gamePath, '', persisted.generateSpoilerLog));
        }
        setSettingsWarning(loaded.warning);
      } catch { /* start from the defaults */ }
      setSettingsReady(true);
      await refreshHistory();
    })();
  }, [backendOnline, refreshHistory]);

  const currentSettings = useMemo(() => splitConfig(config).settings, [config]);
  const settingsKey = JSON.stringify(currentSettings);
  useEffect(() => {
    if (!settingsReady) return undefined;
    const timer = setTimeout(() => { void api.saveSettings(currentSettings, config.output.generateSpoilerLog); }, 400);
    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [settingsReady, settingsKey, config.output.generateSpoilerLog]);

  const appliedEntry = history.find((entry) => entry.status === 'applied') ?? null;
  const phase = deriveRunPhase({ busy, gameState: gameState?.state ?? null, hasGeneratedWorld: generation?.ok === true });

  const enabledCount = useMemo(
    () => Number(config.items.enabled || config.enemies.enabled) + Number(config.fog.enabled),
    [config],
  );

  const reportError = (error: unknown) => {
    const detail = error instanceof Error ? error.message : String(error);
    setLogs((current) => [...current, `error: ${detail}`]);
    setErrorMessage('Something went wrong. Please try again; the details are under Advanced.');
    setMessage('Something went wrong.');
  };

  const setItem = <K extends keyof RandomizerConfig['items']>(key: K, value: RandomizerConfig['items'][K]) =>
    setConfig((current) => ({ ...current, items: { ...current.items, [key]: value } }));
  const setEnemy = <K extends keyof RandomizerConfig['enemies']>(key: K, value: RandomizerConfig['enemies'][K]) =>
    setConfig((current) => ({ ...current, enemies: { ...current.enemies, [key]: value } }));
  /** Switching the algorithm moves the sampling-only settings to their legacy defaults (sampling) or neutral values (classic). */
  const setPlacementAlgorithm = (algorithm: EnemyPlacementAlgorithm) =>
    setConfig((current) => ({
      ...current,
      enemies: {
        ...current.enemies,
        placement: algorithm === 'sampling-v1'
          ? { algorithm, replacementChance: 100, normalReplacement: 'normals', fit: 'fits', difficultyMode: 'random-easy-asylum', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null, bossReplacement: 'classic', uniqueBosses: false, bossChanceInBossSlots: 90, bossChanceInNormalSlots: 10, roamingBossSouls: 50, roamingBossesStayDead: false }
          : { algorithm, replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null, bossReplacement: 'classic', uniqueBosses: false, bossChanceInBossSlots: 90, bossChanceInNormalSlots: 10, roamingBossSouls: 50, roamingBossesStayDead: false },
      },
    }));
  const setPlacement = <K extends keyof RandomizerConfig['enemies']['placement']>(key: K, value: RandomizerConfig['enemies']['placement'][K]) =>
    setConfig((current) => ({ ...current, enemies: { ...current.enemies, placement: { ...current.enemies.placement, [key]: value } } }));
  /** Leaving the modes that allow roaming bosses puts their settings back to the defaults, so a hidden setting never stays in the world. */
  const setNormalReplacement = (mode: RandomizerConfig['enemies']['placement']['normalReplacement']) =>
    setConfig((current) => {
      const roaming = mode === 'bosses' || mode === 'both';
      const placement = { ...current.enemies.placement, normalReplacement: mode };
      if (mode !== 'both') placement.bossChanceInNormalSlots = 10;
      if (!roaming) { placement.roamingBossSouls = 50; placement.roamingBossesStayDead = false; }
      return { ...current, enemies: { ...current.enemies, placement } };
    });
  /** Leaving "Bosses or normal enemies" puts its chance back to the default, and Unique bosses is off wherever no boss is drawn. */
  const setBossReplacement = (mode: RandomizerConfig['enemies']['placement']['bossReplacement']) =>
    setConfig((current) => {
      const placement = { ...current.enemies.placement, bossReplacement: mode };
      if (mode !== 'both') placement.bossChanceInBossSlots = 90;
      if (mode !== 'bosses' && mode !== 'both') placement.uniqueBosses = false;
      return { ...current, enemies: { ...current.enemies, placement } };
    });
  const setFog = <K extends keyof RandomizerConfig['fog']>(key: K, value: RandomizerConfig['fog'][K]) =>
    setConfig((current) => ({ ...current, fog: { ...current.fog, [key]: value } }));

  const importPristine = async () => {
    setBusy(true);
    setProgressStage('');
    setErrorMessage(null);
    try {
      const result = await api.importPristine(config.gamePath);
      setMessage(result.ok ? 'Dark Souls Remastered checked.' : result.message);
      if (!result.ok) setErrorMessage(result.message);
      setGameState(await api.state(config.gamePath));
      setRefreshKey((value) => value + 1);
    } catch (error) { reportError(error); }
    finally { setBusy(false); }
  };

  /** `seedCode` reproduces that exact world; without it a new world (a fresh master seed and a new Seed Code) is made. */
  const generateRun = async (seedCode?: string) => {
    setBusy(true);
    setProgress(0);
    setProgressStage('Preparing generation');
    setRunMenuOpen(false);
    setPendingSeed(null);
    setLogs([]);
    setGeneration(null);
    setErrorMessage(null);
    setNotice(null);
    setLaunchMessage(null);
    setCopied(false);
    try {
      const result = await api.generate(config, (event) => {
        if (event.type === 'progress') { setProgress(event.progress); setProgressStage(event.stage); setMessage(event.message); }
        else setLogs((current) => [...current, `[${event.stage}] ${event.level}: ${event.message}`]);
      }, seedCode);
      setGeneration(result);
      if (result.ok) {
        const detail = result as typeof result & { masterSeed?: string };
        setLastRun({ seedCode: result.seedCode, profile: result.profile, masterSeed: detail.masterSeed ?? result.manifest.masterSeed });
        void refreshHistory();
      } else {
        setErrorMessage(result.message);
        setLogs((current) => [...current, `[${result.stage}] failed: ${result.code}`]);
      }
      setMessage(result.ok ? 'Your randomized game is ready.' : result.message);
    } catch (error) { reportError(error); }
    finally { setBusy(false); }
  };

  const applyPackage = async (packagePath: string) => {
    setBusy(true);
    setWorkLabel('Applying the randomized game');
    setProgressStage('');
    setErrorMessage(null);
    setNotice(null);
    setLaunchMessage(null);
    try {
      const result = await api.activate(config.gamePath, packagePath);
      if (result.ok) {
        setNotice({ ...OUTCOME_MESSAGES.applied });
        setMessage(OUTCOME_MESSAGES.applied.text);
      } else {
        setMessage(result.message);
        setErrorMessage(`${result.message}${result.problems.length ? ` ${result.problems.join(' ')}` : ''}`);
      }
      setGameState(await api.state(config.gamePath));
      setRefreshKey((value) => value + 1);
      void refreshHistory();
    } catch (error) { reportError(error); }
    finally { setBusy(false); }
  };

  const activate = async () => {
    if (generation?.ok) await applyPackage(generation.packagePath);
  };

  const restore = async () => {
    setBusy(true);
    setWorkLabel('Restoring the original game');
    setProgressStage('');
    setErrorMessage(null);
    setNotice(null);
    setLaunchMessage(null);
    try {
      const result = await api.restore(config.gamePath);
      if (result.ok) {
        setGeneration(null);
        setNotice({ ...OUTCOME_MESSAGES.restored });
        setMessage(OUTCOME_MESSAGES.restored.text);
      } else {
        setMessage(result.message);
        setErrorMessage(`${result.message}${result.problems.length ? ` ${result.problems.join(' ')}` : ''}`);
      }
      setGameState(await api.state(config.gamePath));
      setRefreshKey((value) => value + 1);
      void refreshHistory();
    } catch (error) { reportError(error); }
    finally { setBusy(false); }
  };

  const launch = async () => {
    setLaunchMessage(null);
    try {
      const result = await window.desktop?.launchGame(config.gamePath);
      if (!result) { setLaunchMessage('Start Dark Souls Remastered from Steam.'); return; }
      setLaunchMessage(result.message);
      if (result.technical) setLogs((current) => [...current, `launch (${result.method}): ${result.technical}`]);
    } catch (error) {
      setLaunchMessage('Dark Souls Remastered could not be started. Please start it from Steam.');
      setLogs((current) => [...current, `launch: ${error instanceof Error ? error.message : String(error)}`]);
    }
  };

  const copySeed = async (code: string) => {
    try { await navigator.clipboard.writeText(code); setCopied(true); setMessage('Seed code copied.'); } catch { setMessage('Select the seed code and copy it with Ctrl+C.'); }
  };

  /** Puts the settings of a world into the controls and remembers that they came from a Seed Code. */
  const adoptSeed = (seed: LoadedSeed) => {
    setConfig((current) => joinConfig(seed.settings, current.gamePath, '', current.output.generateSpoilerLog));
    setSeedPanelOpen(false);
  };

  /** Load seed: take over the settings of the code and create that exact world. If the game is not original, restoring comes first. */
  const loadSeed = async (seed: LoadedSeed) => {
    adoptSeed(seed);
    setNotice(null);
    setErrorMessage(null);
    if (gameState?.state === 'vanilla') await generateShared(seed);
    else setPendingSeed(seed);
  };

  /** The Fog data has to be ready before a world that uses it is created; the check is quick when it already is. */
  const generateShared = async (seed: LoadedSeed) => {
    if (seed.settings.fog.enabled) {
      setBusy(true);
      setWorkLabel('Preparing the fog randomizer');
      try {
        const prepared = await api.prepareWorldData(config.gamePath);
        if (!prepared.ok) { setErrorMessage(prepared.userMessage); setBusy(false); return; }
      } catch (error) { setBusy(false); reportError(error); return; }
      setBusy(false);
    }
    await generateRun(seed.code);
  };

  /** Regenerate files: the same world again from its Seed Code. It refreshes the history entry; it is not a new run. */
  const regenerate = async (entry: RunHistoryEntry) => {
    await generateRun(entry.seedCode);
  };

  // A shared world that was waiting for an original game is created as soon as the game is original again.
  useEffect(() => {
    if (pendingSeed && phase === 'ready') {
      const seed = pendingSeed;
      setPendingSeed(null);
      void generateShared(seed);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pendingSeed, phase]);

  const deleteEntry = async (entry: RunHistoryEntry) => {
    try { await api.deleteHistoryEntry(entry.id); } catch { /* the entry stays */ }
    await refreshHistory();
  };

  const spoilerOf = async (entry: RunHistoryEntry): Promise<string> => {
    try {
      const result = await api.spoiler(entry.id);
      return result.ok ? result.text : result.message;
    } catch { return 'The spoiler log could not be read.'; }
  };

  const exportPreset = async () => {
    setPresetProblems([]);
    try {
      setPresetText(JSON.stringify(await api.exportPreset({ ...config, masterSeed: 'preset' }), null, 2));
      setMessage('Preset exported. Copy the text to share these settings.');
    } catch (error) { reportError(error); }
  };

  const importPreset = async () => {
    setPresetProblems([]);
    try {
      const result = await api.importPreset(presetText);
      if (!result.ok) { setPresetProblems(result.problems); return; }
      const { format: _format, presetVersion: _version, masterSeed: _ignoredSeed, ...settings } = result.preset;
      setConfig((current) => ({ ...current, ...settings }));
      setMessage('Preset applied. Review the settings, then click Randomize.');
    } catch (error) { reportError(error); }
  };

  const resetSettings = () => {
    setConfig((current) => ({ ...initialConfig, gamePath: current.gamePath }));
    setPresetProblems([]);
    setMessage('Settings restored to the defaults.');
  };

  const previewPlan = async () => {
    setBusy(true);
    setProgressStage('');
    setErrorMessage(null);
    try {
      setPlan(await api.plan({ ...config, masterSeed: lastRun?.masterSeed ?? 'preview' }));
      setMessage('Pipeline plan generated. Planned extensions remain disabled until implemented.');
    } catch (error) {
      reportError(error);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="app-shell">
      <header className="hero">
        <div className="hero-brand"><div className="eyebrow">DARK SOULS REMASTERED</div><h1>Triple Randomizer</h1><p>Randomize items, enemies and fog gates of Dark Souls Remastered.</p></div>
        <div className="hero-side">
          <div className={`status ${backendOnline ? 'online' : 'offline'}`}><span /> {backendOnline === null ? 'Starting...' : backendOnline ? 'Ready' : 'Not running'}</div>
          <img className="hero-ludos" src={ludos} alt="" aria-hidden="true" draggable={false} />
        </div>
      </header>

      <main>
        {settingsWarning && <div className="notice">{settingsWarning}</div>}
        {startupError && <div className="notice error">DS1 Triple Randomizer could not start its engine. Close and reopen the app; if it keeps happening, download the app again.<details><summary>Details</summary>{startupError}</details></div>}
        <section className="card setup-card">
          <div className="section-title"><span>01</span><h2>Your game</h2></div>
          <Readiness
            gamePath={config.gamePath}
            backendReady={backendOnline === true}
            worldNeeded={config.fog.enabled}
            busy={busy}
            refreshKey={refreshKey}
            onPath={(path) => { setConfig((current) => ({ ...current, gamePath: path })); setGameState(null); setGeneration(null); setErrorMessage(null); }}
            onState={setGameState}
          />
          <div className="inline-options">
            <Toggle disabled label="Include DLC" checked hint="Excluding the DLC is not implemented; the DLC is always included." onChange={() => undefined} />
            <Toggle label="Generate spoiler log" checked={config.output.generateSpoilerLog} onChange={(value) => setConfig({ ...config, output: { ...config.output, generateSpoilerLog: value } })} />
          </div>
          <details className="log-panel"><summary>Settings preset (import / export)</summary>
            <textarea className="run-logs" aria-label="Settings preset" rows={8} value={presetText} onChange={(event) => setPresetText(event.target.value)} placeholder="Paste a DS1TR preset here, or export the current settings." />
            <div className="run-actions"><button className="secondary" onClick={exportPreset}>Export current settings</button><button className="secondary" onClick={importPreset} disabled={!presetText.trim()}>Apply preset</button><button className="secondary" onClick={resetSettings}>Restore default settings</button></div>
            {presetProblems.length > 0 && <div className="notice error" role="alert">{presetProblems.join(' ')}</div>}
          </details>
        </section>

        <div className="grid-3">
          <section className={`card engine-card ${config.items.enabled ? '' : 'disabled-card'}`}>
            <div className="engine-heading"><div><span className="engine-index">ITEMS</span><h2>Item settings</h2></div><input type="checkbox" checked={config.items.enabled} onChange={(event) => setItem('enabled', event.target.checked)} /></div>
            <div className="option-group"><h3>Main options</h3>
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
            <div className="option-group"><h3>Legacy options</h3>
              <Toggle inactive={!config.items.randomizeWorldItems} label="Fashion Souls" hint="On: armor pieces are shuffled individually. Off: complete armor sets stay together and are exchanged between set locations." checked={config.items.fashionSouls} onChange={(value) => setItem('fashionSouls', value)} />
              <label className="field-label">Soul items</label>
              <select value={config.items.soulItems} disabled={!(config.items.randomizeWorldItems || config.items.randomizeGifts || config.items.randomizeEnemyDrops)} title="Replaced: the lesser souls (Soul of a Lost Undead through Soul of a Great Hero) become other consumables before shuffling." onChange={(event) => setItem('soulItems', event.target.value as SoulItemsMode)}><option value="shuffled">Shuffled</option><option value="replaced">Replaced</option><option value="transposed" disabled>Transposed (not available yet)</option></select>
              <label className="field-label">Starting items</label>
              <select value={config.items.startingItems} disabled={!config.items.randomizeStartingEquipment} title="Needs Starting equipment. Shield modes give the left hand a shield; mixed pool also allows weapons." onChange={(event) => setItem('startingItems', event.target.value as StartingItemsMode)}><option value="shield-1h">Shield + 1H weapon</option><option value="shield-2h">Shield + 1H/2H weapon</option><option value="mixed-pool">Shield/weapon + weapon</option></select>
            </div>
            <div className="option-group planned-group"><h3>Legacy parity <span>planned</span></h3>
              <label className="field-label">Difficulty</label><select defaultValue="fair" disabled><option value="fair">Fair</option><option value="unfair">Unfair</option><option value="very-unfair">Very Unfair</option></select>
              <label className="field-label">Key placement</label><select defaultValue="shuffled" disabled><option value="vanilla">Not Shuffled</option><option value="shuffled">Shuffled</option><option value="race">Race Mode</option><option value="race-plus">Race Mode+</option></select>
              {['Laundromat Mixup', 'Randomize Lordvessel', 'Randomize Lord Souls'].map((label) => <Toggle key={label} disabled label={label} checked={false} onChange={() => undefined} />)}
            </div>
          </section>

          <section className={`card engine-card ${config.enemies.enabled ? '' : 'disabled-card'}`}>
            <div className="engine-heading"><div><span className="engine-index">ENEMIES</span><h2>Enemy settings</h2></div><input type="checkbox" checked={config.enemies.enabled} onChange={(event) => setEnemy('enabled', event.target.checked)} /></div>
            <div className="option-group"><h3>Main options</h3>
              <Toggle label="Regular enemies" checked={config.enemies.randomizeRegularEnemies} onChange={(value) => setEnemy('randomizeRegularEnemies', value)} />
              <Toggle label="Bosses" checked={config.enemies.randomizeBosses} onChange={(value) => setEnemy('randomizeBosses', value)} />
              <label className="field-label">Scaling</label><select value={config.enemies.scaling} onChange={(event) => setEnemy('scaling', event.target.value as RandomizerConfig['enemies']['scaling'])}><option value="vanilla">Vanilla</option><option value="area">Area</option><option value="progressive">Progressive</option></select>
            </div>
            <div className="option-group"><h3>Enemy placement</h3>
              <label className="field-label">Placement algorithm</label>
              <select value={config.enemies.placement.algorithm} disabled={!config.enemies.randomizeRegularEnemies} title="Classic permutation keeps the DS1TR behavior and reproduces old seeds: the enemies of a run are the same enemies, shuffled. Legacy-style sampling decides every regular enemy slot on its own (chance, fit, Prevent same) and may repeat types; it changes every enemy layout. Opt-in." onChange={(event) => setPlacementAlgorithm(event.target.value as EnemyPlacementAlgorithm)}><option value="permutation-v1">Classic permutation</option><option value="sampling-v1">Legacy-style sampling</option></select>
              <small className="field-note">{config.enemies.placement.algorithm === 'sampling-v1'
                ? 'Per-slot legacy-style settings for regular enemies. Bosses, linked groups and NPCs are still planned the classic way; this is not yet full legacy parity.'
                : 'Preserves existing DS1TR behavior and old seeds. The settings below need Legacy-style sampling.'}</small>
              <RangeOption label="Replacement chance" hint="Chance that each regular enemy slot is replaced at all. Legacy-style sampling only. The 30-model map limit never gets worse: maps that bosses and dragons already push above 30 (Undead Parish) stay at or below their current count." value={config.enemies.placement.replacementChance} inactive={!config.enemies.randomizeRegularEnemies || config.enemies.placement.algorithm !== 'sampling-v1'} onChange={(value) => setPlacement('replacementChance', value)} />
              <label className="field-label">Normal enemy replacement</label>
              <select value={config.enemies.placement.normalReplacement} disabled={!config.enemies.randomizeRegularEnemies || config.enemies.placement.algorithm !== 'sampling-v1'} title="Legacy-style sampling only. Only normal enemies: the usual replacement. Only bosses: bosses stand in the regular enemy slots that take part (a boss that wanders the world, with a share of its souls). Bosses or normal enemies: each slot that takes part gets a boss with the chance set below, otherwise a normal enemy. Bosses that need their own arena (Bed of Chaos, Ceaseless Discharge, Dark Sun Gwyndolin, Lord Gwyn) and the multi-body bosses never appear in a regular slot." onChange={(event) => setNormalReplacement(event.target.value as RandomizerConfig['enemies']['placement']['normalReplacement'])}><option value="none">Disabled</option><option value="normals">Only normal enemies</option><option value="bosses">Only bosses</option><option value="both">Bosses or normal enemies</option></select>
              {config.enemies.placement.algorithm === 'sampling-v1' && config.enemies.placement.normalReplacement === 'both' && (
                <RangeOption label="Boss chance in normal slots" hint="Of the regular enemy slots that take part, the share that get a boss instead of a normal enemy." value={config.enemies.placement.bossChanceInNormalSlots} inactive={!config.enemies.randomizeRegularEnemies} onChange={(value) => setPlacement('bossChanceInNormalSlots', value)} />
              )}
              {config.enemies.placement.algorithm === 'sampling-v1' && (config.enemies.placement.normalReplacement === 'bosses' || config.enemies.placement.normalReplacement === 'both') && (
                <>
                  <RangeOption label="Roaming boss souls" hint="How many of its fight souls a boss drops when it stands in a regular enemy slot (percent of the souls the boss gives in its own arena)." value={config.enemies.placement.roamingBossSouls} inactive={!config.enemies.randomizeRegularEnemies} onChange={(value) => setPlacement('roamingBossSouls', value)} />
                  <Toggle inactive={!config.enemies.randomizeRegularEnemies} label="Roaming bosses stay dead" hint="On: a boss in a regular slot does not come back once the area is reset (played in the game: it stayed dead). Off: it respawns like a normal enemy (written to the game's data; not yet confirmed in a playthrough)." checked={config.enemies.placement.roamingBossesStayDead} onChange={(value) => setPlacement('roamingBossesStayDead', value)} />
                </>
              )}
              <label className="field-label">Placement</label>
              <select value={config.enemies.placement.fit} disabled={!config.enemies.randomizeRegularEnemies || config.enemies.placement.algorithm !== 'sampling-v1'} title="Legacy-style sampling only. Only where they fit: a replacement is never larger than the slot. Anywhere: any size. For regular enemies the last option equals Anywhere (it differs only for NPC slots)." onChange={(event) => setPlacement('fit', event.target.value as RandomizerConfig['enemies']['placement']['fit'])}><option value="fits">Only where they fit</option><option value="anywhere">Anywhere</option><option value="anywhere-except-npcs">Anywhere, fit NPCs</option></select>
              <label className="field-label">Difficulty mode</label>
              <select value={config.enemies.placement.difficultyMode} disabled={!config.enemies.randomizeRegularEnemies || config.enemies.placement.algorithm !== 'sampling-v1'} title="Legacy-style sampling only. Difficulty curve: a regular enemy is replaced by one of comparable difficulty (strictness decides how far it may differ). Fully random: any enemy anywhere. Easy Asylum: the Asylum only receives the easiest enemies. Difficulty classes are derived by DS1TR from the enemies' areas and HP, so they approximate the original ones." onChange={(event) => setPlacement('difficultyMode', event.target.value as RandomizerConfig['enemies']['placement']['difficultyMode'])}><option value="curve-easy-asylum">Curve + easy Asylum</option><option value="curve">Difficulty curve</option><option value="random-easy-asylum">Random + easy Asylum</option><option value="random">Fully random</option></select>
              <label className="field-label">Difficulty strictness</label>
              <select value={config.enemies.placement.difficultyStrictness} disabled={!config.enemies.randomizeRegularEnemies || config.enemies.placement.algorithm !== 'sampling-v1' || !config.enemies.placement.difficultyMode.startsWith('curve')} title="Only used by the difficulty-curve modes. Strict: mostly the same difficulty class, harder ones are rare. A bit loose: harder enemies appear more often. Very loose: any difficulty can appear anywhere." onChange={(event) => setPlacement('difficultyStrictness', event.target.value as RandomizerConfig['enemies']['placement']['difficultyStrictness'])}><option value="strict">Strict</option><option value="loose">A bit loose</option><option value="very-loose">Very loose</option></select>
              <label className="field-label">Type replacement</label>
              <select value={config.enemies.placement.typeReplacement} disabled={!config.enemies.randomizeRegularEnemies || config.enemies.placement.algorithm !== 'sampling-v1'} title="Legacy-style sampling only. Enabled: all enemies of one kind in a map (every Hollow in the Parish) are replaced by the same new kind, and two kinds in one map never get the same replacement. The replacement chance then applies to the kind, not to each enemy. With bosses in regular slots, Enabled gives every enemy of one kind the same boss; the exception lets each slot draw its own boss instead." onChange={(event) => setPlacement('typeReplacement', event.target.value as RandomizerConfig['enemies']['placement']['typeReplacement'])}><option value="enabled">Enabled</option><option value="enabled-except-roaming-bosses">Enabled, except roaming bosses</option><option value="disabled">Disabled</option></select>
              <label className="field-label">Boss replacement</label>
              <select value={config.enemies.placement.bossReplacement} disabled={!config.enemies.randomizeBosses || config.enemies.placement.algorithm !== 'sampling-v1'} title="Legacy-style sampling only. Classic shuffle: the bosses are shuffled among the boss slots as before. Random bosses: every single-body boss slot draws its own replacement from the boss pool, so a boss can appear more than once. Only normal enemies / Bosses or normal enemies: a normal enemy takes the place of the boss in three early fights (Asylum Demon, Taurus Demon, Capra Demon); the fight stays the boss's (its health bar and its name, its fog gate, what it unlocks and its rewards) and the normal enemy never comes back. With Area or Progressive scaling it takes the boss's health and strength, with Vanilla scaling its own. Every other boss keeps its own boss in Only normal enemies and gets a random boss in Bosses or normal enemies. The dragons, Seath, Priscilla, the Sanctuary Guardian, the Bell Gargoyles and the Centipede Demon keep the classic shuffle. Lord Gwyn follows the Gwyn spawn nerf." onChange={(event) => setBossReplacement(event.target.value as RandomizerConfig['enemies']['placement']['bossReplacement'])}><option value="classic">Classic shuffle</option><option value="bosses">Random bosses (legacy-style)</option><option value="normals">Only normal enemies (early fights)</option><option value="both">Bosses or normal enemies</option></select>
              {config.enemies.placement.algorithm === 'sampling-v1' && config.enemies.placement.bossReplacement === 'both' && (
                <RangeOption label="Boss chance in boss slots" hint="Of the boss slots that take part and can take a normal enemy, the share that get a boss; the rest get a normal enemy." value={config.enemies.placement.bossChanceInBossSlots} inactive={!config.enemies.randomizeBosses} onChange={(value) => setPlacement('bossChanceInBossSlots', value)} />
              )}
              <Toggle inactive={!config.enemies.randomizeBosses || config.enemies.placement.algorithm !== 'sampling-v1' || (config.enemies.placement.bossReplacement !== 'bosses' && config.enemies.placement.bossReplacement !== 'both')} label="Unique bosses" hint="Needs Random bosses or Bosses or normal enemies. Every boss of the pool is used once before any boss appears a second time." checked={config.enemies.placement.uniqueBosses} onChange={(value) => setPlacement('uniqueBosses', value)} />
              <EnemyPoolEditor gamePath={config.gamePath} active={config.enemies.randomizeRegularEnemies && config.enemies.placement.algorithm === 'sampling-v1'} fit={config.enemies.placement.fit} customPool={config.enemies.placement.customPool} onChange={(pool) => setPlacement('customPool', pool)} />
            </div>
            <div className="option-group"><h3>Legacy options</h3>
              <Toggle inactive={!config.enemies.randomizeRegularEnemies} label="Replace mimics" hint="Off: Mimics stay in place and are kept out of the enemy pool." checked={config.enemies.replaceMimics} onChange={(value) => setEnemy('replaceMimics', value)} />
              <Toggle inactive={!config.enemies.randomizeRegularEnemies} label="Replace respawning mosquitoes" hint="Off: the six respawning Blighttown swamp mosquitoes stay in place." checked={config.enemies.replaceRespawningMosquitoes} onChange={(value) => setEnemy('replaceRespawningMosquitoes', value)} />
              <Toggle inactive={!config.enemies.randomizeRegularEnemies} label="Prevent same enemy" hint="On: no regular enemy is replaced by an enemy of its own kind (a Hollow never becomes a Hollow). Bosses are never replaced by the same boss." checked={config.enemies.preventSameEnemy} onChange={(value) => setEnemy('preventSameEnemy', value)} />
              <Toggle inactive={!config.enemies.randomizeRegularEnemies} label="Pinwheel Chaos" hint="On: the main Pinwheel is not replaced; its clones are replaced by regular enemies with their own stats." checked={config.enemies.pinwheelChaos} onChange={(value) => setEnemy('pinwheelChaos', value)} />
              <label className="field-label">Replace second Gargoyle</label>
              <select value={config.enemies.secondGargoyle} disabled={!config.enemies.randomizeBosses} title="Needs boss randomization. Replace: the second Bell Gargoyle becomes an independently drawn boss. Do not replace: it stays a Bell Gargoyle. Single boss: the earlier DS1TR behavior (the extra Gargoyle is removed)." onChange={(event) => setEnemy('secondGargoyle', event.target.value as SecondGargoyleMode)}><option value="replace">Replace</option><option value="keep">Do not replace</option><option value="single-boss">Single boss (earlier DS1TR behavior)</option></select>
              <label className="field-label">Gwyn spawn nerf</label>
              <select value={config.enemies.gwynNerf} disabled={!config.enemies.randomizeBosses} title="Chance that Lord Gwyn is rejected as a boss replacement: strong 85%, medium 60%." onChange={(event) => setEnemy('gwynNerf', event.target.value as GwynNerfMode)}><option value="strong">Strong</option><option value="medium">Medium</option><option value="none">None</option></select>
            </div>
            <div className="option-group planned-group"><h3>Legacy parity <span>planned</span></h3>
              {['NPC replacement'].map((label) => <div key={label}><label className="field-label">{label}</label><select defaultValue="disabled" disabled><option value="disabled">Disabled</option><option value="bosses">Only bosses</option><option value="enemies">Only normal enemies</option><option value="all">Bosses or normal enemies</option></select></div>)}
              {['Allow T-pose behavior', 'Hostile NPCs'].map((label) => <Toggle key={label} disabled label={label} checked={false} onChange={() => undefined} />)}
            </div>
          </section>

          <section className={`card engine-card ${config.fog.enabled ? '' : 'disabled-card'}`}>
            <div className="engine-heading"><div><span className="engine-index">FOG</span><h2>Fog randomizer settings</h2><p className="engine-note">Randomizes fog gates, warps and world progression.</p></div><input type="checkbox" checked={config.fog.enabled} onChange={(event) => setFog('enabled', event.target.checked)} /></div>
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

        <section className={`card run-card phase-${phase}`}>
          <div className="section-title"><span>02</span><h2>{phaseTitle(phase)}</h2></div>
          <p className="sr-only" role="status">{message}</p>
          {phase === 'working' && (
            <div className="progress-status">
              <span>{progressStage ? `${progressLabel(progressStage)}: ${Math.round(progress * 100)}%` : `${workLabel}...`}</span>
              {progressStage ? <progress value={progress} max={1} aria-label="Progress" /> : <progress aria-label="Progress" />}
            </div>
          )}
          {phase !== 'working' && <p className="run-state">{stateText(gameState)}</p>}
          {errorMessage && <div className="notice error" role="alert">{errorMessage}</div>}

          {phase === 'ready' && (
            <>
              {notice && <div className="notice success apply-notice" role="status"><strong>{notice.text}</strong><span>{notice.sub}</span></div>}
              <SummaryChips settings={currentSettings} />
              <div className="run-actions">
                <button className="primary" onClick={() => void generateRun()} disabled={!config.gamePath || enabledCount === 0}>Randomize</button>
                <button className="secondary" onClick={() => setSeedPanelOpen((open) => !open)}>Load seed</button>
              </div>
            </>
          )}

          {phase === 'generated' && generation?.ok && (
            <>
              <div className="generated-status" role="status">
                <strong>Your randomized game is ready</strong>
                <textarea className="run-logs seed-output" readOnly rows={2} aria-label="Seed code" value={generation.seedCode} onFocus={(event) => event.currentTarget.select()} />
                <SummaryChips settings={generation.settings ?? currentSettings} />
                {generation.settings && isLoadedSeedModified(generation.settings, currentSettings) && <span className="stale-warning">You changed the settings after this world was created. Apply installs the world shown above; Randomize creates a new one.</span>}
              </div>
              <div className="run-actions">
                <button className="primary" onClick={() => void activate()}>Apply to game</button>
                <button className="secondary" onClick={() => void copySeed(generation.seedCode)}>{copied ? 'Copied' : 'Copy seed'}</button>
                <div className="menu-anchor">
                  <button className="secondary" aria-label="More actions" aria-haspopup="menu" aria-expanded={runMenuOpen} onClick={() => setRunMenuOpen((open) => !open)}>…</button>
                  {runMenuOpen && (
                    <div className="menu" role="menu">
                      <button role="menuitem" onClick={() => void generateRun()} disabled={!config.gamePath || enabledCount === 0}>Randomize again</button>
                      <button role="menuitem" onClick={() => { setRunMenuOpen(false); setSeedPanelOpen(true); }}>Load seed</button>
                    </div>
                  )}
                </div>
              </div>
            </>
          )}

          {phase === 'applied' && (
            <>
              {notice && <div className="notice success apply-notice" role="status"><strong>{notice.text}</strong><span>{notice.sub}</span></div>}
              {launchMessage && <div className="notice" role="status">{launchMessage}</div>}
              {pendingSeed && <div className="notice" role="status">To create the shared world, restore the original game first. It is created automatically afterwards.</div>}
              <div className="run-actions">
                <button className="primary" onClick={() => void launch()} disabled={!config.gamePath}>Launch game</button>
                <button className="secondary" onClick={restore}>{pendingSeed ? 'Restore and create shared world' : 'Restore original game'}</button>
                {appliedEntry && <button className="secondary" onClick={() => void copySeed(appliedEntry.seedCode)} title={appliedEntry.seedCode}>{copied ? 'Copied' : `Copy seed ${shortSeedCode(appliedEntry.seedCode)}`}</button>}
                <div className="menu-anchor">
                  <button className="secondary" aria-label="More actions" aria-haspopup="menu" aria-expanded={runMenuOpen} onClick={() => setRunMenuOpen((open) => !open)}>…</button>
                  {runMenuOpen && (
                    <div className="menu" role="menu">
                      <button role="menuitem" onClick={() => { setRunMenuOpen(false); setSeedPanelOpen(true); }}>Load seed</button>
                    </div>
                  )}
                </div>
              </div>
            </>
          )}

          {phase === 'repair' && (
            <>
              {pendingSeed && <div className="notice" role="status">To create the shared world, repair the game first. It is created automatically afterwards.</div>}
              <div className="run-actions"><button className="primary" onClick={restore}>Restore original game</button></div>
            </>
          )}

          {phase === 'blocked' && pendingSeed && <div className="notice" role="status">The shared world is created as soon as your game is ready.</div>}
          {phase === 'blocked' && gameState?.state === 'version-changed' && <div className="run-actions"><button className="primary" onClick={restore}>Restore original game</button></div>}

          {seedPanelOpen && phase !== 'working' && <SeedPanel busy={busy} onLoaded={(seed) => void loadSeed(seed)} onClose={() => setSeedPanelOpen(false)} />}

          <RunHistoryPanel
            entries={history} gameState={gameState} busy={busy}
            onApply={(entry) => void applyPackage(entry.packagePath)} onRegenerate={(entry) => void regenerate(entry)}
            onCopy={(entry) => void copySeed(entry.seedCode)} onLaunch={() => void launch()}
            onDelete={(entry) => void deleteEntry(entry)} onSpoiler={spoilerOf}
          />
          <details className="log-panel" open={logs.length > 0}><summary>Advanced</summary>
            <div className="run-actions">
              <button className="secondary" onClick={previewPlan} disabled={busy}>Preview randomization plan</button>
              <button className="secondary" onClick={importPristine} disabled={busy || !config.gamePath}>Check game files again</button>
            </div>
            {lastRun && <p>Internal master seed: <code>{lastRun.masterSeed}</code> - generation profile: <code>{lastRun.profile}</code></p>}
            {generation?.ok && <p>Files: <code>{generation.packagePath}</code> ({generation.manifest.files.length} files)</p>}
            <SmokeTestPanel busy={busy} onCreate={(seed) => void loadSeed(seed)} />
            <pre className="run-logs" aria-label="Details">{logs.length ? logs.join('\n') : 'Nothing to show yet.'}</pre>
          </details>
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
