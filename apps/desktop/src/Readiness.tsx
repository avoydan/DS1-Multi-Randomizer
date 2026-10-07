import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { decideStartupInstallation } from '@ds1tr/contracts';
import type { GameStateReport } from '@ds1tr/contracts';
import { api, type DetectedInstallation } from './api';
import { useErrorText, type ErrorInfo } from './ErrorNotice';

interface Props {
  gamePath: string;
  backendReady: boolean;
  /** The fog randomizer (fog gates) is switched on, so its data has to be ready. */
  worldNeeded: boolean;
  onPath: (path: string) => void;
  onState: (state: GameStateReport | null) => void;
  /** Bumped by the parent after it changed the game files (apply / restore) so the status is re-read. */
  refreshKey: number;
  busy: boolean;
}

/** `problem` holds a key of `game.problem.*` or `game.state.*`, never a sentence: the text is chosen at render time in the current language. */
type GameStatus =
  | { kind: 'searching' }
  | { kind: 'choose'; options: DetectedInstallation[] }
  | { kind: 'missing' }
  | { kind: 'checking' }
  | { kind: 'ready' }
  | { kind: 'problem'; problem: 'notComplete' | 'notVerified' | 'cannotCheck' | Exclude<GameStateReport['state'], 'vanilla' | 'active'> };

type WorldStatus = { kind: 'idle' } | { kind: 'preparing' } | { kind: 'ready' } | { kind: 'failed'; error: ErrorInfo };

/**
 * First-run readiness: finds Dark Souls Remastered, checks it and prepares the fog randomizer, all without asking the
 * person for anything but, at most, which installation to use. Technical details stay in the backend log.
 */
