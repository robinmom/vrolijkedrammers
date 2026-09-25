import { describeProblem } from '../api/errors';

/** Foutmelding uit ProblemDetails, voorgelezen door schermlezers (role=alert). */
export function ProblemAlert({ error }: { error: unknown }) {
  if (!error) {
    return null;
  }
  return (
    <div role="alert" className="alert alert-error">
      {describeProblem(error)}
    </div>
  );
}

export function SuccessMessage({ message }: { message: string | null }) {
  return message ? (
    <div role="status" className="alert alert-success">
      {message}
    </div>
  ) : null;
}
