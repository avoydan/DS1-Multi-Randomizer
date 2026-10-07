import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { EnemyPoolDescription, PlacementFitMode } from '@ds1tr/contracts';
import { api } from './api';
import { ErrorNotice, type ErrorInfo } from './ErrorNotice';

interface Props {
  gamePath: string;
  /** The editor is usable: Sampling with regular enemies on. */
  active: boolean;
  fit: PlacementFitMode;
  customPool: string[] | null;
  onChange: (pool: string[] | null) => void;
}

/**
 * Custom enemy pool: which regular enemies may be placed. The list comes from the backend (read from the clean game
 * data), the selection is stored by model id. The pool restricts replacements only; enemies that are not selected stay
 * in the game where they are. Boss and NPC entries of the original tool are not offered yet.
 */
export function EnemyPoolEditor({ gamePath, active, fit, customPool, onChange }: Props) {
  const { t } = useTranslation();
  const [description, setDescription] = useState<EnemyPoolDescription | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ErrorInfo | null>(null);
  const [filter, setFilter] = useState('');
  const enabled = customPool !== null;
  const selected = useMemo(() => new Set(customPool ?? []), [customPool]);

  const load = async (): Promise<EnemyPoolDescription | null> => {
    setLoading(true);
    setError(null);
    try {
      const response = await api.enemyPool(gamePath);
      if (!('pool' in response)) {
        setError({ code: response.code, detail: response.message });
        return null;
      }
      setDescription(response.pool);
      return response.pool;
    } catch (failure) {
      setError({ code: 'BACKEND_UNAVAILABLE', detail: failure instanceof Error ? failure.message : String(failure) });
      return null;
    } finally {
      setLoading(false);
    }
  };

  const toggleUse = async (checked: boolean) => {
    if (!checked) {
      onChange(null);
      return;
    }
    const loaded = description ?? await load();
    if (loaded) onChange(loaded.entries.map((entry) => entry.model));
  };

  const setModel = (model: string, on: boolean) => {
    const next = new Set(selected);
    if (on) next.add(model); else next.delete(model);
    onChange([...next].sort());
  };

  const visible = useMemo(() => {
    const needle = filter.trim().toLowerCase();
    return (description?.entries ?? []).filter((entry) => needle === '' || entry.model.toLowerCase().includes(needle)
      || entry.maps.some((map) => map.toLowerCase().includes(needle)));
  }, [description, filter]);

  const coverage = useMemo(() => {
    if (!description || !enabled) return null;
    const chosen = description.entries.filter((entry) => selected.has(entry.model));
    const smallest = chosen.reduce((min, entry) => Math.min(min, entry.size), Infinity);
    const sizeCounts = [0, 1, 2, 3, 4, 5].map((size) => chosen.filter((entry) => entry.size === size).length);
    const unknown = [...selected].filter((model) => !description.entries.some((entry) => entry.model === model));
    const slotsWithoutFit = fit === 'fits' && chosen.length > 0
      ? description.entries.filter((entry) => entry.size < smallest).reduce((sum, entry) => sum + entry.slots, 0)
      : 0;
    return { chosen: chosen.length, sizeCounts, unknown, slotsWithoutFit };
  }, [description, enabled, selected, fit]);

  return (
    <div className="pool-editor">
      <label className="toggle-row" title={t('pool.hint')}>
        <span>{t('pool.label')}</span>
        <input type="checkbox" checked={enabled} disabled={!active || loading} onChange={(event) => { void toggleUse(event.target.checked); }} />
      </label>
      {!enabled && <small className="field-note">{t('pool.fullPool')}</small>}
      {error && <ErrorNotice error={error} />}
      {loading && <small className="field-note">{t('pool.reading')}</small>}
      {enabled && !description && !loading && <button className="secondary" type="button" onClick={() => { void load(); }}>{t('pool.loadList')}</button>}
      {enabled && description && coverage && (
        <>
          <small className="field-note">
            {t('pool.selected', { chosen: coverage.chosen, total: description.entries.length, sizes: coverage.sizeCounts.join(' / ') })}
          </small>
          {coverage.chosen === 0 && <small className="field-note pool-error">{t('pool.selectOne')}</small>}
          {coverage.slotsWithoutFit > 0 && (
            <small className="field-note pool-warning">{t('pool.noFit', { count: coverage.slotsWithoutFit })}</small>
          )}
          {coverage.unknown.length > 0 && (
            <small className="field-note pool-error">{t('pool.unknown', { count: coverage.unknown.length, ids: coverage.unknown.slice(0, 4).join(', ') })}{' '}
              <button className="link-button" type="button" onClick={() => onChange([...selected].filter((model) => !coverage.unknown.includes(model)).sort())}>{t('pool.removeThem')}</button>
            </small>
          )}
          <div className="pool-actions">
            <input value={filter} placeholder={t('pool.filterPlaceholder')} aria-label={t('pool.filterLabel')} onChange={(event) => setFilter(event.target.value)} />
            <button className="secondary" type="button" onClick={() => onChange(description.entries.map((entry) => entry.model))}>{t('pool.all')}</button>
            <button className="secondary" type="button" onClick={() => onChange([])}>{t('pool.none')}</button>
            <button className="secondary" type="button" onClick={() => onChange(null)} title={t('pool.resetHint')}>{t('pool.reset')}</button>
          </div>
          <div className="pool-list" role="group" aria-label={t('pool.groupLabel')}>
            {visible.map((entry) => (
              <label className="pool-row" key={entry.model}>
                <input type="checkbox" checked={selected.has(entry.model)} onChange={(event) => setModel(entry.model, event.target.checked)} />
                <span className="pool-model">{entry.model}</span>
                <span className="pool-meta">{t('pool.meta', { size: entry.size, difficulty: entry.difficulty, slots: t('pool.slots', { count: entry.slots }), maps: t('pool.maps', { count: entry.maps.length }) })}</span>
              </label>
            ))}
            {visible.length === 0 && <small className="field-note">{t('pool.noMatch')}</small>}
          </div>
        </>
      )}
    </div>
  );
}
