import { useEffect, useRef, type ReactNode } from 'react';

/**
 * Modale dialoog op basis van het native &lt;dialog&gt;-element: focus-trap, Escape en de juiste rol zonder extra
 * bibliotheek.
 */
export function Dialog({
  open,
  title,
  onClose,
  children,
}: {
  open: boolean;
  title: string;
  onClose: () => void;
  children: ReactNode;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) {
      return;
    }
    if (open && !dialog.open) {
      dialog.showModal?.();
    } else if (!open && dialog.open) {
      dialog.close?.();
    }
  }, [open]);

  return (
    <dialog ref={ref} aria-labelledby="dialog-title" onClose={onClose} className="dialog">
      {open ? (
        <>
          <h2 id="dialog-title">{title}</h2>
          {children}
        </>
      ) : null}
    </dialog>
  );
}

/** Bevestiging voor destructieve of gevoelige acties (docs/02 §6). */
export function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel,
  onConfirm,
  onCancel,
  busy,
}: {
  open: boolean;
  title: string;
  message: string;
  confirmLabel: string;
  onConfirm: () => void;
  onCancel: () => void;
  busy?: boolean;
}) {
  return (
    <Dialog open={open} title={title} onClose={onCancel}>
      <p>{message}</p>
      <div className="actions">
        <button type="button" className="button secondary" onClick={onCancel}>
          Annuleren
        </button>
        <button type="button" className="button danger" onClick={onConfirm} disabled={busy}>
          {confirmLabel}
        </button>
      </div>
    </Dialog>
  );
}
