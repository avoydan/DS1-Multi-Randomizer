import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import { ErrorNotice, useProblemText, type ErrorInfo } from './ErrorNotice';
import { LanguageSelect } from './LanguageSelect';
import { behaviorLabel, behaviorValue } from './planText';
import { HintSheetPanel } from './HintSheetPanel';
import { DiagnosticsPanel } from './DiagnosticsPanel';
import { exportSupportBundle } from './supportBundle';
import { EnemyPoolEditor } from './EnemyPoolEditor';
import { Readiness } from './Readiness';
import { SeedPanel, SummaryChips, shortSeedCode, type LoadedSeed } from './SeedPanel';
import { RunHistoryPanel } from './RunHistoryPanel';
import { SmokeTestPanel } from './SmokeTestPanel';
import { DEFAULT_GENERATION_SETTINGS, deriveRunPhase, isLoadedSeedModified, joinConfig, splitConfig } from '@ds1tr/contracts';
import type { RunPhase, RunHistoryEntry, EnemyPlacementAlgorithm, FogLanguage, GameStateReport, GwynNerfMode, SecondGargoyleMode, KeyPlacementMode, ItemDifficultyMode, SoulItemsMode, StartingItemsMode, GenerationResult, PipelinePlan, RandomizerConfig } from '@ds1tr/contracts';
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
  const { t } = useTranslation();
  return (
    <label className={`toggle-row ${disabled ? 'planned-option' : ''}`} title={hint}>
      <span>{label}{disabled && <small>{t('common.plannedExtension')}</small>}</span>
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
  const { t } = useTranslation();
  return (
    <label className={`range-option ${disabled ? 'planned-option' : ''}`} title={hint}>
      <span>{label}{disabled && <small>{t('common.plannedExtension')}</small>}</span>
      <div><input type="range" min="0" max="100" value={value} disabled={disabled || inactive} onChange={(event) => onChange(Number(event.target.value))} /><strong>{value}%</strong></div>
    </label>
  );
}

function stateText(state: GameStateReport | null, t: TFunction): string {
  switch (state?.state) {
    case 'vanilla': case 'active': case 'interrupted': case 'version-changed': case 'modified': case 'invalid': case 'no-snapshot':
      return t(`app.state.${state.state}`);
    default: return t('app.state.looking');
  }
}

function phaseTitle(phase: RunPhase, t: TFunction): string {
  switch (phase) {
    case 'generated': return t('app.phase.generated');
    case 'applied': return t('app.phase.applied');
    case 'repair': return t('app.phase.repair');
    case 'blocked': return t('app.phase.blocked');
    case 'working': return t('app.phase.working');
    default: return t('app.phase.ready');
  }
}

function progressLabel(stage: string, t: TFunction): string {
  switch (stage) {
    case 'verify': case 'import': return t('app.progress.checking');
    case 'combined': return t('app.progress.combined');
    case 'fog': return t('app.progress.fog');
    default: return t('app.progress.working');
  }
}

type WorkKey = 'working' | 'applying' | 'restoring' | 'preparingFog';
type NoticeKey = 'applied' | 'restored';
type LaunchKey = 'steam' | 'executable' | 'failed' | 'noBridge';

