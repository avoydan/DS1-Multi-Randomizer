import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { renderSolverRoute, renderSolverSteps, renderSolverTree, type SolverWitnessDocument } from '@ds1tr/contracts';
import { api } from './api';
import { shortSeedCode } from './SeedPanel';

interface Props {
  /** The history entry whose solver route is shown. */
  entryId: string;
  onClose: () => void;
}

type Status = { kind: 'loading' } | { kind: 'unavailable' } | { kind: 'ready'; document: SolverWitnessDocument };

/**
 * The solver route (docs/SOLVER_WITNESS.md): the exact route the solver used to accept this world, step by step, with BOTH sides of every gate, the
 * solver's requirements and where it assumes key items are obtained. It only shows what FogHost recorded while accepting the world; nothing here can
 * change a world. The text is the same Markdown that is saved as `solver-route.md` next to the run, so what is copied is what support receives.
 */
export function SolverRoutePanel({ entryId, onClose }: Props) {
  const { t } = useTranslation();
  const [status, setStatus] = useState<Status>({ kind: 'loading' });
  const [showTree, setShowTree] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setStatus({ kind: 'loading' });
    api.solverRoute(entryId)
      .then((result) => { if (!cancelled) setStatus(result.ok ? { kind: 'ready', document: result.document } : { kind: 'unavailable' }); })
      .catch(() => { if (!cancelled) setStatus({ kind: 'unavailable' }); });
    return () => { cancelled = true; };
  }, [entryId]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  const document = status.kind === 'ready' ? status.document : null;
  const steps = useMemo(() => (document ? renderSolverSteps(document).join('\n\n') : ''), [document]);
  const copy = async () => {
    if (!document) return;
    try {
      await navigator.clipboard.writeText(renderSolverRoute(document, { includeTree: showTree }));
      setMessage(t('solver.viewer.copied'));
    } catch { setMessage(t('solver.viewer.copyFailed')); }
  };
  const context = document?.context;

  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose(); }}>
      <div className="modal solver-route" role="dialog" aria-modal="true" aria-labelledby="solver-route-title">
        <div className="modal-header">
          <h2 id="solver-route-title">{t('solver.viewer.title')}</h2>
          <div className="run-actions">
            {document && <button className="secondary" onClick={() => void copy()}>{t('solver.viewer.copy')}</button>}
            <button className="secondary" onClick={onClose}>{t('solver.viewer.close')}</button>
          </div>
        </div>
        {status.kind === 'loading' && <p className="field-note">{t('solver.viewer.loading')}</p>}
        {status.kind === 'unavailable' && <div className="notice">{t('solver.viewer.unavailable')}</div>}
        {document && (
          <>
            <p className="hint-warning">{t('solver.viewer.warning')}</p>
            <p className="field-note">{t('solver.viewer.intro')}</p>
            <dl className="hint-meta">
              {context && <><dt>{t('solver.viewer.seedCode')}</dt><dd><code title={context.seedCode}>{shortSeedCode(context.seedCode)}</code></dd></>}
              {context && <><dt>{t('solver.viewer.profile')}</dt><dd><code>{context.generationProfile}</code></dd></>}
              {context && <><dt>{t('solver.viewer.fogSeed')}</dt><dd><code>{context.fogSeed ?? '—'}</code></dd></>}
              {context && <><dt>{t('solver.viewer.attempt')}</dt><dd>{t('solver.viewer.attemptValue', { fog: context.fogAttempt === null ? '—' : context.fogAttempt + 1, combined: context.combinedAttempt === null ? '—' : context.combinedAttempt + 1 })}</dd></>}
              <dt>{t('solver.viewer.claim')}</dt>
              <dd>{t('solver.viewer.claimValue', { reached: document.claim.areasReached, total: document.claim.areasTotal, target: document.target.area })}</dd>
            </dl>
            <h3>{t('solver.viewer.route')}</h3>
            <pre className="solver-route-text" tabIndex={0}>{steps}</pre>
            <label className="field-note solver-tree-toggle">
              <input type="checkbox" checked={showTree} onChange={(event) => setShowTree(event.target.checked)} /> {t('solver.viewer.showTree')}
            </label>
            {showTree && (
              <>
                <h3>{t('solver.viewer.tree')}</h3>
                <pre className="solver-route-text" tabIndex={0}>{renderSolverTree(document).join('\n')}</pre>
              </>
            )}
            {message && <p className="field-note" role="status">{message}</p>}
          </>
        )}
      </div>
    </div>
  );
}
