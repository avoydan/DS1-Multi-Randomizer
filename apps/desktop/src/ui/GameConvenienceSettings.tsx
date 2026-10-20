import { useEffect, useId, useState, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';

function shortcutKey(code: string): string | null {
  if (/^F(?:[1-9]|1[0-9]|2[0-4])$/u.test(code)) return code;
  const letter = /^Key([A-Z])$/u.exec(code)?.[1];
  if (letter) return letter;
  const digit = /^Digit([0-9])$/u.exec(code)?.[1];
  if (digit) return digit;
  const named: Record<string, string> = {
    Space: 'Space', Tab: 'Tab', Insert: 'Insert', Delete: 'Delete', Home: 'Home', End: 'End',
    PageUp: 'PageUp', PageDown: 'PageDown', ArrowUp: 'Up', ArrowDown: 'Down', ArrowLeft: 'Left', ArrowRight: 'Right',
    Enter: 'Enter', Backspace: 'Backspace',
  };
  return named[code] ?? null;
}

export function GameConvenienceSettings({ onEnabledChange, onSkillIssueChange, onReadyChange }: {
  onEnabledChange: (enabled: boolean) => void;
  onSkillIssueChange: (enabled: boolean) => void;
  onReadyChange: (ready: boolean) => void;
}) {
  const { t } = useTranslation();
  const id = useId();
  const [enabled, setEnabled] = useState(false);
  const [skillIssueMode, setSkillIssueMode] = useState(false);
  const [busy, setBusy] = useState(true);
  const [shortcut, setShortcut] = useState('F10');
  const [shortcutBusy, setShortcutBusy] = useState(true);

  useEffect(() => {
    let active = true;
    const conveniences = window.desktop?.gameConveniences;
    if (!conveniences) { setBusy(false); setShortcutBusy(false); onReadyChange(true); return () => { active = false; }; }
    void Promise.all([conveniences.freeCursorInMenus(), conveniences.freeCursorShortcut(), conveniences.skillIssueMode()]).then(([value, savedShortcut, skillIssue]) => {
      if (active) {
        const saved = value === true;
        setEnabled(saved);
        setSkillIssueMode(skillIssue === true);
        setShortcut(savedShortcut || 'F10');
        onEnabledChange(saved);
        onSkillIssueChange(skillIssue === true);
      }
    }).catch(() => { if (active) { setEnabled(false); setSkillIssueMode(false); setShortcut('F10'); onEnabledChange(false); onSkillIssueChange(false); } }).finally(() => { if (active) { setBusy(false); setShortcutBusy(false); onReadyChange(true); } });
    return () => { active = false; };
  }, [onEnabledChange, onSkillIssueChange, onReadyChange]);

  const update = async (next: boolean) => {
    setBusy(true);
    try {
      const saved = await window.desktop?.gameConveniences.setFreeCursorInMenus(next);
      if (saved) { setEnabled(next); onEnabledChange(next); }
    } catch {
      // Keep the last persisted value if the app bridge is unavailable.
    } finally {
      setBusy(false);
    }
  };

  const updateSkillIssue = async (next: boolean) => {
    setBusy(true);
    try {
      const saved = await window.desktop?.gameConveniences.setSkillIssueMode(next);
      if (saved) { setSkillIssueMode(next); onSkillIssueChange(next); }
    } catch {
      // Keep the last persisted value if the app bridge is unavailable.
    } finally {
      setBusy(false);
    }
  };

  const captureShortcut = (event: KeyboardEvent<HTMLInputElement>) => {
    event.preventDefault();
    if (event.repeat || event.metaKey) return;
    const key = shortcutKey(event.code);
    if (!key) return; // Modifier keys alone are not shortcuts.
    const modifiers = [event.ctrlKey && 'Ctrl', event.altKey && 'Alt', event.shiftKey && 'Shift'].filter(Boolean);
    const next = [...modifiers, key].join('+');
    setShortcutBusy(true);
    void window.desktop?.gameConveniences.setFreeCursorShortcut(next).then((saved) => {
      if (saved) setShortcut(next);
    }).catch(() => { /* Keep the last accepted shortcut if saving fails. */ }).finally(() => setShortcutBusy(false));
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
      <label className="update-auto">
        <input type="checkbox" role="switch" className="switch" checked={skillIssueMode} disabled={busy} onChange={(event) => { void updateSkillIssue(event.target.checked); }} />
        <span>
          <strong>{t('app.views.settings.skillIssueLabel')}</strong>
          <small>{t('app.views.settings.skillIssueDescription')}</small>
        </span>
      </label>
      {skillIssueMode && <p className="notice" role="note">{t('app.views.settings.skillIssueOverrideNote')}</p>}
      <div className="cursor-shortcut-row">
        <label htmlFor={`${id}-shortcut`}>
          <strong>{t('app.views.settings.freeCursorShortcutLabel')}</strong>
          <small>{t('app.views.settings.freeCursorShortcutDescription')}</small>
        </label>
        <input
          id={`${id}-shortcut`}
          className="cursor-shortcut-input"
          type="text"
          value={shortcut}
          readOnly
          disabled={shortcutBusy}
          aria-label={t('app.views.settings.freeCursorShortcutLabel')}
          onKeyDown={captureShortcut}
          onFocus={(event) => event.currentTarget.select()}
        />
      </div>
    </section>
  );
}
