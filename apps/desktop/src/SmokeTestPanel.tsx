import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { GenerationSettings } from '@ds1tr/contracts';
import { api, type SmokeTestList } from './api';
import { ErrorNotice, type ErrorInfo } from './ErrorNotice';
import { currentLanguage } from './i18n';
import { formatNumber } from './i18n/format';
import type { LoadedSeed } from './SeedPanel';

interface Props {
  busy: boolean;
  onCreate: (seed: LoadedSeed) => void;
}

/**
 * For people who test the randomizer in the game: fixed worlds with a short list of what to look at. It lives in Advanced and
 * creates the world through the normal Load seed path. Quick smoke tests show one feature within about a minute of gaining
 * control, at a place stated exactly; extended gameplay tests need progression or exercise a whole system. Nothing here is
 * checked automatically.
 *
 * The scenario texts are written in English in the shared contracts (the source of truth for the test worlds); the visible text comes
 * from `smoke.worlds.<id>.*` in the current language, falling back to that English source for anything not translated.
 */
export function SmokeTestPanel({ busy, onCreate }: Props) {
  const { t } = useTranslation();
  const [list, setList] = useState<SmokeTestList | null>(null);
  const [selected, setSelected] = useState('');
  const [error, setError] = useState<ErrorInfo | null>(null);

  const load = async () => {
    if (list) return;
    try {
      const result = await api.smokeTests();
      setList(result);
      setSelected(result.worlds[0]?.id ?? '');
    } catch { setError({ code: 'BACKEND_UNAVAILABLE' }); }
  };

  /** One text of a scenario in the current language, or its English source. */
  const text = (id: string, field: string, source: string): string => t(`smoke.worlds.${id}.${field}` as never, { defaultValue: source }) as string;
  const lines = (id: string, field: string, source: readonly string[]): string[] => source.map((line, index) => text(id, `${field}.${index}`, line));
  const estimate = (seconds: number): string => {
    if (seconds < 90) return t('smoke.estimateSeconds', { count: seconds });
    const minutes = Math.round(seconds / 30) / 2;
    return t('smoke.estimateMinutes', { count: minutes, minutes: formatNumber(minutes, currentLanguage()) });
  };

  const world = list?.worlds.find((entry) => entry.id === selected) ?? null;
  const quick = list?.worlds.filter((entry) => entry.category === 'quick') ?? [];
  const extended = list?.worlds.filter((entry) => entry.category === 'extended') ?? [];
  return (
    <details className="log-panel" onToggle={(event) => { if ((event.currentTarget as HTMLDetailsElement).open) void load(); }}>
      <summary>{t('smoke.summary')}</summary>
      <p className="field-note">{t('smoke.intro')}</p>
      {error && <ErrorNotice error={error} />}
      {list && (
        <>
          <label className="field-label" htmlFor="smoke-world">{t('smoke.world')}</label>
          <select id="smoke-world" value={selected} onChange={(event) => setSelected(event.target.value)}>
            <optgroup label={t('smoke.groupQuick')}>
              {quick.map((entry) => <option key={entry.id} value={entry.id}>{text(entry.id, 'title', entry.title)}</option>)}
            </optgroup>
            <optgroup label={t('smoke.groupExtended')}>
              {extended.map((entry) => <option key={entry.id} value={entry.id}>{text(entry.id, 'title', entry.title)}</option>)}
            </optgroup>
          </select>
          {world && (
            <div className="smoke-details">
              <strong className={`smoke-category ${world.category}`}>{world.category === 'quick' ? t('smoke.categoryQuick') : t('smoke.categoryExtended')}</strong>
              <p>{text(world.id, 'purpose', world.purpose)}</p>
              <p className={`smoke-result ${world.gameplay ? 'passed' : 'unplayed'}`}>{world.gameplay
                ? t('smoke.played', { date: world.gameplay.date, result: t(world.gameplay.result === 'passed' ? 'smoke.resultPassed' : 'smoke.resultPartly'), note: text(world.id, 'gameplayNote', world.gameplay.note) })
                : t('smoke.unplayed')}</p>
              {world.category === 'quick' ? (
                <>
                  <strong>{t('smoke.target')}</strong>
                  <ul>{world.targets.map((line, index) => <li key={line}>{text(world.id, `targets.${index}`, line)}</li>)}</ul>
                  <strong>{t('smoke.expected')}</strong>
                  <ul>{world.expected.map((line, index) => <li key={line}>{text(world.id, `expected.${index}`, line)}</li>)}</ul>
                  {world.estimateSeconds !== null && <p><strong>{t('smoke.estimatedTime')}</strong> {t('smoke.estimateAfter', { time: estimate(world.estimateSeconds) })}</p>}
                  <strong>{t('smoke.check')}</strong>
                  <ul>{lines(world.id, 'observe', world.observe).map((line) => <li key={line}>{line}</li>)}</ul>
                  {world.afterwards.length > 0 && (<><strong>{t('smoke.afterwards')}</strong><ul>{lines(world.id, 'afterwards', world.afterwards).map((line) => <li key={line}>{line}</li>)}</ul></>)}
                  <p className="field-note">{t('smoke.outsideAsylum', { start: t('smoke.start', { defaultValue: list.start }) })}</p>
                </>
              ) : (
                <>
                  <strong>{t('smoke.inThisWorld')}</strong>
                  <ul>{lines(world.id, 'observe', world.observe).map((line) => <li key={line}>{line}</li>)}</ul>
                  <strong>{t('smoke.checkInGame')}</strong>
                  <ul>{list.checklist.map((line, index) => <li key={line}>{t(`smoke.checklist.${index}` as never, { defaultValue: line }) as string}</li>)}</ul>
                </>
              )}
              <label className="field-label" htmlFor="smoke-code">{t('smoke.seedCode')}</label>
              <textarea id="smoke-code" className="run-logs seed-output" readOnly rows={2} value={world.seedCode} onFocus={(event) => event.currentTarget.select()} />
              <div className="run-actions"><button className="secondary" disabled={busy} onClick={() => onCreate({ code: world.seedCode, settings: world.settings as GenerationSettings })}>{t('smoke.create')}</button></div>
            </div>
          )}
        </>
      )}
    </details>
  );
}
