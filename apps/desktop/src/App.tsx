import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import { ErrorNotice, useProblemText, type ErrorInfo } from './ErrorNotice';
import { behaviorLabel, behaviorValue } from './planText';
import { HintSheetPanel } from './HintSheetPanel';
import { DiagnosticsPanel } from './DiagnosticsPanel';
import { exportSupportBundle } from './supportBundle';
import { Readiness } from './Readiness';
import { SummaryChips, shortSeedCode, type LoadedSeed } from './SeedPanel';
import { RunHistoryPanel } from './RunHistoryPanel';
import { SmokeTestPanel } from './SmokeTestPanel';
import { DEFAULT_GENERATION_SETTINGS, applySetting, deriveRunPhase, isLoadedSeedModified, joinConfig, sectionsWithCustomSettings, splitConfig } from '@ds1tr/contracts';
import type { RunPhase, RunHistoryEntry, GameStateReport, GenerationResult, PipelinePlan, RandomizerConfig, SectionId } from '@ds1tr/contracts';
import { api } from './api';
import logo from './assets/branding/app-mark.png';
import { HelpPanel, HelpProvider } from './ui/HelpPanel';
import { AboutView, AppSettingsView, PresetsView, SeedCodeView, TransferView } from './ui/OtherViews';
import { UpdateNotice } from './ui/UpdatePanel';
import { useUpdates } from './ui/useUpdates';
import { useDevTools } from './ui/devTools';
import { AdvancedView, BasicView, ModeSwitch, type SettingsMode } from './ui/SettingsViews';
import { Sidebar, type ViewId } from './ui/Sidebar';

// The same defaults the smoke-test worlds are built from (contracts): one definition, so the screen and the tests cannot drift apart.
// The master seed stays empty on purpose: it is made when Randomize is clicked and travels inside the Seed Code.
const initialConfig: RandomizerConfig = {
  ...joinConfig(structuredClone(DEFAULT_GENERATION_SETTINGS), '', '', true),
};

