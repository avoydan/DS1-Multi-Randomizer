import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import type { RandomizerConfig } from '@ds1tr/contracts';
import { settingById, settingState, settingValue } from '@ds1tr/contracts';
import { Icon } from './icons';
import { helpChoiceKey, helpKey, settingChoiceKey, settingLabelKey, useTx } from './text';

/**
 * Contextual help (docs/SETTINGS_INVENTORY.md). The help of an option is read from the translation resources by its setting id; this file only decides how it is shown.
 * Opening or closing help is interface state: it never touches the configuration.
 */
interface HelpState { openId: string | null; open: (id: string) => void; toggle: (id: string) => void; close: () => void }
const HelpContext = createContext<HelpState>({ openId: null, open: () => undefined, toggle: () => undefined, close: () => undefined });
export const useHelp = (): HelpState => useContext(HelpContext);

export function HelpProvider({ children }: { children: ReactNode }) {
  const [openId, setOpenId] = useState<string | null>(null);
  const value = useMemo<HelpState>(() => ({
    openId,
    open: (id) => setOpenId(id),
    toggle: (id) => setOpenId((current) => (current === id ? null : id)),
    close: () => setOpenId(null),
  }), [openId]);
  return <HelpContext.Provider value={value}>{children}</HelpContext.Provider>;
}

/** The `?` next to an option: hover or keyboard focus shows the short explanation, a click (or Enter / Space) opens the full panel. It stays usable on a disabled option. */
export function HelpButton({ id }: { id: string }) {
  const { tx } = useTx();
  const help = useHelp();
  const open = help.openId === id;
  const name = tx(settingLabelKey(id));
  return (
    <span className="help-anchor">
      <button
        type="button" className={`help-button${open ? ' is-open' : ''}`} data-help-for={id}
        aria-label={tx('help.ui.helpFor', { name })} aria-expanded={open} aria-controls="help-panel" aria-describedby={`tip-${id}`}
        onClick={() => help.toggle(id)}
      >?</button>
      <span role="tooltip" id={`tip-${id}`} className="help-tip">{tx(helpKey(id, 'short'))}</span>
    </span>
  );
}

function Part({ title, text, tone }: { title: string; text: string; tone?: 'warn' | 'example' }) {
  return <div className={`help-part${tone ? ` ${tone}` : ''}`}><h4>{title}</h4><p>{text}</p></div>;
}

export function HelpPanel({ config, alwaysVisible = false }: { config: RandomizerConfig; alwaysVisible?: boolean }) {
  const { t } = useTranslation();
  const { tx, exists } = useTx();
  const help = useHelp();
  const id = help.openId;

  useEffect(() => {
    if (!id) return undefined;
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') help.close(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [id, help]);

  if (!id) {
    return alwaysVisible
      ? <aside id="help-panel" className="help-panel is-empty" aria-label={t('help.ui.panelTitle')}><p>{t('help.ui.panelEmpty')}</p></aside>
      : null;
  }
  const def = settingById(id);
  const state = settingState(config, id);
  const current = def.kind === 'select' ? String(settingValue(config, id) ?? def.choices?.[0]) : null;
  return (
    <aside id="help-panel" className="help-panel" aria-label={t('help.ui.panelTitle')}>
      <div className="help-panel-head">
        <div><span className="eyebrow">{t('help.ui.panelTitle')}</span><h3>{tx(settingLabelKey(id))}</h3></div>
        <button type="button" className="icon-button" aria-label={t('help.ui.close')} onClick={help.close}><Icon name="close" size={18} /></button>
      </div>
      <p className="help-short">{tx(helpKey(id, 'short'))}</p>
      {!state.enabled && state.reason && <div className="help-unavailable" role="note"><strong>{t('help.ui.unavailable')}</strong> {tx(`help.reasons.${state.reason}`)}</div>}
      <Part title={t('help.ui.what')} text={tx(helpKey(id, 'what'))} />
      <Part title={t('help.ui.affects')} text={tx(helpKey(id, 'affects'))} />
      <Part title={t('help.ui.limits')} text={tx(helpKey(id, 'limits'))} tone="warn" />
      {exists(helpKey(id, 'example')) && <Part title={t('help.ui.example')} text={tx(helpKey(id, 'example'))} tone="example" />}
      {def.kind === 'select' && !def.plainChoices && def.choices && (
        <div className="help-part"><h4>{t('help.ui.choices')}</h4>
          <dl className="help-choices">
            {def.choices.map((value) => (
              <div key={value} className={value === current ? 'is-current' : undefined}>
                <dt>{tx(settingChoiceKey(id, value))}{value === current && <span className="pill ready">{t('help.ui.selected')}</span>}</dt>
                <dd>{tx(helpChoiceKey(id, value))}</dd>
              </div>
            ))}
          </dl>
        </div>
      )}
      {def.evidence === 'partial' && <p className="help-evidence" role="note">{t('help.ui.evidencePartial')}</p>}
    </aside>
  );
}
