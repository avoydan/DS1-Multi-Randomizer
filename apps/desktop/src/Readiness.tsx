import { useCallback, useEffect, useRef, useState } from 'react';
import { decideStartupInstallation } from '@ds1tr/contracts';
import type { GameStateReport } from '@ds1tr/contracts';
import { api, type DetectedInstallation } from './api';

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

type GameStatus =
  | { kind: 'searching' }
  | { kind: 'choose'; options: DetectedInstallation[] }
  | { kind: 'missing' }
  | { kind: 'checking' }
  | { kind: 'ready' }
  | { kind: 'problem'; message: string };

type WorldStatus = { kind: 'idle' } | { kind: 'preparing'; message: string } | { kind: 'ready' } | { kind: 'failed'; message: string };

const plainState: Record<GameStateReport['state'], string> = {
  invalid: 'This folder does not look like Dark Souls Remastered.',
  'no-snapshot': 'Dark Souls Remastered is being checked.',
  'version-changed': 'Dark Souls Remastered was updated. Restore the original game files, then try again.',
  vanilla: '',
  active: '',
  interrupted: 'A previous change to the game was interrupted. Use "Restore original game" to repair it.',
  modified: 'Some game files were changed by another tool. Verify the game files in Steam, then try again.',
};

/**
 * First-run readiness: finds Dark Souls Remastered, checks it and prepares the fog randomizer, all without asking the
 * person for anything but, at most, which installation to use. Technical details stay in the backend log.
 */
export function Readiness({ gamePath, backendReady, worldNeeded, onPath, onState, refreshKey, busy }: Props) {
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
        setGame({ kind: 'problem', message: 'This folder does not look like a complete Dark Souls Remastered installation. Verify the game files in Steam, or choose another folder.' });
        onState(null);
        return;
      }
      let state = await api.state(path);
      if (state.state === 'no-snapshot') {
        const imported = await api.importPristine(path);
        if (!imported.ok) {
          setGame({ kind: 'problem', message: 'Dark Souls Remastered could not be verified. If you used other randomizers on it, verify the game files in Steam and try again.' });
          onState(state);
          return;
        }
        state = await api.state(path);
      }
      onState(state);
      const hint = plainState[state.state];
      setGame(hint && state.state !== 'vanilla' && state.state !== 'active' ? { kind: 'problem', message: hint } : { kind: 'ready' });
    } catch {
      setGame({ kind: 'problem', message: 'Dark Souls Remastered could not be checked. Restart the app and try again.' });
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
    setWorld({ kind: 'preparing', message: 'Preparing fog randomizer...' });
    try {
      const result = await api.prepareWorldData(gamePath);
      setWorld(result.ok ? { kind: 'ready' } : { kind: 'failed', message: result.userMessage });
    } catch {
      setWorld({ kind: 'failed', message: 'The fog randomizer could not be prepared. Click Retry.' });
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
            {game.kind === 'ready' && 'Dark Souls Remastered found'}
            {game.kind === 'searching' && 'Looking for Dark Souls Remastered...'}
            {game.kind === 'checking' && 'Checking Dark Souls Remastered...'}
            {game.kind === 'choose' && 'We found more than one copy of Dark Souls Remastered'}
            {game.kind === 'missing' && "We couldn't find Dark Souls Remastered automatically."}
            {game.kind === 'problem' && 'Dark Souls Remastered needs attention'}
          </strong>
          {game.kind === 'problem' && <p>{game.message}</p>}
          {game.kind === 'choose' && (
            <div className="readiness-choices">
              {game.options.map((item) => <button key={item.gamePath} className="secondary" onClick={() => pick(item.gamePath)}>Use the copy in {item.gamePath}</button>)}
            </div>
          )}
          {gamePath && game.kind === 'ready' && <small className="field-note">{gamePath}</small>}
        </div>
        <button className="secondary" disabled={busy} onClick={() => (game.kind === 'missing' ? void choose() : setShowFolder((open) => !open))}>
          {game.kind === 'missing' ? 'Select game folder' : 'Change game folder'}
        </button>
      </div>
      {showFolder && (
        <div className="path-row">
          <input value={draft} onChange={(event) => setDraft(event.target.value)} placeholder="Type the Dark Souls Remastered folder, or use Browse" aria-label="Dark Souls Remastered folder" />
          <button className="secondary" onClick={() => void choose()}>Browse</button>
          <button className="secondary" disabled={!draft.trim()} onClick={() => { pick(draft.trim()); setShowFolder(false); }}>Use this folder</button>
        </div>
      )}
      {worldNeeded && game.kind === 'ready' && (
        <div className={`readiness-row ${world.kind}`}>
          <span className="readiness-mark">{world.kind === 'ready' ? '✓' : world.kind === 'failed' ? '!' : '…'}</span>
          <div>
            <strong>{world.kind === 'ready' ? 'Fog randomizer ready' : world.kind === 'failed' ? 'Fog randomizer needs attention' : world.kind === 'preparing' ? world.message : 'Fog randomizer'}</strong>
            {world.kind === 'failed' && <p>{world.message}</p>}
          </div>
          {world.kind === 'failed' && <button className="secondary" onClick={() => void prepareWorld()}>Retry</button>}
        </div>
      )}
    </div>
  );
}