export function App() {
  const { t, i18n } = useTranslation();
  const problemText = useProblemText();
  const [config, setConfig] = useState(initialConfig);
  const [backendOnline, setBackendOnline] = useState<boolean | null>(null);
  const [startupError, setStartupError] = useState<string | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const [plan, setPlan] = useState<PipelinePlan | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const [gameState, setGameState] = useState<GameStateReport | null>(null);
  const [generation, setGeneration] = useState<GenerationResult | null>(null);
  const [progress, setProgress] = useState(0);
  const [progressStage, setProgressStage] = useState('');
  const [logs, setLogs] = useState<string[]>([]);
  const [problem, setProblem] = useState<ErrorInfo | null>(null);
  const [presetText, setPresetText] = useState('');
  const [presetProblems, setPresetProblems] = useState<string[]>([]);
  const [seedPanelOpen, setSeedPanelOpen] = useState(false);
  const [lastRun, setLastRun] = useState<{ seedCode: string; profile: string; masterSeed: string } | null>(null);
  const [notice, setNotice] = useState<NoticeKey | null>(null);
  const [history, setHistory] = useState<RunHistoryEntry[]>([]);
  const [settingsReady, setSettingsReady] = useState(false);
  const [settingsWarning, setSettingsWarning] = useState<string | null>(null);
  const [launchMessage, setLaunchMessage] = useState<LaunchKey | null>(null);
  const [copied, setCopied] = useState(false);
  /** A shared world waiting for the game to be original again (restoring is the one thing the person has to do). */
  const [pendingSeed, setPendingSeed] = useState<LoadedSeed | null>(null);
  const [workLabel, setWorkLabel] = useState<WorkKey>('working');
  const [runMenuOpen, setRunMenuOpen] = useState(false);
  /** The world whose hint sheet is open (history entry id), and whether it opened on the export options. */
  const [diagnosticsOpen, setDiagnosticsOpen] = useState(false);
  /** The id of the last operation the backend reported: lines the interface logs about it carry the same id. */
  const [lastOperationId, setLastOperationId] = useState<string | undefined>(undefined);
  const [diagnosticsTarget, setDiagnosticsTarget] = useState<string | null>(null);
  const [hintTarget, setHintTarget] = useState<{ id: string; exportFirst: boolean } | null>(null);

  useEffect(() => {
    api.health().then(setBackendOnline);
    window.desktop?.connection().then((state) => setStartupError(state.error));
  }, []);

  useEffect(() => {
    setPlan(null);
  }, [config]);

  // The transient status message is text in one language: drop it when the language changes (the plan preview is data and is re-rendered).
  useEffect(() => { setMessage(''); }, [i18n.language]);

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
    void api.log({ level: 'error', stage: 'APP', message: detail, correlationId: lastOperationId });
    setProblem({ kind: 'unexpected', detail });
    setMessage(t('app.message.failed'));
  };

  const setItem = <K extends keyof RandomizerConfig['items']>(key: K, value: RandomizerConfig['items'][K]) =>
    setConfig((current) => ({ ...current, items: { ...current.items, [key]: value } }));
  /**
   * Leaving the key modes puts the two switches that belong to them back to off, so a hidden switch never stays in the world. Race Mode + is only available with
   * Fair item difficulty (as in the legacy tool), so choosing it puts the difficulty back to Fair.
   */
  const setKeyPlacement = (mode: KeyPlacementMode) =>
    setConfig((current) => ({
      ...current,
      items: {
        ...current.items, keyPlacement: mode,
        ...(mode === 'vanilla' ? { randomizeLordvessel: false, randomizeLordSouls: false } : {}),
        ...(mode === 'race-plus' ? { itemDifficulty: 'fair' as ItemDifficultyMode } : {}),
      },
    }));
  const setEnemy = <K extends keyof RandomizerConfig['enemies']>(key: K, value: RandomizerConfig['enemies'][K]) =>
    setConfig((current) => ({ ...current, enemies: { ...current.enemies, [key]: value } }));
  /** Switching the algorithm moves the sampling-only settings to their legacy defaults (sampling) or neutral values (classic). */
  const setPlacementAlgorithm = (algorithm: EnemyPlacementAlgorithm) =>
    setConfig((current) => ({
      ...current,
      enemies: {
        ...current.enemies,
        placement: algorithm === 'sampling-v1'
          ? { algorithm, replacementChance: 100, normalReplacement: 'normals', fit: 'fits', difficultyMode: 'random-easy-asylum', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null, bossReplacement: 'classic', uniqueBosses: false, bossChanceInBossSlots: 90, hostileNpcs: false, bossChanceInNormalSlots: 10, roamingBossSouls: 50, roamingBossesStayDead: false }
          : { algorithm, replacementChance: 100, normalReplacement: 'normals', fit: 'anywhere', difficultyMode: 'random', difficultyStrictness: 'loose', typeReplacement: 'disabled', customPool: null, bossReplacement: 'classic', uniqueBosses: false, bossChanceInBossSlots: 90, hostileNpcs: false, bossChanceInNormalSlots: 10, roamingBossSouls: 50, roamingBossesStayDead: false },
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
      // Hostile NPCs join the pool of replaced normal enemies, so the setting goes back to off wherever none are replaced.
      if (mode !== 'normals' && mode !== 'both') placement.hostileNpcs = false;
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
  // The fog randomizer always contains the DLC areas, so turning it on puts the DLC back in (docs/DLC_AUDIT.md); the DLC switch is locked while it is on.
  const setFog = <K extends keyof RandomizerConfig['fog']>(key: K, value: RandomizerConfig['fog'][K]) =>
    setConfig((current) => ({
      ...current, fog: { ...current.fog, [key]: value },
      output: key === 'enabled' && value === true ? { ...current.output, includeDlc: true } : current.output,
    }));

  const importPristine = async () => {
    setBusy(true);
    setProgressStage('');
    setProblem(null);
    try {
      const result = await api.importPristine(config.gamePath);
      setMessage(result.ok ? t('app.message.gameChecked') : t(`errors.codes.${result.code}` as never, { defaultValue: result.message }) as string);
      if (!result.ok) setProblem({ code: result.code, detail: result.message, problems: result.problems });
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
    setProblem(null);
    setNotice(null);
    setLaunchMessage(null);
    setCopied(false);
    try {
      const result = await api.generate(config, (event) => {
        if (event.type === 'progress') { setProgress(event.progress); setProgressStage(event.stage); setMessage(progressLabel(event.stage, t)); }
        else setLogs((current) => [...current, `[${event.stage}] ${event.level}: ${event.message}`]);
      }, seedCode);
      setGeneration(result);
      if (result.correlationId) setLastOperationId(result.correlationId);
      if (result.ok) {
        const detail = result as typeof result & { masterSeed?: string };
        setLastRun({ seedCode: result.seedCode, profile: result.profile, masterSeed: detail.masterSeed ?? result.manifest.masterSeed });
        void refreshHistory();
      } else {
        setProblem({ code: result.code, detail: result.message });
        setLogs((current) => [...current, `[${result.stage}] failed: ${result.code}`]);
      }
      setMessage(result.ok ? t('app.message.worldReady') : t(`errors.codes.${result.code}` as never, { defaultValue: result.message }) as string);
    } catch (error) { reportError(error); }
    finally { setBusy(false); }
  };

  const applyPackage = async (packagePath: string) => {
    setBusy(true);
    setWorkLabel('applying');
    setProgressStage('');
    setProblem(null);
    setNotice(null);
    setLaunchMessage(null);
    try {
      const result = await api.activate(config.gamePath, packagePath);
      if (result.correlationId) setLastOperationId(result.correlationId);
      if (result.ok) {
        setNotice('applied');
        setMessage(t('app.outcome.applied.text'));
      } else {
        setMessage(t(`errors.codes.${result.code}` as never, { defaultValue: result.message }) as string);
        setProblem({ code: result.code, detail: result.message, problems: result.problems });
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
    setWorkLabel('restoring');
    setProgressStage('');
    setProblem(null);
    setNotice(null);
    setLaunchMessage(null);
    try {
      const result = await api.restore(config.gamePath);
      if (result.correlationId) setLastOperationId(result.correlationId);
      if (result.ok) {
        setGeneration(null);
        setNotice('restored');
        setMessage(t('app.outcome.restored.text'));
      } else {
        setMessage(t(`errors.codes.${result.code}` as never, { defaultValue: result.message }) as string);
        setProblem({ code: result.code, detail: result.message, problems: result.problems });
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
      if (!result) { setLaunchMessage('noBridge'); return; }
      setLaunchMessage(result.ok ? (result.method === 'steam' ? 'steam' : 'executable') : 'failed');
      if (result.technical) setLogs((current) => [...current, `launch (${result.method}): ${result.technical}`]);
    } catch (error) {
      setLaunchMessage('failed');
      setLogs((current) => [...current, `launch: ${error instanceof Error ? error.message : String(error)}`]);
    }
  };

  const copySeed = async (code: string) => {
    try { await navigator.clipboard.writeText(code); setCopied(true); setMessage(t('app.message.seedCopied')); } catch { setMessage(t('app.message.seedCopyFailed')); }
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
    setProblem(null);
    if (gameState?.state === 'vanilla') await generateShared(seed);
    else setPendingSeed(seed);
  };

  /** The Fog data has to be ready before a world that uses it is created; the check is quick when it already is. */
  const generateShared = async (seed: LoadedSeed) => {
    if (seed.settings.fog.enabled) {
      setBusy(true);
      setWorkLabel('preparingFog');
      try {
        const prepared = await api.prepareWorldData(config.gamePath);
        if (!prepared.ok) { setProblem({ code: prepared.code, detail: prepared.userMessage }); setBusy(false); return; }
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
      return result.ok ? result.text : t(`app.spoiler.${result.code === 'SPOILER_NONE' || result.code === 'SPOILER_UNAVAILABLE' ? result.code : 'unreadable'}`);
    } catch { return t('app.spoiler.unreadable'); }
  };

  /** Export support bundle for one world of the history: the result is announced in the status line, a refusal shows as an error. */
  const bundleOf = async (entry: RunHistoryEntry) => {
    setProblem(null);
    const outcome = await exportSupportBundle(config.gamePath, entry.id, (key, params) => t(key as never, params as never) as unknown as string);
    if (outcome.kind === 'refused') setProblem({ code: outcome.code });
    else setMessage(outcome.kind === 'saved' ? t('support.ui.saved', { name: outcome.name, count: outcome.count, size: outcome.size }) : outcome.kind === 'cancelled' ? t('support.ui.cancelled') : t('support.ui.failed'));
  };

  const exportPreset = async () => {
    setPresetProblems([]);
    try {
      setPresetText(JSON.stringify(await api.exportPreset({ ...config, masterSeed: 'preset' }), null, 2));
      setMessage(t('app.preset.exported'));
    } catch (error) { reportError(error); }
  };

  const importPreset = async () => {
    setPresetProblems([]);
    try {
      const result = await api.importPreset(presetText);
      if (!result.ok) { setPresetProblems(result.problems); return; }
      const { format: _format, presetVersion: _version, masterSeed: _ignoredSeed, ...settings } = result.preset;
      setConfig((current) => ({ ...current, ...settings }));
      setMessage(t('app.preset.applied'));
    } catch (error) { reportError(error); }
  };

  const resetSettings = () => {
    setConfig((current) => ({ ...initialConfig, gamePath: current.gamePath }));
    setPresetProblems([]);
    setMessage(t('app.preset.resetDone'));
  };

  const previewPlan = async () => {
    setBusy(true);
    setProgressStage('');
    setProblem(null);
    try {
      setPlan(await api.plan({ ...config, masterSeed: lastRun?.masterSeed ?? 'preview' }));
      setMessage(t('app.message.planDone'));
    } catch (error) {
      reportError(error);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="app-shell">
      <header className="hero">
        <div className="hero-brand"><div className="eyebrow">{t('app.header.eyebrow')}</div><h1>{t('app.header.title')}</h1><p>{t('app.header.tagline')}</p></div>
        <div className="hero-side">
          <div className={`status ${backendOnline ? 'online' : 'offline'}`}><span /> {backendOnline === null ? t('app.status.starting') : backendOnline ? t('app.status.ready') : t('app.status.offline')}</div>
          <LanguageSelect />
          <img className="hero-ludos" src={ludos} alt="" aria-hidden="true" draggable={false} />
        </div>
      </header>

      <main>
        {settingsWarning && <div className="notice">{settingsWarning}</div>}
        {startupError && <div className="notice error">{t('app.startupError.text')}<details><summary>{t('app.startupError.details')}</summary>{startupError}</details></div>}
        <section className="card setup-card">
          <div className="section-title"><span>01</span><h2>{t('app.sections.game')}</h2></div>
          <Readiness
            gamePath={config.gamePath}
            backendReady={backendOnline === true}
            worldNeeded={config.fog.enabled}
            busy={busy}
            refreshKey={refreshKey}
            onPath={(path) => { setConfig((current) => ({ ...current, gamePath: path })); setGameState(null); setGeneration(null); setProblem(null); }}
            onState={setGameState}
          />
          <div className="inline-options">
            <Toggle
              label={t('app.options.includeDlc.label')} checked={config.output.includeDlc || config.fog.enabled} inactive={config.fog.enabled}
              hint={config.fog.enabled ? t('app.options.includeDlc.fogHint') : t('app.options.includeDlc.hint')}
              onChange={(value) => setConfig((current) => ({ ...current, output: { ...current.output, includeDlc: value } }))}
            />
            <Toggle label={t('app.options.spoilerLog')} checked={config.output.generateSpoilerLog} onChange={(value) => setConfig({ ...config, output: { ...config.output, generateSpoilerLog: value } })} />
          </div>
          <p className="engine-note dlc-note">{config.fog.enabled ? t('app.options.includeDlc.fogNote') : config.output.includeDlc ? t('app.options.includeDlc.onNote') : t('app.options.includeDlc.offNote')}</p>
          <details className="log-panel"><summary>{t('app.preset.summary')}</summary>
            <textarea className="run-logs" aria-label={t('app.preset.aria')} rows={8} value={presetText} onChange={(event) => setPresetText(event.target.value)} placeholder={t('app.preset.placeholder')} />
            <div className="run-actions"><button className="secondary" onClick={exportPreset}>{t('app.preset.export')}</button><button className="secondary" onClick={importPreset} disabled={!presetText.trim()}>{t('app.preset.apply')}</button><button className="secondary" onClick={resetSettings}>{t('app.preset.reset')}</button></div>
            {presetProblems.length > 0 && <div className="notice error" role="alert">{t('app.preset.problemsLead')}<ul>{presetProblems.map((problem) => <li key={problem} title={problem}>{problemText(problem)}</li>)}</ul></div>}
          </details>
        </section>

        <div className="grid-3">
          <section className={`card engine-card ${config.items.enabled ? '' : 'disabled-card'}`}>
            <div className="engine-heading"><div><span className="engine-index">{t('settings.items.index')}</span><h2>{t('settings.items.title')}</h2></div><input type="checkbox" checked={config.items.enabled} onChange={(event) => setItem('enabled', event.target.checked)} /></div>
            <div className="option-group"><h3>{t('settings.items.groups.main')}</h3>
              <Toggle label={t('settings.items.worldItems.label')} checked={config.items.randomizeWorldItems} onChange={(value) => setItem('randomizeWorldItems', value)} />
              <Toggle label={t('settings.items.progressionLogic.label')} checked={config.items.progressionLogic} onChange={(value) => setItem('progressionLogic', value)} />
              <Toggle label={t('settings.items.earlyWeapon.label')} checked={config.items.guaranteeEarlyWeapon} onChange={(value) => setItem('guaranteeEarlyWeapon', value)} />
              <Toggle label={t('settings.items.balancedEarlyLoot.label')} checked={config.items.balancedEarlyLoot} onChange={(value) => setItem('balancedEarlyLoot', value)} />
              <Toggle label={t('settings.items.startingClass.label')} checked={config.items.randomizeStartingClass} onChange={(value) => setItem('randomizeStartingClass', value)} />
              <Toggle label={t('settings.items.startingEquipment.label')} checked={config.items.randomizeStartingEquipment} onChange={(value) => setItem('randomizeStartingEquipment', value)} />
              <Toggle label={t('settings.items.npcGifts.label')} checked={config.items.randomizeGifts} onChange={(value) => setItem('randomizeGifts', value)} />
              <Toggle label={t('settings.items.enemyDrops.label')} checked={config.items.randomizeEnemyDrops} onChange={(value) => setItem('randomizeEnemyDrops', value)} />
              <Toggle label={t('settings.items.guaranteedDrops.label')} checked={config.items.guaranteedEnemyDrops} onChange={(value) => setItem('guaranteedEnemyDrops', value)} />
              <Toggle label={t('settings.items.shops.label')} checked={config.items.randomizeShops} onChange={(value) => setItem('randomizeShops', value)} />
            </div>
            <div className="option-group"><h3>{t('settings.items.groups.legacy')}</h3>
              <Toggle inactive={!config.items.randomizeWorldItems} label={t('settings.items.fashionSouls.label')} hint={t('settings.items.fashionSouls.hint')} checked={config.items.fashionSouls} onChange={(value) => setItem('fashionSouls', value)} />
              <label className="field-label">{t('settings.items.soulItems.label')}</label>
              <select value={config.items.soulItems} disabled={!(config.items.randomizeWorldItems || config.items.randomizeGifts || config.items.randomizeEnemyDrops)} title={t('settings.items.soulItems.hint')} onChange={(event) => setItem('soulItems', event.target.value as SoulItemsMode)}><option value="shuffled">{t('settings.items.soulItems.options.shuffled')}</option><option value="replaced">{t('settings.items.soulItems.options.replaced')}</option><option value="transposed">{t('settings.items.soulItems.options.transposed')}</option></select>
              <label className="field-label">{t('settings.items.startingItems.label')}</label>
              <select value={config.items.startingItems} disabled={!config.items.randomizeStartingEquipment} title={t('settings.items.startingItems.hint')} onChange={(event) => setItem('startingItems', event.target.value as StartingItemsMode)}><option value="shield-1h">{t('settings.items.startingItems.options.shield-1h')}</option><option value="shield-2h">{t('settings.items.startingItems.options.shield-2h')}</option><option value="mixed-pool">{t('settings.items.startingItems.options.mixed-pool')}</option></select>
              <Toggle label={t('settings.items.laundromat.label')} hint={t('settings.items.laundromat.hint')} checked={config.items.laundromat ?? false} onChange={(value) => setItem('laundromat', value)} />
            </div>
            <div className="option-group"><h3>{t('settings.items.groups.keyItems')}</h3>
              <label className="field-label">{t('settings.items.keyPlacement.label')}</label>
              <select value={config.items.keyPlacement ?? 'vanilla'} title={t('settings.items.keyPlacement.hint')} onChange={(event) => setKeyPlacement(event.target.value as KeyPlacementMode)}><option value="vanilla">{t('settings.items.keyPlacement.options.vanilla')}</option><option value="shuffled">{t('settings.items.keyPlacement.options.shuffled')}</option><option value="race">{t('settings.items.keyPlacement.options.race')}</option><option value="race-plus">{t('settings.items.keyPlacement.options.race-plus')}</option></select>
              <small className="field-note">{(config.items.keyPlacement ?? 'vanilla') === 'vanilla'
                ? t('settings.items.keyNotes.vanilla')
                : !config.items.randomizeWorldItems
                  ? t('settings.items.keyNotes.needsWorld')
                  : config.items.keyPlacement === 'shuffled'
                    ? t('settings.items.keyNotes.shuffled')
                    : config.items.keyPlacement === 'race'
                      ? t('settings.items.keyNotes.race')
                      : t('settings.items.keyNotes.racePlus')}</small>
              <Toggle inactive={(config.items.keyPlacement ?? 'vanilla') === 'vanilla'} label={t('settings.items.lordvessel.label')} hint={t('settings.items.lordvessel.hint')} checked={config.items.randomizeLordvessel ?? false} onChange={(value) => setItem('randomizeLordvessel', value)} />
              <Toggle inactive={(config.items.keyPlacement ?? 'vanilla') === 'vanilla'} label={t('settings.items.lordSouls.label')} hint={t('settings.items.lordSouls.hint')} checked={config.items.randomizeLordSouls ?? false} onChange={(value) => setItem('randomizeLordSouls', value)} />
            </div>
            <div className="option-group"><h3>{t('settings.items.groups.difficulty')}</h3>
              <label className="field-label">{t('settings.items.difficulty.label')}</label>
              <select value={config.items.itemDifficulty ?? 'fair'} disabled={config.items.keyPlacement === 'race-plus'} title={t('settings.items.difficulty.hint')} onChange={(event) => setItem('itemDifficulty', event.target.value as ItemDifficultyMode)}><option value="fair">{t('settings.items.difficulty.options.fair')}</option><option value="unfair">{t('settings.items.difficulty.options.unfair')}</option><option value="very-unfair">{t('settings.items.difficulty.options.very-unfair')}</option></select>
              <small className="field-note">{config.items.keyPlacement === 'race-plus'
                ? t('settings.items.difficultyNotes.racePlus')
                : (config.items.itemDifficulty ?? 'fair') === 'fair'
                  ? t('settings.items.difficultyNotes.fair')
                  : config.items.itemDifficulty === 'unfair'
                    ? t('settings.items.difficultyNotes.unfair')
                    : t('settings.items.difficultyNotes.veryUnfair')}</small>
            </div>
          </section>

          <section className={`card engine-card ${config.enemies.enabled ? '' : 'disabled-card'}`}>
            <div className="engine-heading"><div><span className="engine-index">{t('settings.enemies.index')}</span><h2>{t('settings.enemies.title')}</h2></div><input type="checkbox" checked={config.enemies.enabled} onChange={(event) => setEnemy('enabled', event.target.checked)} /></div>
            <div className="option-group"><h3>{t('settings.enemies.groups.main')}</h3>
              <Toggle label={t('settings.enemies.regular.label')} checked={config.enemies.randomizeRegularEnemies} onChange={(value) => setEnemy('randomizeRegularEnemies', value)} />
              <Toggle label={t('settings.enemies.bosses.label')} checked={config.enemies.randomizeBosses} onChange={(value) => setEnemy('randomizeBosses', value)} />
              <label className="field-label">{t('settings.enemies.scaling.label')}</label><select value={config.enemies.scaling} onChange={(event) => setEnemy('scaling', event.target.value as RandomizerConfig['enemies']['scaling'])}><option value="vanilla">{t('settings.enemies.scaling.options.vanilla')}</option><option value="area">{t('settings.enemies.scaling.options.area')}</option><option value="progressive">{t('settings.enemies.scaling.options.progressive')}</option></select>
            </div>
            <div className="option-group"><h3>{t('settings.enemies.groups.placement')}</h3>
              <label className="field-label">{t('settings.enemies.algorithm.label')}</label>
              <select value={config.enemies.placement.algorithm} disabled={!config.enemies.randomizeRegularEnemies} title={t('settings.enemies.algorithm.hint')} onChange={(event) => setPlacementAlgorithm(event.target.value as EnemyPlacementAlgorithm)}><option value="sampling-v1">{t('settings.enemies.algorithm.options.sampling-v1')}</option><option value="permutation-v1">{t('settings.enemies.algorithm.options.permutation-v1')}</option></select>
              <small className="field-note">{config.enemies.placement.algorithm === 'sampling-v1'
                ? t('settings.enemies.algorithmNotes.sampling')
                : t('settings.enemies.algorithmNotes.permutation')}</small>
              {config.enemies.placement.algorithm === 'sampling-v1' && (
                <>
              <RangeOption label={t('settings.enemies.replacementChance.label')} hint={t('settings.enemies.replacementChance.hint')} value={config.enemies.placement.replacementChance} inactive={!config.enemies.randomizeRegularEnemies || config.enemies.placement.algorithm !== 'sampling-v1'} onChange={(value) => setPlacement('replacementChance', value)} />
              <label className="field-label">{t('settings.enemies.normalReplacement.label')}</label>
              <select value={config.enemies.placement.normalReplacement} disabled={!config.enemies.randomizeRegularEnemies || config.enemies.placement.algorithm !== 'sampling-v1'} title={t('settings.enemies.normalReplacement.hint')} onChange={(event) => setNormalReplacement(event.target.value as RandomizerConfig['enemies']['placement']['normalReplacement'])}><option value="none">{t('settings.enemies.normalReplacement.options.none')}</option><option value="normals">{t('settings.enemies.normalReplacement.options.normals')}</option><option value="bosses">{t('settings.enemies.normalReplacement.options.bosses')}</option><option value="both">{t('settings.enemies.normalReplacement.options.both')}</option></select>
              {config.enemies.placement.algorithm === 'sampling-v1' && config.enemies.placement.normalReplacement === 'both' && (
                <RangeOption label={t('settings.enemies.bossChanceNormal.label')} hint={t('settings.enemies.bossChanceNormal.hint')} value={config.enemies.placement.bossChanceInNormalSlots} inactive={!config.enemies.randomizeRegularEnemies} onChange={(value) => setPlacement('bossChanceInNormalSlots', value)} />
              )}
              {config.enemies.placement.algorithm === 'sampling-v1' && (config.enemies.placement.normalReplacement === 'bosses' || config.enemies.placement.normalReplacement === 'both') && (
                <>
                  <RangeOption label={t('settings.enemies.roamingSouls.label')} hint={t('settings.enemies.roamingSouls.hint')} value={config.enemies.placement.roamingBossSouls} inactive={!config.enemies.randomizeRegularEnemies} onChange={(value) => setPlacement('roamingBossSouls', value)} />
                  <Toggle inactive={!config.enemies.randomizeRegularEnemies} label={t('settings.enemies.roamingDead.label')} hint={t('settings.enemies.roamingDead.hint')} checked={config.enemies.placement.roamingBossesStayDead} onChange={(value) => setPlacement('roamingBossesStayDead', value)} />
                </>
              )}
              <label className="field-label">{t('settings.enemies.fit.label')}</label>
              <select value={config.enemies.placement.fit} disabled={!config.enemies.randomizeRegularEnemies || config.enemies.placement.algorithm !== 'sampling-v1'} title={t('settings.enemies.fit.hint')} onChange={(event) => setPlacement('fit', event.target.value as RandomizerConfig['enemies']['placement']['fit'])}><option value="fits">{t('settings.enemies.fit.options.fits')}</option><option value="anywhere">{t('settings.enemies.fit.options.anywhere')}</option><option value="anywhere-except-npcs">{t('settings.enemies.fit.options.anywhere-except-npcs')}</option></select>
              <label className="field-label">{t('settings.enemies.difficultyMode.label')}</label>
              <select value={config.enemies.placement.difficultyMode} disabled={!config.enemies.randomizeRegularEnemies || config.enemies.placement.algorithm !== 'sampling-v1'} title={t('settings.enemies.difficultyMode.hint')} onChange={(event) => setPlacement('difficultyMode', event.target.value as RandomizerConfig['enemies']['placement']['difficultyMode'])}><option value="curve-easy-asylum">{t('settings.enemies.difficultyMode.options.curve-easy-asylum')}</option><option value="curve">{t('settings.enemies.difficultyMode.options.curve')}</option><option value="random-easy-asylum">{t('settings.enemies.difficultyMode.options.random-easy-asylum')}</option><option value="random">{t('settings.enemies.difficultyMode.options.random')}</option></select>
              <label className="field-label">{t('settings.enemies.strictness.label')}</label>
              <select value={config.enemies.placement.difficultyStrictness} disabled={!config.enemies.randomizeRegularEnemies || config.enemies.placement.algorithm !== 'sampling-v1' || !config.enemies.placement.difficultyMode.startsWith('curve')} title={t('settings.enemies.strictness.hint')} onChange={(event) => setPlacement('difficultyStrictness', event.target.value as RandomizerConfig['enemies']['placement']['difficultyStrictness'])}><option value="strict">{t('settings.enemies.strictness.options.strict')}</option><option value="loose">{t('settings.enemies.strictness.options.loose')}</option><option value="very-loose">{t('settings.enemies.strictness.options.very-loose')}</option></select>
              <label className="field-label">{t('settings.enemies.typeReplacement.label')}</label>
              <select value={config.enemies.placement.typeReplacement} disabled={!config.enemies.randomizeRegularEnemies || config.enemies.placement.algorithm !== 'sampling-v1'} title={t('settings.enemies.typeReplacement.hint')} onChange={(event) => setPlacement('typeReplacement', event.target.value as RandomizerConfig['enemies']['placement']['typeReplacement'])}><option value="enabled">{t('settings.enemies.typeReplacement.options.enabled')}</option><option value="enabled-except-roaming-bosses">{t('settings.enemies.typeReplacement.options.enabled-except-roaming-bosses')}</option><option value="disabled">{t('settings.enemies.typeReplacement.options.disabled')}</option></select>
              <label className="field-label">{t('settings.enemies.bossReplacement.label')}</label>
              <select value={config.enemies.placement.bossReplacement} disabled={!config.enemies.randomizeBosses || config.enemies.placement.algorithm !== 'sampling-v1'} title={t('settings.enemies.bossReplacement.hint')} onChange={(event) => setBossReplacement(event.target.value as RandomizerConfig['enemies']['placement']['bossReplacement'])}><option value="classic">{t('settings.enemies.bossReplacement.options.classic')}</option><option value="bosses">{t('settings.enemies.bossReplacement.options.bosses')}</option><option value="normals">{t('settings.enemies.bossReplacement.options.normals')}</option><option value="both">{t('settings.enemies.bossReplacement.options.both')}</option></select>
              {config.enemies.placement.algorithm === 'sampling-v1' && config.enemies.placement.bossReplacement === 'both' && (
                <RangeOption label={t('settings.enemies.bossChanceBoss.label')} hint={t('settings.enemies.bossChanceBoss.hint')} value={config.enemies.placement.bossChanceInBossSlots} inactive={!config.enemies.randomizeBosses} onChange={(value) => setPlacement('bossChanceInBossSlots', value)} />
              )}
              <Toggle inactive={!config.enemies.randomizeBosses || config.enemies.placement.algorithm !== 'sampling-v1' || (config.enemies.placement.bossReplacement !== 'bosses' && config.enemies.placement.bossReplacement !== 'both')} label={t('settings.enemies.uniqueBosses.label')} hint={t('settings.enemies.uniqueBosses.hint')} checked={config.enemies.placement.uniqueBosses} onChange={(value) => setPlacement('uniqueBosses', value)} />
              <EnemyPoolEditor gamePath={config.gamePath} active={config.enemies.randomizeRegularEnemies && config.enemies.placement.algorithm === 'sampling-v1'} fit={config.enemies.placement.fit} customPool={config.enemies.placement.customPool} onChange={(pool) => setConfig((current) => ({ ...current, enemies: { ...current.enemies, placement: { ...current.enemies.placement, customPool: pool, ...(pool !== null ? { hostileNpcs: false } : {}) } } }))} />
                </>
              )}
            </div>
            <div className="option-group"><h3>{t('settings.enemies.groups.legacy')}</h3>
              <Toggle inactive={!config.enemies.randomizeRegularEnemies} label={t('settings.enemies.mimics.label')} hint={t('settings.enemies.mimics.hint')} checked={config.enemies.replaceMimics} onChange={(value) => setEnemy('replaceMimics', value)} />
              {config.enemies.placement.algorithm === 'sampling-v1' && (config.enemies.placement.normalReplacement === 'normals' || config.enemies.placement.normalReplacement === 'both') && config.enemies.placement.customPool === null && (
                <Toggle inactive={!config.enemies.randomizeRegularEnemies} label={t('settings.enemies.hostileNpcs.label')} hint={t('settings.enemies.hostileNpcs.hint')} checked={config.enemies.placement.hostileNpcs} onChange={(value) => setPlacement('hostileNpcs', value)} />
              )}
              <Toggle inactive={!config.enemies.randomizeRegularEnemies} label={t('settings.enemies.mosquitoes.label')} hint={t('settings.enemies.mosquitoes.hint')} checked={config.enemies.replaceRespawningMosquitoes} onChange={(value) => setEnemy('replaceRespawningMosquitoes', value)} />
              <Toggle inactive={!config.enemies.randomizeRegularEnemies} label={t('settings.enemies.sameEnemy.label')} hint={t('settings.enemies.sameEnemy.hint')} checked={config.enemies.preventSameEnemy} onChange={(value) => setEnemy('preventSameEnemy', value)} />
              <Toggle inactive={!config.enemies.randomizeRegularEnemies} label={t('settings.enemies.pinwheel.label')} hint={t('settings.enemies.pinwheel.hint')} checked={config.enemies.pinwheelChaos} onChange={(value) => setEnemy('pinwheelChaos', value)} />
              <label className="field-label">{t('settings.enemies.gargoyle.label')}</label>
              <select value={config.enemies.secondGargoyle} disabled={!config.enemies.randomizeBosses} title={t('settings.enemies.gargoyle.hint')} onChange={(event) => setEnemy('secondGargoyle', event.target.value as SecondGargoyleMode)}><option value="replace">{t('settings.enemies.gargoyle.options.replace')}</option><option value="keep">{t('settings.enemies.gargoyle.options.keep')}</option><option value="single-boss">{t('settings.enemies.gargoyle.options.single-boss')}</option></select>
              <label className="field-label">{t('settings.enemies.gwyn.label')}</label>
              <select value={config.enemies.gwynNerf} disabled={!config.enemies.randomizeBosses} title={t('settings.enemies.gwyn.hint')} onChange={(event) => setEnemy('gwynNerf', event.target.value as GwynNerfMode)}><option value="strong">{t('settings.enemies.gwyn.options.strong')}</option><option value="medium">{t('settings.enemies.gwyn.options.medium')}</option><option value="none">{t('settings.enemies.gwyn.options.none')}</option></select>
            </div>
          </section>

          <section className={`card engine-card ${config.fog.enabled ? '' : 'disabled-card'}`}>
            <div className="engine-heading"><div><span className="engine-index">{t('settings.fog.index')}</span><h2>{t('settings.fog.title')}</h2><p className="engine-note">{t('settings.fog.note')}</p></div><input type="checkbox" checked={config.fog.enabled} onChange={(event) => setFog('enabled', event.target.checked)} /></div>
            <div className="option-group"><h3>{t('settings.fog.groups.entrances')}</h3>
              <Toggle label={t('settings.fog.traversable.label')} checked={config.fog.traversableFogs} onChange={(value) => setFog('traversableFogs', value)} />
              <Toggle label={t('settings.fog.bossFogs.label')} checked={config.fog.bossFogs} onChange={(value) => setFog('bossFogs', value)} />
              <Toggle label={t('settings.fog.warps.label')} checked={config.fog.areaWarps} onChange={(value) => setFog('areaWarps', value)} />
              <Toggle label={t('settings.fog.majorPvp.label')} checked={config.fog.majorPvpFogs} onChange={(value) => setFog('majorPvpFogs', value)} />
              <Toggle label={t('settings.fog.minorPvp.label')} checked={config.fog.minorPvpFogs} onChange={(value) => setFog('minorPvpFogs', value)} />
              <Toggle label={t('settings.fog.lordvesselGates.label')} checked={config.fog.lordvesselGates} onChange={(value) => setFog('lordvesselGates', value)} />
            </div>
            <div className="option-group"><h3>{t('settings.fog.groups.logic')}</h3>
              <Toggle label={t('settings.fog.randomStart.label')} checked={config.fog.randomStart} onChange={(value) => setFog('randomStart', value)} />
              <Toggle label={t('settings.fog.disconnected.label')} checked={config.fog.disconnectedFogs} onChange={(value) => setFog('disconnectedFogs', value)} />
              <Toggle label={t('settings.fog.noFall.label')} checked={config.fog.noFallBedOfChaos} onChange={(value) => setFog('noFallBedOfChaos', value)} />
              <Toggle label={t('settings.fog.scaling.label')} checked={config.fog.scaling} onChange={(value) => setFog('scaling', value)} />
              <Toggle label={t('settings.fog.glitched.label')} checked={config.fog.glitchedLogic} onChange={(value) => setFog('glitchedLogic', value)} />
              <Toggle label={t('settings.fog.requireLordSouls.label')} checked={config.fog.requireLordSouls} onChange={(value) => setFog('requireLordSouls', value)} />
              <Toggle label={t('settings.fog.pacifist.label')} checked={config.fog.pacifistMode} onChange={(value) => setFog('pacifistMode', value)} />
              <label className="field-label">{t('settings.fog.gameLanguage.label')}</label><select value={config.fog.language} title={t('settings.fog.gameLanguage.hint')} onChange={(event) => setFog('language', event.target.value as FogLanguage)}><option value="ENGLISH">{t('settings.fog.gameLanguage.options.ENGLISH')}</option><option value="FRENCH">{t('settings.fog.gameLanguage.options.FRENCH')}</option><option value="GERMAN">{t('settings.fog.gameLanguage.options.GERMAN')}</option><option value="ITALIAN">{t('settings.fog.gameLanguage.options.ITALIAN')}</option><option value="JAPANESE">{t('settings.fog.gameLanguage.options.JAPANESE')}</option><option value="KOREAN">{t('settings.fog.gameLanguage.options.KOREAN')}</option><option value="NSPANISH">{t('settings.fog.gameLanguage.options.NSPANISH')}</option><option value="POLISH">{t('settings.fog.gameLanguage.options.POLISH')}</option><option value="PORTUGUESE">{t('settings.fog.gameLanguage.options.PORTUGUESE')}</option><option value="RUSSIAN">{t('settings.fog.gameLanguage.options.RUSSIAN')}</option><option value="SCHINESE">{t('settings.fog.gameLanguage.options.SCHINESE')}</option><option value="SPANISH">{t('settings.fog.gameLanguage.options.SPANISH')}</option><option value="TCHINESE">{t('settings.fog.gameLanguage.options.TCHINESE')}</option></select>
            </div>
          </section>
        </div>

        <section className={`card run-card phase-${phase}`}>
          <div className="section-title"><span>02</span><h2>{phaseTitle(phase, t)}</h2></div>
          <p className="sr-only" role="status">{message || t('app.run.chooseSettings')}</p>
          {phase === 'working' && (
            <div className="progress-status">
              <span>{progressStage ? `${progressLabel(progressStage, t)}: ${Math.round(progress * 100)}%` : `${t(`app.work.${workLabel}`)}...`}</span>
              {progressStage ? <progress value={progress} max={1} aria-label={t('app.progressAria')} /> : <progress aria-label={t('app.progressAria')} />}
            </div>
          )}
          {phase !== 'working' && <p className="run-state">{stateText(gameState, t)}</p>}
          {problem && <ErrorNotice error={problem} />}

          {phase === 'ready' && (
            <>
              {notice && <div className="notice success apply-notice" role="status"><strong>{t(`app.outcome.${notice}.text`)}</strong><span>{t(`app.outcome.${notice}.sub`)}</span></div>}
              <SummaryChips settings={currentSettings} />
              <div className="run-actions">
                <button className="primary" onClick={() => void generateRun()} disabled={!config.gamePath || enabledCount === 0}>{t('app.run.randomize')}</button>
                <button className="secondary" onClick={() => setSeedPanelOpen((open) => !open)}>{t('common.loadSeed')}</button>
              </div>
            </>
          )}

          {phase === 'generated' && generation?.ok && (
            <>
              <div className="generated-status" role="status">
                <strong>{t('app.run.readyTitle')}</strong>
                <textarea className="run-logs seed-output" readOnly rows={2} aria-label={t('app.run.seedAria')} value={generation.seedCode} onFocus={(event) => event.currentTarget.select()} />
                <SummaryChips settings={generation.settings ?? currentSettings} />
                {generation.settings && isLoadedSeedModified(generation.settings, currentSettings) && <span className="stale-warning">{t('app.run.stale')}</span>}
              </div>
              <div className="run-actions">
                <button className="primary" onClick={() => void activate()}>{t('app.run.apply')}</button>
                <button className="secondary" onClick={() => void copySeed(generation.seedCode)}>{copied ? t('common.copied') : t('common.copySeed')}</button>
                <div className="menu-anchor">
                  <button className="secondary" aria-label={t('common.moreActions')} aria-haspopup="menu" aria-expanded={runMenuOpen} onClick={() => setRunMenuOpen((open) => !open)}>…</button>
                  {runMenuOpen && (
                    <div className="menu" role="menu">
                      <button role="menuitem" onClick={() => void generateRun()} disabled={!config.gamePath || enabledCount === 0}>{t('app.run.randomizeAgain')}</button>
                      <button role="menuitem" onClick={() => { setRunMenuOpen(false); setSeedPanelOpen(true); }}>{t('common.loadSeed')}</button>
                      <button role="menuitem" disabled={!generation.historyId} onClick={() => { setRunMenuOpen(false); if (generation.historyId) setHintTarget({ id: generation.historyId, exportFirst: false }); }}>{t('hints.action.view')}</button>
                    </div>
                  )}
                </div>
              </div>
            </>
          )}

          {phase === 'applied' && (
            <>
              {notice && <div className="notice success apply-notice" role="status"><strong>{t(`app.outcome.${notice}.text`)}</strong><span>{t(`app.outcome.${notice}.sub`)}</span></div>}
              {launchMessage && <div className="notice" role="status">{t(`app.launch.${launchMessage}`)}</div>}
              {pendingSeed && <div className="notice" role="status">{t('app.run.pendingRestore')}</div>}
              <div className="run-actions">
                <button className="primary" onClick={() => void launch()} disabled={!config.gamePath}>{t('common.launchGame')}</button>
                <button className="secondary" onClick={restore}>{pendingSeed ? t('app.run.restoreShared') : t('app.run.restore')}</button>
                {appliedEntry && <button className="secondary" onClick={() => void copySeed(appliedEntry.seedCode)} title={appliedEntry.seedCode}>{copied ? t('common.copied') : t('app.run.copySeedShort', { seed: shortSeedCode(appliedEntry.seedCode) })}</button>}
                <div className="menu-anchor">
                  <button className="secondary" aria-label={t('common.moreActions')} aria-haspopup="menu" aria-expanded={runMenuOpen} onClick={() => setRunMenuOpen((open) => !open)}>…</button>
                  {runMenuOpen && (
                    <div className="menu" role="menu">
                      <button role="menuitem" onClick={() => { setRunMenuOpen(false); setSeedPanelOpen(true); }}>{t('common.loadSeed')}</button>
                      <button role="menuitem" disabled={!appliedEntry?.hintsAvailable} onClick={() => { setRunMenuOpen(false); if (appliedEntry) setHintTarget({ id: appliedEntry.id, exportFirst: false }); }}>{t('hints.action.view')}</button>
                    </div>
                  )}
                </div>
              </div>
            </>
          )}

          {phase === 'repair' && (
            <>
              {pendingSeed && <div className="notice" role="status">{t('app.run.pendingRepair')}</div>}
              <div className="run-actions"><button className="primary" onClick={restore}>{t('app.run.restore')}</button></div>
            </>
          )}

          {phase === 'blocked' && pendingSeed && <div className="notice" role="status">{t('app.run.pendingBlocked')}</div>}
          {phase === 'blocked' && gameState?.state === 'version-changed' && <div className="run-actions"><button className="primary" onClick={restore}>{t('app.run.restore')}</button></div>}

          {seedPanelOpen && phase !== 'working' && <SeedPanel busy={busy} onLoaded={(seed) => void loadSeed(seed)} onClose={() => setSeedPanelOpen(false)} />}

          <RunHistoryPanel
            entries={history} gameState={gameState} busy={busy}
            onApply={(entry) => void applyPackage(entry.packagePath)} onRegenerate={(entry) => void regenerate(entry)}
            onCopy={(entry) => void copySeed(entry.seedCode)} onLaunch={() => void launch()}
            onDelete={(entry) => void deleteEntry(entry)} onSpoiler={spoilerOf} onHints={(entry, exportFirst) => setHintTarget({ id: entry.id, exportFirst })} onDiagnostics={(entry) => setDiagnosticsTarget(entry.id)} onSupportBundle={(entry) => void bundleOf(entry)}
          />
          <details className="log-panel" open={logs.length > 0}><summary>{t('app.advanced.title')}</summary>
            <div className="run-actions">
              <button className="secondary" onClick={previewPlan} disabled={busy}>{t('app.advanced.preview')}</button>
              <button className="secondary" onClick={importPristine} disabled={busy || !config.gamePath}>{t('app.advanced.checkAgain')}</button>
            </div>
            {lastRun && <p>{t('app.advanced.internalSeed', { seed: lastRun.masterSeed, profile: lastRun.profile })}</p>}
            {generation?.ok && <p>{t('app.advanced.files', { path: generation.packagePath, count: generation.manifest.files.length })}</p>}
            <details className="log-panel" onToggle={(event) => setDiagnosticsOpen((event.currentTarget as HTMLDetailsElement).open)}><summary>{t('diagnostics.panel.title')}</summary>{diagnosticsOpen && <DiagnosticsPanel gamePath={config.gamePath} refreshKey={`${lastOperationId ?? ''}|${history.length}|${gameState?.state ?? ''}`} />}</details>
            <SmokeTestPanel busy={busy} onCreate={(seed) => void loadSeed(seed)} />
            <pre className="run-logs" aria-label={t('app.advanced.detailsAria')}>{logs.length ? logs.join('\n') : t('app.advanced.nothing')}</pre>
          </details>
        </section>

        {plan && <section className="card plan-card"><div className="section-title"><span>02</span><h2>{t('app.plan.title')}</h2></div>{plan.behaviors && plan.behaviors.length > 0 && <ul className="plan-behaviors" aria-label={t('app.plan.behaviorsAria')}>{plan.behaviors.map((line) => <li key={line.labelKey ?? line.label}><strong>{behaviorLabel(t, line)}</strong><span>{behaviorValue(t, line)}</span></li>)}</ul>}<div className="seed-grid">{plan.stages.map((stage) => <div key={stage.engine}><span>{t(`plan.engines.${stage.engine}`)}</span><strong>{stage.enabled ? stage.seed : t('app.plan.disabled')}</strong></div>)}</div><div className="engine-status-list">{plan.engineStatus.map((status) => <div key={status.name} className="engine-status"><strong>{t(`plan.engines.${status.name}`)}</strong><span className={`pill ${status.ready ? 'ready' : ''}`}>{t(`plan.modes.${status.mode}`)}</span><p>{t(`plan.details.${status.mode === 'executable' && status.ready ? 'ready' : status.mode}`)}</p></div>)}</div></section>}

        {diagnosticsTarget && <DiagnosticsPanel gamePath={config.gamePath} worldId={diagnosticsTarget} onClose={() => setDiagnosticsTarget(null)} />}
        {hintTarget && <HintSheetPanel entryId={hintTarget.id} exportFirst={hintTarget.exportFirst} onClose={() => setHintTarget(null)} />}

        <section className="card legal-card">
          <div><div className="eyebrow">{t('app.legal.eyebrow')}</div><h2>{t('app.legal.title')}</h2></div>
          <p>{t('app.legal.copyright')}</p>
          <p>{t('app.legal.disclaimer')}</p>
          <a href="https://www.gnu.org/licenses/gpl-3.0.html" target="_blank" rel="noreferrer">{t('app.legal.readLicense')}</a>
        </section>
      </main>
    </div>
  );
}
