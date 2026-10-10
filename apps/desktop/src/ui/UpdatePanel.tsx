import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { UpdateState } from '../../electron/updater';
import type { UpdatesApi } from './useUpdates';

/** The words for one state, shared by the card and the notice. */
function useUpdateWords(state: UpdateState | null, refused: string | null) {
  const { t, i18n } = useTranslation();
  if (!state) return { line: t('app.update.unsupported.development'), detail: null as string | null, blocked: false, when: null as string | null };
  const version = state.availableVersion ?? '';
  const when = state.lastCheckAt ? new Date(state.lastCheckAt).toLocaleString(i18n.language === 'pl' ? 'pl-PL' : 'en-GB') : null;
  let line: string;
  switch (state.phase) {
    case 'checking': line = t('app.update.checking'); break;
    case 'upToDate': line = t('app.update.upToDate'); break;
    case 'noStableRelease': line = t('app.update.noStableRelease'); break;
    case 'available': line = t(state.capability === 'manual' ? 'app.update.availableManual' : 'app.update.available', { version }); break;
    case 'downloading': line = t('app.update.downloading', { percent: state.progress ?? 0 }); break;
    case 'downloaded': line = t('app.update.ready', { version }); break;
    case 'installing': line = t('app.update.installing'); break;
    case 'unsupported': line = t(`app.update.unsupported.${state.unsupportedReason ?? 'development'}`); break;
    case 'error': line = t(`app.update.errors.${state.error?.code ?? 'CHECK_FAILED'}`); break;
    default: line = when ? t('app.update.lastChecked', { when }) : t('app.update.neverChecked');
  }
  const blocked = state.installBlocked || refused === 'OPERATION_ACTIVE';
  return { line, detail: state.phase === 'error' ? state.error?.detail ?? null : null, blocked, when: when as string | null };
}

/** The buttons that fit the state. A portable build never offers a download or a restart: it opens the trusted release page instead. */
function UpdateActions({ updates, later }: { updates: UpdatesApi; later?: () => void }) {
  const { t } = useTranslation();
  const { state } = updates;
  if (!state || state.distribution !== 'github' || state.phase === 'unsupported') return null;
  const manual = state.capability === 'manual';
  const working = state.phase === 'checking' || state.phase === 'downloading' || state.phase === 'installing';
  return (
    <div className="run-actions update-actions">
      {(state.phase === 'available' || (state.phase === 'error' && state.availableVersion !== null)) && !manual && (
        <button type="button" onClick={() => void updates.download()} disabled={working}>{t('app.update.download')}</button>
      )}
      {state.phase === 'downloaded' && <button type="button" onClick={() => void updates.install()}>{t('app.update.restart')}</button>}
      {state.availableVersion !== null && state.phase !== 'installing' && (
        <button type="button" className="secondary" onClick={updates.openReleasePage}>{manual ? t('app.update.openPage') : t('app.update.whatsNew')}</button>
      )}
      {later && (state.phase === 'available' || state.phase === 'downloaded') && <button type="button" className="secondary" onClick={later}>{t('app.update.later')}</button>}
    </div>
  );
}

/**
 * Updates in the About view and in the app settings (docs/AUTO_UPDATE.md). The switch for the automatic check is shown only where `showAutoCheck` is set. Every
 * failure is a calm sentence; none of them blocks anything else in the application.
 */
export function UpdateCard({ updates, showAutoCheck }: { updates: UpdatesApi; showAutoCheck: boolean }) {
  const { t } = useTranslation();
  const { state } = updates;
  const words = useUpdateWords(state, updates.refused);
  const working = state?.phase === 'checking' || state?.phase === 'downloading' || state?.phase === 'installing';
  if (state?.distribution === 'nexus') {
    return (
      <section className="card update-card" aria-label={t('app.update.title')}>
        <h2>{t('app.update.title')}</h2>
        <p>{t('app.update.nexus.note')}</p>
        <p className="update-line" role="status">{t('app.update.installedVersion', { version: state.currentVersion })}</p>
        {state.nexusPageUrl && <div className="run-actions"><button type="button" className="secondary" onClick={updates.openNexusPage}>{t('app.update.nexus.openPage')}</button></div>}
      </section>
    );
  }
  if (state?.distribution === 'unavailable') {
    return (
      <section className="card update-card" aria-label={t('app.update.title')}>
        <h2>{t('app.update.title')}</h2>
        <p className="update-line" role="status">{t('app.update.metadataUnavailable')}</p>
        <p>{t('app.update.installedVersion', { version: state.currentVersion })}</p>
      </section>
    );
  }
  const canCheck = state !== null && state.distribution === 'github' && state.phase !== 'unsupported';
  return (
    <section className="card update-card" aria-label={t('app.update.title')}>
      <h2>{t('app.update.title')}</h2>
      <p>{t('app.update.note')}</p>
      <p className="update-line" role="status" aria-live="polite">{words.line}</p>
      {state?.phase === 'downloading' && <progress max={100} value={state.progress ?? 0} aria-label={words.line} />}
      {state?.phase !== 'checking' && state?.phase !== 'idle' && words.when && state?.phase !== 'error' && state?.phase !== 'unsupported' && <p className="update-when">{t('app.update.lastChecked', { when: words.when })}</p>}
      {words.blocked && <div className="notice" role="alert">{t('app.update.blocked')}</div>}
      {state?.capability === 'manual' && state.phase === 'available' && <p>{t('app.update.portableNote')}</p>}
      {words.detail && <details className="update-details"><summary>{t('app.update.details')}</summary><code>{words.detail}</code></details>}
      <UpdateActions updates={updates} />
      {canCheck && <div className="run-actions"><button type="button" className="secondary" onClick={() => void updates.check()} disabled={working}>{state?.phase === 'checking' ? t('app.update.checking') : t('app.update.check')}</button></div>}
      {showAutoCheck && state && state.capability !== 'none' && (
        <label className="update-auto">
          <input type="checkbox" role="switch" className="switch" checked={state.autoCheck} onChange={(event) => void updates.setAutoCheck(event.target.checked)} />
          <span><strong>{t('app.update.autoCheck')}</strong><small>{t('app.update.autoCheckNote')}</small></span>
        </label>
      )}
    </section>
  );
}

/** A short line at the top of the page when a version is waiting; "Later" hides it for the current state only. Never shown while the person is already in the update card. */
export function UpdateNotice({ updates, hidden }: { updates: UpdatesApi; hidden: boolean }) {
  const { t } = useTranslation();
  const [dismissed, setDismissed] = useState<string | null>(null);
  const { state } = updates;
  const words = useUpdateWords(state, updates.refused);
  if (hidden || !state || state.distribution !== 'github' || !['available', 'downloading', 'downloaded'].includes(state.phase) || state.availableVersion === null) return null;
  const key = `${state.phase}:${state.availableVersion}`;
  if (dismissed === key) return null;
  return (
    <div className="notice update-notice" role="status">
      <span>{state.phase === 'downloaded' ? t('app.update.noticeReady', { version: state.availableVersion }) : state.phase === 'downloading' ? words.line : t('app.update.noticeAvailable', { version: state.availableVersion })}</span>
      {words.blocked && <span> {t('app.update.blocked')}</span>}
      <UpdateActions updates={updates} later={() => setDismissed(key)} />
    </div>
  );
}
