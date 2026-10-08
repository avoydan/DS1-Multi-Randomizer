import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import type { GenerationSettings } from '@ds1tr/contracts';
import { LanguageSelect } from '../LanguageSelect';
import { SeedPanel, SummaryChips, type LoadedSeed } from '../SeedPanel';

/** The destinations of the sidebar other than "Your game". They show and move text; none of them changes how a world is made. */

export function ViewHeader({ title, note }: { title: string; note?: string }) {
  return <header className="view-head"><h1>{title}</h1>{note && <p>{note}</p>}</header>;
}

export function PresetsView({ settings, onResetDefaults, onGoGame, message }: { settings: GenerationSettings; onResetDefaults: () => void; onGoGame: () => void; message: string }) {
  const { t } = useTranslation();
  return (
    <>
      <ViewHeader title={t('app.views.presets.title')} note={t('app.views.presets.note')} />
      <section className="card">
        <h2>{t('app.views.presets.currentTitle')}</h2>
        <SummaryChips settings={settings} />
        <p>{t('app.views.presets.explain')}</p>
        <div className="run-actions">
          <button type="button" className="secondary" onClick={onResetDefaults}>{t('app.views.presets.reset')}</button>
          <button type="button" className="secondary" onClick={onGoGame}>{t('app.views.presets.back')}</button>
        </div>
        {message && <p className="notice success" role="status">{message}</p>}
      </section>
      <section className="card">
        <h2>{t('app.views.presets.differenceTitle')}</h2>
        <dl className="definition-list">
          <dt>{t('app.views.presets.presetTerm')}</dt><dd>{t('app.views.presets.presetText')}</dd>
          <dt>{t('app.views.presets.seedTerm')}</dt><dd>{t('app.views.presets.seedText')}</dd>
        </dl>
      </section>
    </>
  );
}

export function TransferView({ text, onText, onExport, onImport, problems, message }: {
  text: string; onText: (text: string) => void; onExport: () => void; onImport: () => void; problems: ReactNode; message: string;
}) {
  const { t } = useTranslation();
  return (
    <>
      <ViewHeader title={t('app.views.transfer.title')} note={t('app.views.transfer.note')} />
      <section className="card">
        <textarea className="run-logs" aria-label={t('app.preset.aria')} rows={12} value={text} onChange={(event) => onText(event.target.value)} placeholder={t('app.preset.placeholder')} />
        <div className="run-actions">
          <button type="button" className="secondary" onClick={onExport}>{t('app.preset.export')}</button>
          <button type="button" className="secondary" onClick={onImport} disabled={!text.trim()}>{t('app.preset.apply')}</button>
        </div>
        {message && <p className="notice success" role="status">{message}</p>}
        {problems}
        <p className="field-note">{t('app.views.transfer.noSeed')}</p>
      </section>
    </>
  );
}

export function SeedCodeView({ current, busy, onLoaded, onCopy, copied }: {
  current: string | null; busy: boolean; onLoaded: (seed: LoadedSeed) => void; onCopy: (code: string) => void; copied: boolean;
}) {
  const { t } = useTranslation();
  return (
    <>
      <ViewHeader title={t('app.views.seed.title')} note={t('app.views.seed.note')} />
      <section className="card">
        <h2>{t('app.views.seed.currentTitle')}</h2>
        {current
          ? (<>
            <textarea className="run-logs seed-output" readOnly rows={2} aria-label={t('app.run.seedAria')} value={current} onFocus={(event) => event.currentTarget.select()} />
            <div className="run-actions"><button type="button" className="secondary" onClick={() => onCopy(current)}>{copied ? t('common.copied') : t('common.copySeed')}</button></div>
          </>)
          : <p>{t('app.views.seed.none')}</p>}
      </section>
      <section className="card">
        <h2>{t('app.views.seed.loadTitle')}</h2>
        <p>{t('app.views.seed.loadExplain')}</p>
        <SeedPanel busy={busy} onLoaded={onLoaded} onClose={() => undefined} />
      </section>
      <section className="card">
        <h2>{t('app.views.seed.whatTitle')}</h2>
        <p>{t('app.views.seed.whatText')}</p>
      </section>
    </>
  );
}

export function AppSettingsView({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  return (
    <>
      <ViewHeader title={t('app.views.settings.title')} note={t('app.views.settings.note')} />
      <section className="card"><h2>{t('app.views.settings.languageTitle')}</h2><p>{t('app.views.settings.languageNote')}</p><LanguageSelect /></section>
      {children}
    </>
  );
}

export function AboutView({ version, logo }: { version: string; logo: string }) {
  const { t } = useTranslation();
  return (
    <>
      <ViewHeader title={t('app.views.about.title')} />
      <section className="card about-card">
        <img src={logo} alt="" aria-hidden="true" draggable={false} />
        <div>
          <div className="eyebrow">{t('app.legal.eyebrow')}</div>
          <h2>{t('app.header.title')}</h2>
          <p>{t('app.views.about.version', { version })}</p>
          <p>{t('app.header.tagline')}</p>
        </div>
      </section>
      <section className="card legal-card">
        <h2>{t('app.legal.title')}</h2>
        <p>{t('app.legal.copyright')}</p>
        <p>{t('app.legal.disclaimer')}</p>
        <a href="https://www.gnu.org/licenses/gpl-3.0.html" target="_blank" rel="noreferrer">{t('app.legal.readLicense')}</a>
      </section>
    </>
  );
}
