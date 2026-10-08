import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { buildDiagnosticsText, type Diagnostics, type Translate } from '@ds1tr/contracts';
import { exportSupportBundle } from './supportBundle';
import { api } from './api';
import { ErrorNotice, useErrorText, type ErrorInfo } from './ErrorNotice';
import { currentLanguage } from './i18n';
import { formatDateTime } from './i18n/format';
import { shortSeedCode } from './SeedPanel';

interface Props {
  gamePath: string;
  /** A world of the history to describe; without it the applied world, else the newest. */
  worldId?: string | null;
  /** Changes when the world or the game changed, so the summary is read again. */
  refreshKey?: string;
  /** Shown as a dialog when given (the history's "Diagnostics"); inline in Advanced otherwise. */
  onClose?: () => void;
}

/**
 * Diagnostics and support (docs/DIAGNOSTICS.md, docs/SUPPORT_BUNDLE.md): a friendly summary first, the technical details behind a toggle, "Copy
 * diagnostics" and "Export support bundle". The model comes from the backend already sanitized; the texts are built here in the app language.
 */
export function DiagnosticsPanel({ gamePath, worldId, refreshKey, onClose }: Props) {
  const { t } = useTranslation();
  const errorText = useErrorText();
  const [diagnostics, setDiagnostics] = useState<Diagnostics | null>(null);
  const [failed, setFailed] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [exportError, setExportError] = useState<ErrorInfo | null>(null);
  const [working, setWorking] = useState(false);

  const translate: Translate = (key, params) => t(key as never, params as never) as unknown as string;
  const formatTime = (iso: string): string => formatDateTime(iso, currentLanguage());

  const load = useCallback(async () => {
    setFailed(false);
    try {
      const result = await api.diagnostics(gamePath, currentLanguage(), worldId ?? undefined);
      setDiagnostics(result.diagnostics);
    } catch { setFailed(true); }
  }, [gamePath, worldId, refreshKey]);

  useEffect(() => { void load(); }, [load]);

  const text = diagnostics ? buildDiagnosticsText(diagnostics, translate, { formatTime }) : '';

  const copy = async () => {
    try { await navigator.clipboard.writeText(text); setMessage(t('diagnostics.panel.copied')); } catch { setMessage(t('diagnostics.panel.copyFailed')); }
  };

  const exportBundle = async () => {
    setWorking(true);
    setMessage(null);
    setExportError(null);
    const outcome = await exportSupportBundle(gamePath, worldId ?? undefined, translate);
    setWorking(false);
    if (outcome.kind === 'refused') setExportError({ code: outcome.code });
    else setMessage(outcome.kind === 'saved' ? t('support.ui.saved', { name: outcome.name, count: outcome.count, size: outcome.size }) : outcome.kind === 'cancelled' ? t('support.ui.cancelled') : t('support.ui.failed'));
  };

  const failure = diagnostics?.generation.lastFailure ?? null;
  const body = (
    <div className="diagnostics">
      <p className="field-note">{t('diagnostics.panel.intro')}</p>
      {!diagnostics && !failed && <p className="field-note">{t('diagnostics.panel.loading')}</p>}
      {failed && <div className="notice error" role="alert">{t('diagnostics.panel.failed')}</div>}
      {diagnostics && (
        <>
          <div className={`notice ${failure ? 'error' : 'success'}`} role="status">
            <strong>{failure ? t('diagnostics.panel.lastFailure') : t('diagnostics.panel.healthy')}</strong>
            {failure?.code && <span>{errorText({ code: failure.code })} <code>{failure.code}</code></span>}
          </div>
          <dl className="hint-meta">
            <dt>{t('diagnostics.field.application')}</dt><dd>DS1 Multi Randomizer {diagnostics.application.desktopVersion ?? diagnostics.application.backendVersion}</dd>
            <dt>{t('diagnostics.field.installMode')}</dt><dd>{t(`diagnostics.installMode.${diagnostics.application.installMode}`)}</dd>
            <dt>{t('diagnostics.field.signing')}</dt><dd>{t(`diagnostics.signing.${diagnostics.release.signing}`)}</dd>
            <dt>{t('diagnostics.field.game')}</dt><dd>{diagnostics.game.state ? t(`diagnostics.gameState.${diagnostics.game.state}`) : t('diagnostics.gameState.unknown')}</dd>
            <dt>{t('diagnostics.field.world')}</dt>
            <dd>{diagnostics.world ? <><code title={diagnostics.world.seedCode}>{shortSeedCode(diagnostics.world.seedCode)}</code> ({t(`diagnostics.worldStatus.${diagnostics.world.status}`)})</> : t('diagnostics.noWorld')}</dd>
          </dl>
          <details className="log-panel">
            <summary>{t('diagnostics.panel.technicalToggle')}</summary>
            <pre className="run-logs" aria-label={t('diagnostics.section.technical')}>{text}</pre>
          </details>
        </>
      )}
      {exportError && <ErrorNotice error={exportError} />}
      {message && <p className="field-note" role="status">{message}</p>}
      <div className="run-actions">
        <button className="secondary" onClick={() => void copy()} disabled={!diagnostics}>{t('diagnostics.panel.copy')}</button>
        <button className="secondary" onClick={() => void exportBundle()} disabled={!diagnostics || working}>{working ? t('support.ui.exporting') : t('support.ui.export')}</button>
        <button className="secondary" onClick={() => void load()}>{t('diagnostics.panel.refresh')}</button>
      </div>
      <p className="field-note">{t('support.ui.note')}</p>
    </div>
  );

  if (!onClose) return body;
  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose(); }}>
      <div className="modal" role="dialog" aria-modal="true" aria-labelledby="diagnostics-title">
        <div className="modal-header">
          <h2 id="diagnostics-title">{diagnostics?.world ? t('diagnostics.panel.forWorld', { seed: shortSeedCode(diagnostics.world.seedCode) }) : t('diagnostics.panel.title')}</h2>
          <button className="secondary" onClick={onClose}>{t('common.close')}</button>
        </div>
        {body}
      </div>
    </div>
  );
}
