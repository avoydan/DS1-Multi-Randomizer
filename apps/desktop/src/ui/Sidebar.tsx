import { useTranslation } from 'react-i18next';
import { Icon, type IconName } from './icons';

export type ViewId = 'game' | 'presets' | 'seed' | 'transfer' | 'settings' | 'about';

const MAIN: Array<{ id: ViewId; icon: IconName }> = [
  { id: 'game', icon: 'game' }, { id: 'presets', icon: 'preset' }, { id: 'seed', icon: 'seed' }, { id: 'transfer', icon: 'transfer' },
];
const FOOT: Array<{ id: ViewId; icon: IconName }> = [{ id: 'settings', icon: 'gear' }, { id: 'about', icon: 'info' }];

interface Props { view: ViewId; onView: (view: ViewId) => void; backend: boolean | null; version: string; logo: string }

/** The navigation. Choosing a destination only changes what is shown; it never touches the configuration. */
export function Sidebar({ view, onView, backend, version, logo }: Props) {
  const { t } = useTranslation();
  const entry = ({ id, icon }: { id: ViewId; icon: IconName }) => (
    <li key={id}>
      <button type="button" className={view === id ? 'is-active' : undefined} aria-current={view === id ? 'page' : undefined} onClick={() => onView(id)}>
        <Icon name={icon} /><span>{t(`app.nav.${id}`)}</span>
      </button>
    </li>
  );
  return (
    <nav className="sidebar" aria-label={t('app.nav.label')}>
      <div className="brand">
        <img src={logo} alt="" aria-hidden="true" draggable={false} />
        <div><span className="brand-top">DS1</span><strong>{t('app.header.title')}</strong></div>
      </div>
      <ul className="nav-list">{MAIN.map(entry)}</ul>
      <div className="sidebar-foot">
        <ul className="nav-list small">{FOOT.map(entry)}</ul>
        <div className={`status ${backend ? 'online' : 'offline'}`} role="status"><span aria-hidden="true" /> {backend === null ? t('app.status.starting') : backend ? t('app.status.ready') : t('app.status.offline')}</div>
        <small className="version">{t('app.nav.version', { version })}</small>
      </div>
    </nav>
  );
}
