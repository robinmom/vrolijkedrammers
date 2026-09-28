import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, useMemberAccess, type AccessResult } from '../api/hooks';
import { formatDateTime } from '../format';
import { ProblemAlert } from './ProblemAlert';

const time = new Intl.DateTimeFormat('nl-NL', { hour: '2-digit', minute: '2-digit', timeZone: 'Europe/Amsterdam' });
const dayTime = new Intl.DateTimeFormat('nl-NL', {
  weekday: 'short',
  day: 'numeric',
  month: 'short',
  hour: '2-digit',
  minute: '2-digit',
  timeZone: 'Europe/Amsterdam',
});

const outcomeLabels: Record<string, string> = {
  Admitted: 'binnen',
  AdmittedAgain: 'opnieuw binnen',
  Warning: 'al eerder binnen',
  Refused: 'geweigerd',
};

/**
 * Kaart "Toegang" bij een lid (fase 14, Figma 📷 Toegangscontrole): tijdens een activiteit met toegangscontrole kan het
 * deurpersoneel (rol Deurcontrole) een lid zonder smartphone inchecken. Zelfde toegangslog als de QR-scans, dus
 * "al binnen" klopt ook als het lid eerder met de QR naar binnen ging.
 */
export function AccessCard({ memberId }: { memberId: string }) {
  const api = useApi();
  const access = useMemberAccess(memberId, true);
  const [result, setResult] = useState<AccessResult | null>(null);
  const checkIn = useApiMutation(
    async (force: boolean) =>
      (await api.POST('/api/v1/admin/members/{memberId}/check-in', { params: { path: { memberId } }, body: { force } }))
        .data,
    [['member-access', memberId]],
  );

  function run(force: boolean) {
    setResult(null);
    checkIn.mutate(force, { onSuccess: (data) => setResult((data as AccessResult | undefined) ?? null) });
  }

  const a = access.data;
  const current = a?.current;
  const inside = a?.inside || result?.title === 'Al binnen';
  return (
    <section className="card" aria-labelledby="toegang">
      <h2 id="toegang">Toegang</h2>
      <ProblemAlert error={access.error} />
      {!a ? null : current ? (
        <>
          <p>
            <strong>
              {current.title} · {dayTime.format(new Date(current.startAt))}
              {current.endAt ? `–${time.format(new Date(current.endAt))}` : ''}
            </strong>
          </p>
          {a.ticketProblem ? (
            <div role="status" className="alert alert-error">
              Geen toegang: {a.ticketProblem}
            </div>
          ) : inside ? (
            <div role="status" className="alert alert-warning">
              <strong>Al binnen sinds {a.insideSince ? time.format(new Date(a.insideSince)) : '–'}</strong>
              <div>Controleer of dit dezelfde persoon is.</div>
            </div>
          ) : (
            <p className="success-text">Nog niet binnen vanavond.</p>
          )}
          {result && result.title !== 'Al binnen' ? (
            <div role="status" className="alert alert-success">
              {result.message}
            </div>
          ) : null}
          <ProblemAlert error={checkIn.error} />
          {!a.ticketProblem ? (
            <button
              type="button"
              className={inside ? 'button secondary' : 'button'}
              disabled={checkIn.isPending}
              onClick={() => run(inside)}
            >
              {inside ? 'Toch opnieuw inchecken' : 'Inchecken'}
            </button>
          ) : null}
        </>
      ) : (
        <p className="muted">Er is nu geen activiteit met toegangscontrole; inchecken kan tijdens zo'n activiteit.</p>
      )}
      {a && a.history.length ? (
        <ul className="list small-text">
          {a.history.slice(0, 5).map((h, i) => (
            <li key={i}>
              {formatDateTime(h.at)} · {h.eventTitle} · {h.method === 'Manual' ? 'ingecheckt' : 'QR gescand'} ·{' '}
              {h.decision === 'Refused'
                ? 'geweigerd'
                : h.decision === 'Admitted'
                  ? 'toegelaten'
                  : outcomeLabels[h.outcome]}
              {h.operator ? ` (${h.operator})` : ''}
            </li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}