/** Which view of the settings is shown is a preference of this window, not part of a world: it is kept next to the language, never in the settings, a preset or a Seed Code. */
const MODE_STORAGE_KEY = 'ds1tr.settingsMode';
const readMode = (): SettingsMode => {
  try { return window.localStorage.getItem(MODE_STORAGE_KEY) === 'advanced' ? 'advanced' : 'basic'; } catch { return 'basic'; }
};
const writeMode = (mode: SettingsMode): void => {
  try { window.localStorage.setItem(MODE_STORAGE_KEY, mode); } catch { /* the preference is a convenience */ }
};

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
  const devTools = useDevTools();
  const problemText = useProblemText();
  const [config, setConfig] = useState(initialConfig);
  const [backendOnline, setBackendOnline] = useState<boolean | null>(null);
  const [startupError, setStartupError] = useState<string | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const [plan, setPlan] = useState<PipelinePlan | null>(null);
  const [busy, setBusy] = useState(false);
  // Application updates (docs/AUTO_UPDATE.md): shown in App settings and About, and as one line at the top while a version is waiting.
  const updates = useUpdates();
  const [message, setMessage] = useState('');
  const [gameState, setGameState] = useState<GameStateReport | null>(null);
  const [generation, setGeneration] = useState<GenerationResult | null>(null);
  const [progress, setProgress] = useState(0);
  const [progressStage, setProgressStage] = useState('');
  const [logs, setLogs] = useState<string[]>([]);
  const [problem, setProblem] = useState<ErrorInfo | null>(null);
  const [presetText, setPresetText] = useState('');
  const [presetProblems, setPresetProblems] = useState<string[]>([]);
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
  const [view, setView] = useState<ViewId>('game');
  const [mode, setModeState] = useState<SettingsMode>(readMode);
  /** The sections the advanced view opens when it is reached from the basic notice about custom values. */
  const [focusSections, setFocusSections] = useState<SectionId[]>([]);
  const [viewMessage, setViewMessage] = useState('');
  const setMode = (next: SettingsMode) => { setModeState(next); writeMode(next); };

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

  /** Every control of the settings goes through the registry: the same rules apply in the basic and in the advanced view (packages/contracts/src/settingsRegistry.ts). */
  const changeSetting = useCallback((id: string, value: unknown) => setConfig((current) => applySetting(current, id, value)), []);
  const showAdvanced = () => { setFocusSections(sectionsWithCustomSettings(config)); setMode('advanced'); };


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
    setView('game');
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
      setMessage(t('app.preset.exported')); setViewMessage(t('app.preset.exported'));
    } catch (error) { reportError(error); }
  };

  const importPreset = async () => {
    setPresetProblems([]);
    try {
      const result = await api.importPreset(presetText);
      if (!result.ok) { setPresetProblems(result.problems); return; }
      const { format: _format, presetVersion: _version, masterSeed: _ignoredSeed, ...settings } = result.preset;
      setConfig((current) => ({ ...current, ...settings }));
      setMessage(t('app.preset.applied')); setViewMessage(t('app.preset.applied'));
    } catch (error) { reportError(error); }
  };

  const resetSettings = () => {
    setConfig((current) => ({ ...initialConfig, gamePath: current.gamePath }));
    setPresetProblems([]);
    setMessage(t('app.preset.resetDone')); setViewMessage(t('app.preset.resetDone'));
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
  const currentSeed = generation?.ok ? generation.seedCode : (appliedEntry?.seedCode ?? lastRun?.seedCode ?? null);

  const runPanel = (
    <section className={`card run-card phase-${phase}`} aria-labelledby="run-title">
      <div className="section-title"><h2 id="run-title">{phaseTitle(phase, t)}</h2></div>
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
          <div className="run-actions stack">
            <button className="primary big" onClick={() => void generateRun()} disabled={!config.gamePath || enabledCount === 0}><span>{t('app.run.randomize')}</span><span aria-hidden="true">→</span></button>
            <button className="secondary" onClick={() => setView('seed')}>{t('common.loadSeed')}</button>
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
                  <button role="menuitem" onClick={() => { setRunMenuOpen(false); setView('seed'); }}>{t('common.loadSeed')}</button>
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
                  <button role="menuitem" onClick={() => { setRunMenuOpen(false); setView('seed'); }}>{t('common.loadSeed')}</button>
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
    </section>
  );

  const toolsCard = (
    <section className="card tools-card">
      <h2>{t('app.advanced.title')}</h2>
      <div className="run-actions">
        <button className="secondary" onClick={previewPlan} disabled={busy}>{t('app.advanced.preview')}</button>
        <button className="secondary" onClick={importPristine} disabled={busy || !config.gamePath}>{t('app.advanced.checkAgain')}</button>
      </div>
      {lastRun && <p>{t('app.advanced.internalSeed', { seed: lastRun.masterSeed, profile: lastRun.profile })}</p>}
      {generation?.ok && <p>{t('app.advanced.files', { path: generation.packagePath, count: generation.manifest.files.length })}</p>}
      <details className="log-panel" onToggle={(event) => setDiagnosticsOpen((event.currentTarget as HTMLDetailsElement).open)}><summary>{t('diagnostics.panel.title')}</summary>{diagnosticsOpen && <DiagnosticsPanel gamePath={config.gamePath} refreshKey={`${lastOperationId ?? ''}|${history.length}|${gameState?.state ?? ''}`} />}</details>
      {devTools && <SmokeTestPanel busy={busy} onCreate={(seed) => { setView('game'); void loadSeed(seed); }} />}
      <pre className="run-logs" aria-label={t('app.advanced.detailsAria')}>{logs.length ? logs.join('\n') : t('app.advanced.nothing')}</pre>
      {plan && (
        <div className="plan-card">
          <h3>{t('app.plan.title')}</h3>
          {plan.behaviors && plan.behaviors.length > 0 && <ul className="plan-behaviors" aria-label={t('app.plan.behaviorsAria')}>{plan.behaviors.map((line) => <li key={line.labelKey ?? line.label}><strong>{behaviorLabel(t, line)}</strong><span>{behaviorValue(t, line)}</span></li>)}</ul>}
          <div className="seed-grid">{plan.stages.map((stage) => <div key={stage.engine}><span>{t(`plan.engines.${stage.engine}`)}</span><strong>{stage.enabled ? stage.seed : t('app.plan.disabled')}</strong></div>)}</div>
          <div className="engine-status-list">{plan.engineStatus.map((status) => <div key={status.name} className="engine-status"><strong>{t(`plan.engines.${status.name}`)}</strong><span className={`pill ${status.ready ? 'ready' : ''}`}>{t(`plan.modes.${status.mode}`)}</span><p>{t(`plan.details.${status.mode === 'executable' && status.ready ? 'ready' : status.mode}`)}</p></div>)}</div>
        </div>
      )}
    </section>
  );

  return (
    <HelpProvider>
      <div className="app-frame">
        <Sidebar view={view} onView={(next) => { setView(next); setViewMessage(''); }} backend={backendOnline} version={__APP_VERSION__} logo={logo} />
        <main className="content" id="main">
          {settingsWarning && <div className="notice">{settingsWarning}</div>}
          <UpdateNotice updates={updates} hidden={view === 'settings' || view === 'about'} />
          {startupError && <div className="notice error">{t('app.startupError.text')}<details><summary>{t('app.startupError.details')}</summary>{startupError}</details></div>}

          {view === 'game' && (
            <>
              <header className="view-head with-switch">
                <div><h1>{t('app.nav.game')}</h1><p>{t('app.views.game.note')}</p></div>
                <ModeSwitch mode={mode} onMode={setMode} />
              </header>
              <section className="card setup-card" aria-label={t('app.sections.game')}>
                <Readiness
                  gamePath={config.gamePath}
                  backendReady={backendOnline === true}
                  worldNeeded={config.fog.enabled}
                  busy={busy}
                  refreshKey={refreshKey}
                  onPath={(path) => { setConfig((current) => ({ ...current, gamePath: path })); setGameState(null); setGeneration(null); setProblem(null); }}
                  onState={setGameState}
                />
              </section>
              <div className="game-grid">
                <div className="settings-column">
                  {mode === 'basic'
                    ? <BasicView config={config} onChange={changeSetting} onShowAdvanced={showAdvanced} />
                    : <AdvancedView config={config} onChange={changeSetting} onBackToBasic={() => setMode('basic')} openSections={focusSections} />}
                </div>
                <div className="side-column">
                  {runPanel}
                  <HelpPanel config={config} alwaysVisible={mode === 'advanced'} />
                </div>
              </div>
              <section className="card history-card">
                <RunHistoryPanel
                  entries={history} gameState={gameState} busy={busy}
                  onApply={(entry) => void applyPackage(entry.packagePath)} onRegenerate={(entry) => void regenerate(entry)}
                  onCopy={(entry) => void copySeed(entry.seedCode)} onLaunch={() => void launch()}
                  onDelete={(entry) => void deleteEntry(entry)} onSpoiler={spoilerOf} onHints={(entry, exportFirst) => setHintTarget({ id: entry.id, exportFirst })} onDiagnostics={(entry) => setDiagnosticsTarget(entry.id)} onSupportBundle={(entry) => void bundleOf(entry)}
                />
              </section>
            </>
          )}

          {view === 'presets' && <PresetsView settings={currentSettings} onResetDefaults={resetSettings} onGoGame={() => setView('game')} message={viewMessage} />}
          {view === 'transfer' && (
            <TransferView
              text={presetText} onText={setPresetText} onExport={() => void exportPreset()} onImport={() => void importPreset()} message={viewMessage}
              problems={presetProblems.length > 0 ? <div className="notice error" role="alert">{t('app.preset.problemsLead')}<ul>{presetProblems.map((problem) => <li key={problem} title={problem}>{problemText(problem)}</li>)}</ul></div> : null}
            />
          )}
          {view === 'seed' && <SeedCodeView current={currentSeed} busy={busy} copied={copied} onCopy={(code) => void copySeed(code)} onLoaded={(seed) => void loadSeed(seed)} />}
          {view === 'settings' && <AppSettingsView updates={updates}>{toolsCard}</AppSettingsView>}
          {view === 'about' && <AboutView version={__APP_VERSION__} logo={logo} updates={updates} />}

          {diagnosticsTarget && <DiagnosticsPanel gamePath={config.gamePath} worldId={diagnosticsTarget} onClose={() => setDiagnosticsTarget(null)} />}
          {hintTarget && <HintSheetPanel entryId={hintTarget.id} exportFirst={hintTarget.exportFirst} onClose={() => setHintTarget(null)} />}
        </main>
      </div>
    </HelpProvider>
  );
}