export function Readiness({ gamePath, backendReady, worldNeeded, onPath, onState, refreshKey, busy }: Props) {
  const { t } = useTranslation();
  const errorText = useErrorText();
  const [game, setGame] = useState<GameStatus>({ kind: 'searching' });
  const [world, setWorld] = useState<WorldStatus>({ kind: 'idle' });
  const [showFolder, setShowFolder] = useState(false);
  const [draft, setDraft] = useState('');
  const started = useRef(false);
  const checked = useRef('');
  /** The folder the person picked by hand (Browse, Use this folder, one of several copies); remembered once it passed the check. */
  const manual = useRef('');
  const pick = (path: string) => { manual.current = path; checked.current = ''; onPath(path); };

  const checkGame = useCallback(async (path: string) => {
    setGame({ kind: 'checking' });
    setWorld({ kind: 'idle' });
    try {
      const validation = await api.validateInstallation(path);
      if (validation.valid && manual.current === path) void window.desktop?.rememberGamePath(path);
      if (!validation.valid) {
        setGame({ kind: 'problem', problem: 'notComplete' });
        onState(null);
        return;
      }
      let state = await api.state(path);
      if (state.state === 'no-snapshot') {
        const imported = await api.importPristine(path);
        if (!imported.ok) {
          setGame({ kind: 'problem', problem: 'notVerified' });
          onState(state);
          return;
        }
        state = await api.state(path);
      }
      onState(state);
      setGame(state.state === 'vanilla' || state.state === 'active' ? { kind: 'ready' } : { kind: 'problem', problem: state.state });
    } catch {
      setGame({ kind: 'problem', problem: 'cannotCheck' });
    }
  }, [onState]);

  useEffect(() => {
    if (!backendReady || started.current) return;
    started.current = true;
    void (async () => {
      const saved = (await window.desktop?.connection())?.gamePath ?? '';
      if (gamePath) return;
      try {
        // A copy the person chose by hand wins while it still passes the check; Steam detection is only the fallback.
        let savedValid = false;
        if (saved) {
          try { savedValid = (await api.validateInstallation(saved)).valid; } catch { savedValid = false; }
        }
        const detected = savedValid ? [] : (await api.detectGame()).installations.filter((item) => item.valid);
        const decision = decideStartupInstallation({ saved, savedValid, detected: detected.map((item) => item.gamePath) });
        if (decision.kind === 'saved' || decision.kind === 'detected') onPath(decision.path);
        else if (decision.kind === 'choose') setGame({ kind: 'choose', options: detected });
        else setGame({ kind: 'missing' });
      } catch {
        setGame({ kind: 'missing' });
      }
    })();
  }, [backendReady, gamePath, onPath]);

  useEffect(() => {
    if (!backendReady || !gamePath) return;
    const key = `${gamePath}|${refreshKey}`;
    if (checked.current === key) return;
    checked.current = key;
    void checkGame(gamePath);
  }, [backendReady, gamePath, refreshKey, checkGame]);

  const prepareWorld = useCallback(async () => {
    setWorld({ kind: 'preparing' });
    try {
      const result = await api.prepareWorldData(gamePath);
      setWorld(result.ok ? { kind: 'ready' } : { kind: 'failed', error: { code: result.code, detail: result.userMessage } });
    } catch {
      setWorld({ kind: 'failed', error: { code: 'FOG_PREREQUISITES_MISSING' } });
    }
  }, [gamePath]);

  useEffect(() => {
    if (game.kind === 'ready' && worldNeeded && world.kind === 'idle') void prepareWorld();
  }, [game.kind, worldNeeded, world.kind, prepareWorld]);

  const choose = async () => {
    const selected = await window.desktop?.selectGameDirectory();
    if (selected) { pick(selected); setShowFolder(false); }
  };

  return (
    <div className="readiness" aria-live="polite">
      <div className={`readiness-row ${game.kind}`}>
        <span className="readiness-mark">{game.kind === 'ready' ? '✓' : game.kind === 'problem' || game.kind === 'missing' ? '!' : '…'}</span>
        <div>
          <strong>
            {game.kind === 'ready' && t('game.status.ready')}
            {game.kind === 'searching' && t('game.status.searching')}
            {game.kind === 'checking' && t('game.status.checking')}
            {game.kind === 'choose' && t('game.status.choose')}
            {game.kind === 'missing' && t('game.status.missing')}
            {game.kind === 'problem' && t('game.status.problem')}
          </strong>
          {game.kind === 'problem' && <p>{t(`game.problem.${game.problem}`)}</p>}
          {game.kind === 'choose' && (
            <div className="readiness-choices">
              {game.options.map((item) => <button key={item.gamePath} className="secondary" onClick={() => pick(item.gamePath)}>{t('game.useCopy', { path: item.gamePath })}</button>)}
            </div>
          )}
          {gamePath && game.kind === 'ready' && <small className="field-note">{gamePath}</small>}
        </div>
        <button className="secondary" disabled={busy} onClick={() => (game.kind === 'missing' ? void choose() : setShowFolder((open) => !open))}>
          {game.kind === 'missing' ? t('game.selectFolder') : t('game.changeFolder')}
        </button>
      </div>
      {showFolder && (
        <div className="path-row">
          <input value={draft} onChange={(event) => setDraft(event.target.value)} placeholder={t('game.folderPlaceholder')} aria-label={t('game.folderLabel')} />
          <button className="secondary" onClick={() => void choose()}>{t('game.browse')}</button>
          <button className="secondary" disabled={!draft.trim()} onClick={() => { pick(draft.trim()); setShowFolder(false); }}>{t('game.useFolder')}</button>
        </div>
      )}
      {worldNeeded && game.kind === 'ready' && (
        <div className={`readiness-row ${world.kind}`}>
          <span className="readiness-mark">{world.kind === 'ready' ? '✓' : world.kind === 'failed' ? '!' : '…'}</span>
          <div>
            <strong>{world.kind === 'ready' ? t('game.world.ready') : world.kind === 'failed' ? t('game.world.attention') : world.kind === 'preparing' ? t('game.world.preparing') : t('game.world.label')}</strong>
            {world.kind === 'failed' && <p>{errorText(world.error)}</p>}
          </div>
          {world.kind === 'failed' && <button className="secondary" onClick={() => void prepareWorld()}>{t('game.world.retry')}</button>}
        </div>
      )}
    </div>
  );
}
