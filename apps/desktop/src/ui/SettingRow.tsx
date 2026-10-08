import { useId } from 'react';
import type { RandomizerConfig } from '@ds1tr/contracts';
import { displayedValue, settingState, type SettingDef } from '@ds1tr/contracts';
import { EnemyPoolEditor } from '../EnemyPoolEditor';
import { HelpButton } from './HelpPanel';
import { helpChoiceKey, settingChoiceKey, settingLabelKey, useTx } from './text';

export interface SettingRowProps {
  def: SettingDef;
  config: RandomizerConfig;
  onChange: (id: string, value: unknown) => void;
  /** Basic tiles show the short explanation under the name. */
  describe?: boolean;
}

/**
 * One option of the configuration, drawn from the registry: the name, the control, the `?` that explains it, and the reason when it cannot be changed now.
 * A disabled option always says why (`help.reasons.*`); nothing is hidden without a word.
 */
export function SettingRow({ def, config, onChange, describe = false }: SettingRowProps) {
  const { tx } = useTx();
  const uid = useId();
  const state = settingState(config, def.id);
  const label = tx(settingLabelKey(def.id));
  const reasonId = `${uid}-reason`;
  const noteId = `${uid}-note`;
  const raw = displayedValue(config, def.id);
  const reason = !state.enabled && state.reason ? tx(`help.reasons.${state.reason}`) : null;
  const warning = state.warning ? tx(`help.reasons.${state.warning}`) : null;
  const describedBy = [reason ? reasonId : null, warning ? reasonId : null, def.kind === 'select' ? noteId : null].filter(Boolean).join(' ') || undefined;
  const classes = `setting-row kind-${def.kind}${state.enabled ? '' : ' is-disabled'}${describe ? ' is-tile' : ''}`;

  let control;
  let note: string | null = null;
  switch (def.kind) {
    case 'toggle':
      control = (
        <input id={uid} type="checkbox" role="switch" className="switch" checked={raw === true} disabled={!state.enabled} aria-describedby={describedBy} onChange={(event) => onChange(def.id, event.target.checked)} />
      );
      break;
    case 'range': {
      const value = typeof raw === 'number' ? raw : 0;
      control = (
        <div className="range-control">
          <input id={uid} type="range" min="0" max="100" value={value} disabled={!state.enabled} aria-describedby={describedBy} aria-valuetext={`${value}%`} onChange={(event) => onChange(def.id, Number(event.target.value))} />
          <strong>{value}%</strong>
        </div>
      );
      break;
    }
    case 'select': {
      const value = String(raw ?? def.choices?.[0] ?? '');
      control = (
        <select id={uid} value={value} disabled={!state.enabled} aria-describedby={describedBy} onChange={(event) => onChange(def.id, event.target.value)}>
          {(def.choices ?? []).map((choice) => <option key={choice} value={choice}>{tx(settingChoiceKey(def.id, choice))}</option>)}
        </select>
      );
      if (!def.plainChoices) note = tx(helpChoiceKey(def.id, value));
      break;
    }
    case 'pool':
      control = null;
      break;
  }

  if (def.kind === 'pool') {
    return (
      <div className={classes} data-setting={def.id}>
        <EnemyPoolEditor
          gamePath={config.gamePath} active={state.enabled} fit={config.enemies.placement.fit} customPool={config.enemies.placement.customPool}
          label={label} help={<HelpButton id={def.id} />} reason={reason}
          onChange={(pool) => onChange(def.id, pool)}
        />
      </div>
    );
  }

  const isRow = def.kind === 'toggle';
  return (
    <div className={classes} data-setting={def.id}>
      <div className={isRow ? 'setting-line' : 'setting-head'}>
        <span className="setting-name">
          <label htmlFor={uid}>{label}</label>
          <HelpButton id={def.id} />
        </span>
        {isRow && control}
      </div>
      {describe && <p className="setting-desc">{tx(`help.settings.${def.id}.short`)}</p>}
      {!isRow && control}
      {note && <p className="setting-note" id={noteId}>{note}</p>}
      {reason && <p className="setting-reason" id={reasonId} role="note">{reason}</p>}
      {!reason && warning && <p className="setting-warning" id={reasonId} role="note">{warning}</p>}
    </div>
  );
}
