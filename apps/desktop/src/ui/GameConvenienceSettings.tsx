import { useEffect, useId, useState } from 'react';
import { useTranslation } from 'react-i18next';

export function GameConvenienceSettings() {
  const { t } = useTranslation();
  const id = useId();
  const [enabled, setEnabled] = useState(false);
  const [busy, setBusy] = useState(true);

  useEffect(() => {
    let active = true;
    void window.desktop?.gameConveniences.freeCursorInMenus().then((value) => {
      if (active) setEnabled(value === true);
    }).catch(() => { if (active) setEnabled(false); }).finally(() => { if (active) setBusy(false); });
    return () => { active = false; };
  }, []);

  const update = async (next: boolean) => {
    setBusy(true);
    try {
      const saved = await window.desktop?.gameConveniences.setFreeCursorInMenus(next);
      if (saved) setEnabled(next);
    } catch {
      // Keep the last persisted value if the app bridge is unavailable.
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="card game-convenience-card">
      <h2>{t('app.views.settings.gameConveniencesTitle')}</h2>
      <p>{t('app.views.settings.gameConveniencesNote')}</p>
      <label className="update-auto">
        <input id={id} type="checkbox" role="switch" className="switch" checked={enabled} disabled={busy} onChange={(event) => { void update(event.target.checked); }} />
        <span>
          <strong>{t('app.views.settings.freeCursorLabel')}</strong>
          <small>{t('app.views.settings.freeCursorDescription')}</small>
        </span>
      </label>
    </section>
  );
}
