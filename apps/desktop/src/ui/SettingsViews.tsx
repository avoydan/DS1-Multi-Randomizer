import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { RandomizerConfig } from '@ds1tr/contracts';
import {
  BASIC_LAYOUT, COMBAT_SUBSECTION_IDS, SECTION_IDS, customAdvancedSettings, sectionsWithCustomSettings, settingById, settingsOfSection, settingsOfSubsection, type CombatSubsectionId, type SectionId,
} from '@ds1tr/contracts';
import { Icon, type IconName } from './icons';
import { SettingRow } from './SettingRow';
import { useTx } from './text';

export type SettingsMode = 'basic' | 'advanced';

const TILE_ICON: Record<string, IconName> = {
  'items.enabled': 'items', 'enemies.enabled': 'enemies', 'fog.enabled': 'fog', 'enemies.bosses': 'bosses',
  'items.npcGifts': 'items', 'items.shops': 'items', 'output.dlc': 'crown', 'output.spoiler': 'doc',
};
const SECTION_ICON: Record<SectionId, IconName> = { items: 'items', combat: 'enemies', transitions: 'fog', keys: 'keys', classic: 'gear', other: 'other' };
const SUBSECTION_ICON: Record<CombatSubsectionId, IconName> = { regularEnemies: 'enemies', enemySlots: 'other', arenaBosses: 'bosses' };

interface ViewProps { config: RandomizerConfig; onChange: (id: string, value: unknown) => void }

export function ModeSwitch({ mode, onMode }: { mode: SettingsMode; onMode: (mode: SettingsMode) => void }) {
  const { t } = useTranslation();
  return (
    <div className="mode-switch" role="group" aria-label={t('app.mode.label')}>
      <button type="button" className={mode === 'basic' ? 'is-active' : undefined} aria-pressed={mode === 'basic'} onClick={() => onMode('basic')}>
        <Icon name="star" /><span><strong>{t('app.mode.basic.title')}</strong><small>{t('app.mode.basic.note')}</small></span>
      </button>
      <button type="button" className={mode === 'advanced' ? 'is-active' : undefined} aria-pressed={mode === 'advanced'} onClick={() => onMode('advanced')}>
        <Icon name="sliders" /><span><strong>{t('app.mode.advanced.title')}</strong><small>{t('app.mode.advanced.note')}</small></span>
      </button>
    </div>
  );
}

/**
 * The basic view: the few options most players use. It is another view of the same configuration as the advanced one: it never changes an advanced value, and when the
 * advanced values differ from the recommended ones it says so and offers the way to them.
 */
export function BasicView({ config, onChange, onShowAdvanced, children }: ViewProps & { onShowAdvanced: () => void; children?: React.ReactNode }) {
  const { t } = useTranslation();
  const custom = customAdvancedSettings(config);
  const tile = (id: string) => (
    <div key={id} className="tile">
      <span className="tile-icon"><Icon name={TILE_ICON[id] ?? 'gear'} size={24} /></span>
      <SettingRow def={settingById(id)} config={config} onChange={onChange} describe />
    </div>
  );
  return (
    <div className="basic-view">
      {custom.length > 0 && (
        <div className="notice custom-notice" role="status">
          <span>{t('app.basic.customActive')}</span>
          <button type="button" className="secondary" onClick={onShowAdvanced}>{t('app.basic.showAdvanced')}</button>
        </div>
      )}
      <section className="card" aria-labelledby="basic-main">
        <header className="card-head"><Icon name="items" size={26} /><div><h2 id="basic-main">{t('app.basic.main.title')}</h2><p>{t('app.basic.main.note')}</p></div></header>
        <div className="tile-grid">{BASIC_LAYOUT.main.map(tile)}</div>
      </section>
      <section className="card" aria-labelledby="basic-common">
        <header className="card-head"><Icon name="gear" size={26} /><div><h2 id="basic-common">{t('app.basic.common.title')}</h2><p>{t('app.basic.common.note')}</p></div></header>
        <div className="tile-grid">{BASIC_LAYOUT.common.map(tile)}</div>
      </section>
      <section className="card" aria-labelledby="basic-dlc">
        <header className="card-head"><Icon name="crown" size={26} /><div><h2 id="basic-dlc">{t('app.basic.dlc.title')}</h2><p>{t('app.basic.dlc.note')}</p></div></header>
        <div className="tile-grid single">{[BASIC_LAYOUT.world[0]].map(tile)}</div>
      </section>
      <section className="card" aria-labelledby="basic-output">
        <header className="card-head"><Icon name="doc" size={26} /><div><h2 id="basic-output">{t('app.basic.output.title')}</h2><p>{t('app.basic.output.note')}</p></div></header>
        <div className="tile-grid single">{[BASIC_LAYOUT.world[1]].map(tile)}</div>
        <p className="spoiler-warning" role="note"><Icon name="warning" size={18} /><span>{t('app.basic.output.warning')}</span></p>
      </section>
      {children}
    </div>
  );
}

