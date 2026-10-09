import { useCallback, useEffect, useState } from 'react';
import type { UpdateErrorCode, UpdateState } from '../../electron/updater';

/**
 * The application update state (docs/AUTO_UPDATE.md), as the main process reports it. The page only shows it and asks for the next step: it never talks to GitHub or to
 * the updater. Without the desktop shell (a browser during development) the state stays null and the update card says nothing is available.
 */
export function useUpdates() {
  const [state, setState] = useState<UpdateState | null>(null);
  const [refused, setRefused] = useState<UpdateErrorCode | null>(null);

  useEffect(() => {
    const updates = window.desktop?.updates;
    if (!updates) return undefined;
    let alive = true;
    void updates.state().then((initial) => { if (alive && initial) setState(initial); }).catch(() => undefined);
    const stop = updates.onState((next) => { if (alive) setState(next); });
    return () => { alive = false; stop(); };
  }, []);

  const run = useCallback(async (call: (() => Promise<UpdateState | null>) | undefined) => {
    setRefused(null);
    try {
      const next = await call?.();
      if (next) setState(next);
    } catch { /* a failed call leaves the last known state; the main process reports its own errors through the state */ }
  }, []);

  return {
    state,
    refused,
    check: () => run(window.desktop?.updates.check),
    download: () => run(window.desktop?.updates.download),
    setAutoCheck: (enabled: boolean) => run(() => window.desktop?.updates.setAutoCheck(enabled) ?? Promise.resolve(null)),
    openReleasePage: () => { void window.desktop?.updates.openReleasePage().catch(() => undefined); },
    install: async () => {
      setRefused(null);
      try {
        const result = await window.desktop?.updates.install();
        if (result?.state) setState(result.state);
        if (result && !result.ok && result.code) setRefused(result.code);
      } catch { /* the application stays as it is */ }
    },
  };
}

export type UpdatesApi = ReturnType<typeof useUpdates>;
