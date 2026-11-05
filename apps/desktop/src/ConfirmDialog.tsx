import { useEffect, useRef, type ReactNode } from 'react';

interface Props {
  title: string;
  /** What will happen, in plain words: the person decides from this text. */
  children: ReactNode;
  confirmLabel: string;
  cancelLabel: string;
  /** The confirmation removes something: the button is styled as dangerous. */
  danger?: boolean;
  busy?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

/** A blocking yes/no dialog. Cancelling (the button, Escape, a click outside) does nothing else; the safe choice has the focus. */
export function ConfirmDialog({ title, children, confirmLabel, cancelLabel, danger = false, busy = false, onConfirm, onCancel }: Props) {
  const cancelRef = useRef<HTMLButtonElement | null>(null);
  useEffect(() => { cancelRef.current?.focus(); }, []);
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape' && !busy) onCancel(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [busy, onCancel]);
  return (
    <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget && !busy) onCancel(); }}>
      <div className="modal confirm-dialog" role="alertdialog" aria-modal="true" aria-labelledby="confirm-dialog-title" aria-describedby="confirm-dialog-body">
        <h2 id="confirm-dialog-title">{title}</h2>
        <div id="confirm-dialog-body" className="confirm-dialog-body">{children}</div>
        <div className="run-actions confirm-dialog-actions">
          <button ref={cancelRef} className="secondary" disabled={busy} onClick={onCancel}>{cancelLabel}</button>
          <button className={danger ? 'danger-action' : 'primary'} disabled={busy} onClick={onConfirm}>{confirmLabel}</button>
        </div>
      </div>
    </div>
  );
}
