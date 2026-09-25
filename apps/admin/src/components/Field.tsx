import { useId, type InputHTMLAttributes, type ReactNode } from 'react';

/** Invoerveld met gekoppeld label en optionele toelichting (toegankelijk). */
export function Field({
  label,
  hint,
  ...input
}: { label: string; hint?: string } & InputHTMLAttributes<HTMLInputElement>) {
  const id = useId();
  const hintId = `${id}-hint`;
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <input id={id} aria-describedby={hint ? hintId : undefined} {...input} />
      {hint ? (
        <small id={hintId} className="muted">
          {hint}
        </small>
      ) : null}
    </div>
  );
}

export function Checkbox({ label, ...input }: { label: ReactNode } & InputHTMLAttributes<HTMLInputElement>) {
  const id = useId();
  return (
    <div className="checkbox">
      <input id={id} type="checkbox" {...input} />
      <label htmlFor={id}>{label}</label>
    </div>
  );
}
