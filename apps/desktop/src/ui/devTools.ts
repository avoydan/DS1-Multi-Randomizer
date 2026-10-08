import { useEffect, useState } from 'react';

/**
 * Whether the internal test tools (the test worlds) are shown. The main process decides: always in an internal build, and in a public build only after the optional
 * Ctrl+Shift+Alt+F12 shortcut. It only changes what is shown, never a setting or a world.
 */
export function useDevTools(): boolean {
  const [shown, setShown] = useState(false);
  useEffect(() => {
    let alive = true;
    void window.desktop?.devTools?.().then((value) => { if (alive) setShown(value === true); }).catch(() => undefined);
    const stop = window.desktop?.onDevTools?.((value) => setShown(value));
    return () => { alive = false; stop?.(); };
  }, []);
  return shown;
}
