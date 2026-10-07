import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { GenerationSettings } from '@ds1tr/contracts';
import { api } from './api';
import { ErrorNotice, type ErrorInfo } from './ErrorNotice';
import { summarize } from './summary';

export interface LoadedSeed { code: string; settings: GenerationSettings }

export const SummaryChips = ({ settings }: { settings: GenerationSettings }) => {
  const { t } = useTranslation();
  const summary = summarize(settings);
  const chip = (label: string, on: boolean) => <span key={label} className={`summary-chip ${on ? 'on' : 'off'}`}>{label}</span>;
  return <div className="summary-row">{chip(t('common.chips.items'), summary.items)}{chip(t('common.chips.enemies'), summary.enemies)}{chip(t('common.chips.bosses'), summary.bosses)}{chip(t('common.chips.fog'), summary.fog)}</div>;
};

/** `DS1TR1-06NJDCP6-...-2G`: the start and the end are enough to recognize a code in a list. */
export const shortSeedCode = (code: string): string => {
  const groups = code.split('-');
  return groups.length <= 3 ? code : `${groups[0]}-${groups[1]}…${groups[groups.length - 1]}`;
};

interface Props {
  busy: boolean;
  /** Called with a valid, decoded code: the app takes over its settings and generates that exact world. */
  onLoaded: (seed: LoadedSeed) => void;
  onClose: () => void;
}

/** "Load seed": paste a Seed Code. A valid code is loaded and its world is created right away; there is no second step. */
export function SeedPanel({ busy, onLoaded, onClose }: Props) {
  const { t } = useTranslation();
  const [text, setText] = useState('');
  const [error, setError] = useState<ErrorInfo | null>(null);
  const [checking, setChecking] = useState(false);

  const load = async () => {
    setError(null);
    setChecking(true);
    try {
      const result = await api.decodeSeed(text);
      if (!result.ok) { setError({ code: result.code, detail: result.technical }); return; }
      setText('');
      onLoaded({ code: result.seedCode, settings: result.settings });
    } catch {
      setError({ code: 'BACKEND_UNAVAILABLE' });
    } finally {
      setChecking(false);
    }
  };

  return (
    <div className="seed-panel">
      <label className="field-label" htmlFor="seed-code-input">{t('seed.label')}</label>
      <textarea id="seed-code-input" className="run-logs seed-input" rows={3} value={text} onChange={(event) => setText(event.target.value)} placeholder={t('seed.placeholder')} />
      {error && <ErrorNotice error={error} />}
      <div className="run-actions">
        <button className="primary" disabled={busy || checking || !text.trim()} onClick={() => void load()}>{t('seed.load')}</button>
        <button className="secondary" onClick={onClose}>{t('common.cancel')}</button>
      </div>
    </div>
  );
}
