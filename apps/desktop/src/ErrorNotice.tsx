import { useTranslation } from 'react-i18next';
import { problemKey } from '@ds1tr/contracts';

/**
 * What went wrong, as data: the backend sends a stable `code` (plus an English `detail` for the technical section); the renderer picks
 * the text in the current language. English sentences are never parsed. An unknown code gets the localized generic message and the
 * original text as expandable technical detail.
 */
export interface ErrorInfo {
  code?: string;
  /** The backend's own (English) text or an exception message: only ever shown as technical detail. */
  detail?: string;
  problems?: string[];
  /** `unexpected`: the renderer itself failed (no backend code). */
  kind?: 'unexpected';
}

/** The key under `errors.codes` for a code, or null when no translation exists. */
export function errorCodeKey(code: string | undefined, exists: (key: string) => boolean): string | null {
  if (!code) return null;
  const key = `errors.codes.${code}`;
  return exists(key) ? key : null;
}

/** The message for an error in the current language: its own text when the code is known, else a localized generic one. */
export function useErrorText(): (error: ErrorInfo) => string {
  const { t, i18n } = useTranslation();
  return (error) => {
    const key = errorCodeKey(error.code, (candidate) => i18n.exists(candidate));
    if (key) return t(key as never) as string;
    if (error.kind === 'unexpected') return t('errors.unexpected');
    return error.code?.startsWith('FOG_') || error.code?.startsWith('WORLD_DATA_') ? t('errors.genericWorld') : t('errors.generic');
  };
}

/** A sentence of a backend validator in the current language when it is a known one, else exactly as the validator wrote it. */
export function useProblemText(): (problem: string) => string {
  const { t } = useTranslation();
  return (problem) => {
    const found = problemKey(problem);
    return found ? String(t(`errors.problems.${found.key}` as never, (found.params ?? {}) as never)) : problem;
  };
}

export function ErrorNotice({ error }: { error: ErrorInfo }) {
  const { t, i18n } = useTranslation();
  const errorText = useErrorText();
  const key = errorCodeKey(error.code, (candidate) => i18n.exists(candidate));
  const text = errorText(error);
  const problems = error.problems ?? [];
  const showDetails = Boolean(error.detail) || problems.length > 0 || (!key && Boolean(error.code));
  return (
    <div className="notice error" role="alert">
      {text}
      {showDetails && (
        <details>
          <summary>{t('errors.technicalDetails')}</summary>
          {error.code && <p><code>{error.code}</code></p>}
          {error.detail && <pre className="run-logs">{error.detail}</pre>}
          {problems.length > 0 && <ul>{problems.map((problem) => <li key={problem}>{problem}</li>)}</ul>}
        </details>
      )}
    </div>
  );
}