/** The advanced view: every option, grouped in sections, each with its own explanation. */
export function AdvancedView({ config, onChange, onBackToBasic, openSections }: ViewProps & { onBackToBasic: () => void; openSections: SectionId[] }) {
  const { t } = useTranslation();
  const { tx } = useTx();
  const [open, setOpen] = useState<Set<SectionId>>(() => new Set(openSections));
  const [jumpTarget, setJumpTarget] = useState<string | null>(null);
  // Coming from the basic notice opens the sections that hold the custom values; the person's own choices are kept otherwise.
  useEffect(() => { if (openSections.length > 0) setOpen((current) => new Set([...current, ...openSections])); }, [openSections]);
  useEffect(() => {
    if (!jumpTarget) return;
    const row = document.querySelector<HTMLElement>(`[data-setting="${jumpTarget}"]`);
    if (row) {
      row.scrollIntoView({ behavior: 'smooth', block: 'center' });
      row.querySelector<HTMLElement>('input:not(:disabled), select:not(:disabled), button:not(:disabled)')?.focus({ preventScroll: true });
    }
    setJumpTarget(null);
  }, [jumpTarget, open]);
  const toggle = (id: SectionId) => setOpen((current) => { const next = new Set(current); if (!next.delete(id)) next.add(id); return next; });
  const jumpToSetting = (id: string) => {
    const target = settingById(id);
    setOpen((current) => new Set([...current, target.section]));
    setJumpTarget(id);
  };
  const customIds = new Set(customAdvancedSettings(config));
  return (
    <div className="advanced-view">
      <header className="advanced-head">
        <div><h2>{t('app.advancedView.title')}</h2><p>{t('app.advancedView.note')}</p></div>
        <button type="button" className="secondary" onClick={onBackToBasic}><Icon name="back" size={16} /> {t('app.advancedView.back')}</button>
      </header>
      <div className="accordion">
        {SECTION_IDS.map((section) => {
          const settings = settingsOfSection(section);
          const isOpen = open.has(section);
          const customHere = settings.filter((setting) => customIds.has(setting.id)).length;
          const renderSetting = (setting: (typeof settings)[number]) => <SettingRow key={setting.id} def={setting} config={config} onChange={onChange} onJumpToSetting={jumpToSetting} />;
          return (
            <section key={section} className={`accordion-item${isOpen ? ' is-open' : ''}`} data-section={section}>
              <h3>
                <button type="button" aria-expanded={isOpen} aria-controls={`section-${section}`} onClick={() => toggle(section)}>
                  <span className="accordion-icon"><Icon name={SECTION_ICON[section]} /></span>
                  <span className="accordion-title">{tx(`settings.sections.${section}.title`)}</span>
                  {customHere > 0 && <span className="pill custom-pill">{t('app.advancedView.changed', { count: customHere })}</span>}
                  <span className="accordion-count">{t('app.advancedView.options', { count: settings.length })}</span>
                  <span className="accordion-chevron"><Icon name="chevron" size={18} /></span>
                </button>
              </h3>
              {isOpen && (
                <div id={`section-${section}`} className="accordion-body">
                  <p className="section-note">{tx(`settings.sections.${section}.note`)}</p>
                  {section === 'combat' ? (
                    <>
                      <div className="combat-master option-list">{settings.filter((setting) => !setting.subsection).map(renderSetting)}</div>
                      {COMBAT_SUBSECTION_IDS.map((subsection) => {
                        const subsectionSettings = settingsOfSubsection(subsection);
                        return (
                          <section key={subsection} className={`combat-subsection combat-subsection-${subsection}`} aria-labelledby={`combat-${subsection}`} data-subsection={subsection}>
                            <header className="combat-subsection-head">
                              <span className="combat-subsection-icon"><Icon name={SUBSECTION_ICON[subsection]} size={17} /></span>
                              <div>
                                <h4 id={`combat-${subsection}`}>{tx(`settings.subsections.${subsection}.title`)}</h4>
                                <p>{tx(`settings.subsections.${subsection}.note`)}</p>
                              </div>
                            </header>
                            <div className="option-list">{subsectionSettings.map(renderSetting)}</div>
                          </section>
                        );
                      })}
                    </>
                  ) : (
                    <div className="option-list">{settings.map(renderSetting)}</div>
                  )}
                </div>
              )}
            </section>
          );
        })}
      </div>
    </div>
  );
}

export { sectionsWithCustomSettings };
