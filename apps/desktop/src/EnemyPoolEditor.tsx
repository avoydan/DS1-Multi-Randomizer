import { useMemo, useState } from 'react';
import type { EnemyPoolDescription, PlacementFitMode } from '@ds1tr/contracts';
import { api } from './api';

interface Props {
  gamePath: string;
  /** The editor is usable: Legacy-style sampling with regular enemies on. */
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
  const [description, setDescription] = useState<EnemyPoolDescription | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState('');
  const enabled = customPool !== null;
  const selected = useMemo(() => new Set(customPool ?? []), [customPool]);

  const load = async (): Promise<EnemyPoolDescription | null> => {
    setLoading(true);
    setError(null);
    try {
      const response = await api.enemyPool(gamePath);
      if (!('pool' in response)) {
        setError(response.message);
        return null;
      }
      setDescription(response.pool);
      return response.pool;
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure));
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
      <label className="toggle-row" title="Legacy-style sampling only. Restricts which enemies may be placed; enemies you leave out stay where they are in the game.">
        <span>Custom enemy pool</span>
        <input type="checkbox" checked={enabled} disabled={!active || loading} onChange={(event) => { void toggleUse(event.target.checked); }} />
      </label>
      {!enabled && <small className="field-note">Full pool: every regular enemy of the game. Boss and NPC entries of the original tool are not available yet.</small>}
      {error && <small className="field-note pool-error">{error}</small>}
      {loading && <small className="field-note">Reading the enemy list from the clean game data…</small>}
      {enabled && !description && !loading && <button className="secondary" type="button" onClick={() => { void load(); }}>Load enemy list</button>}
      {enabled && description && coverage && (
        <>
          <small className="field-note">
            Selected {coverage.chosen} of {description.entries.length} regular enemies. By size class 0-5: {coverage.sizeCounts.join(' / ')}.
          </small>
          {coverage.chosen === 0 && <small className="field-note pool-error">Select at least one enemy, or switch the custom pool off.</small>}
          {coverage.slotsWithoutFit > 0 && (
            <small className="field-note pool-warning">With "only where they fit", {coverage.slotsWithoutFit} regular slots have no selected enemy small enough and will stay as they are.</small>
          )}
          {coverage.unknown.length > 0 && (
            <small className="field-note pool-error">{coverage.unknown.length} selected id(s) are not in this game's data ({coverage.unknown.slice(0, 4).join(', ')}); generation would be refused.{' '}
              <button className="link-button" type="button" onClick={() => onChange([...selected].filter((model) => !coverage.unknown.includes(model)).sort())}>Remove them</button>
            </small>
          )}
          <div className="pool-actions">
            <input value={filter} placeholder="Filter by model or map" aria-label="Filter enemies" onChange={(event) => setFilter(event.target.value)} />
            <button className="secondary" type="button" onClick={() => onChange(description.entries.map((entry) => entry.model))}>All</button>
            <button className="secondary" type="button" onClick={() => onChange([])}>None</button>
            <button className="secondary" type="button" onClick={() => onChange(null)} title="Back to the full pool">Reset</button>
          </div>
          <div className="pool-list" role="group" aria-label="Regular enemies">
            {visible.map((entry) => (
              <label className="pool-row" key={entry.model}>
                <input type="checkbox" checked={selected.has(entry.model)} onChange={(event) => setModel(entry.model, event.target.checked)} />
                <span className="pool-model">{entry.model}</span>
                <span className="pool-meta">size {entry.size} · difficulty {entry.difficulty} · {entry.slots} slot{entry.slots === 1 ? '' : 's'} · {entry.maps.length} map{entry.maps.length === 1 ? '' : 's'}</span>
              </label>
            ))}
            {visible.length === 0 && <small className="field-note">No enemy matches the filter.</small>}
          </div>
        </>
      )}
    </div>
  );
}
