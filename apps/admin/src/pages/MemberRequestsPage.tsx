import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, type Schemas } from '../api/hooks';
import { Dialog } from '../components/Dialog';
import { Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDateTime } from '../format';

type Rejecting = { kind: 'change' | 'break'; id: string; name: string } | null;
type BreakItem = Schemas['BreakRequestItem'];

const breakStatusLabels: Record<string, string> = {
  AwaitingAgreement: 'Wacht op akkoord van de ander',
  AwaitingApproval: 'Beide akkoord: wacht op goedkeuring',
};

/**
 * Wijzigingsverzoeken (fase 26): leden vragen in de app om hun adres, e-mail, telefoon of IBAN te wijzigen, of om een
 * combinatie te verbreken. Niets wordt doorgevoerd zonder goedkeuring hier.
 */
export function MemberRequestsPage() {
  const api = useApi();
  const requests = useQuery({
    queryKey: ['member-requests'],
    queryFn: async () => (await api.GET('/api/v1/admin/member-requests')).data!,
  });
  const [message, setMessage] = useState<string | null>(null);
  const [rejecting, setRejecting] = useState<Rejecting>(null);
  const [reason, setReason] = useState('');

  const approveChange = useApiMutation(
    (id: string) => api.POST('/api/v1/admin/member-requests/changes/{id}/approve', { params: { path: { id } } }),
    [['member-requests'], ['members']],
  );
  const approveBreak = useApiMutation(
    (id: string) => api.POST('/api/v1/admin/member-requests/breaks/{id}/approve', { params: { path: { id } } }),
    [['member-requests'], ['members'], ['contributions']],
  );
  const reject = useApiMutation(
    (r: NonNullable<Rejecting>) =>
      r.kind === 'change'
        ? api.POST('/api/v1/admin/member-requests/changes/{id}/reject', {
            params: { path: { id: r.id } },
            body: { reason },
          })
        : api.POST('/api/v1/admin/member-requests/breaks/{id}/reject', {
            params: { path: { id: r.id } },
            body: { reason },
          }),
    [['member-requests']],
  );

  const changes = requests.data?.changes ?? [];
  const breaks = requests.data?.breaks ?? [];
  const busy = approveChange.isPending || approveBreak.isPending;

  return (
    <>
      <div className="page-header">
        <h1>Wijzigingsverzoeken</h1>
      </div>
      <ProblemAlert error={requests.error ?? approveChange.error ?? approveBreak.error} />
      <SuccessMessage message={message} />

      <section className="card" aria-labelledby="gegevens">
        <h2 id="gegevens">
          Gegevens wijzigen <span className="muted">({changes.length})</span>
        </h2>
        <p className="card-hint">
          Na goedkeuring zijn de gewijzigde velden &quot;handmatig&quot;: de sync met e-Boekhouden overschrijft ze niet.
          Het lid krijgt een e-mail. Een IBAN zie je hier alleen gemaskeerd.
        </p>
        {requests.data && changes.length === 0 ? <p className="muted">Geen openstaande verzoeken.</p> : null}
        {changes.map((c) => (
          <article key={c.id} className="card nested" aria-label={`Wijziging van ${c.fullName}`}>
            <div className="card-header">
              <h3>
                <Link to="/leden/$id" params={{ id: c.memberId }}>
                  {c.fullName}
                </Link>{' '}
                <span className="muted">({c.memberNumber})</span>
              </h3>
              <span className="muted small-text">{formatDateTime(c.requestedAt)}</span>
            </div>
            <div className="table-scroll" tabIndex={0} role="region" aria-label={`Wijzigingen van ${c.fullName}`}>
              <table className="table compact">
                <caption className="visually-hidden">Wijzigingen van {c.fullName}</caption>
                <thead>
                  <tr>
                    <th scope="col">Gegeven</th>
                    <th scope="col">Nu</th>
                    <th scope="col">Wordt</th>
                  </tr>
                </thead>
                <tbody>
                  {c.fields.map((f) => (
                    <tr key={f.field}>
                      <td>{f.label}</td>
                      <td>{f.current ?? '—'}</td>
                      <td>
                        <strong>{f.requested}</strong>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="actions">
              <button
                type="button"
                className="button secondary small"
                onClick={() => {
                  setReason('');
                  setRejecting({ kind: 'change', id: c.id, name: c.fullName });
                }}
              >
                Afwijzen
              </button>
              <button
                type="button"
                className="button small"
                disabled={busy}
                aria-label={`Wijziging van ${c.fullName} goedkeuren`}
                onClick={() =>
                  approveChange.mutate(c.id, {
                    onSuccess: () => setMessage(`De wijziging van ${c.fullName} is doorgevoerd.`),
                  })
                }
              >
                Goedkeuren
              </button>
            </div>
          </article>
        ))}
      </section>

      <section className="card" aria-labelledby="verbreken">
        <h2 id="verbreken">
          Combinatie verbreken <span className="muted">({breaks.length})</span>
        </h2>
        <p className="card-hint">
          Beide leden moeten akkoord geven; het tweede lid geeft daarbij een eigen IBAN en machtiging. Na goedkeuring
          betalen beiden het tarief voor één lid. De jaren lid blijven gelijk.
        </p>
        {requests.data && breaks.length === 0 ? <p className="muted">Geen lopende verzoeken.</p> : null}
        {breaks.map((b: BreakItem) => (
          <article key={b.id} className="card nested" aria-label={`Verbreken ${b.payerName} en ${b.partnerName}`}>
            <h3>
              <Link to="/leden/$id" params={{ id: b.payerMemberId }}>
                {b.payerName}
              </Link>{' '}
              en{' '}
              <Link to="/leden/$id" params={{ id: b.partnerMemberId }}>
                {b.partnerName}
              </Link>
            </h3>
            <dl className="details">
              <dt>Aangevraagd door</dt>
              <dd>
                {b.initiatedBy}, {formatDateTime(b.initiatedAt)}
              </dd>
              <dt>Akkoord hoofdlid</dt>
              <dd>{b.payerAgreedAt ? formatDateTime(b.payerAgreedAt) : 'Nog niet'}</dd>
              <dt>Akkoord tweede lid</dt>
              <dd>{b.partnerAgreedAt ? formatDateTime(b.partnerAgreedAt) : 'Nog niet'}</dd>
              <dt>IBAN tweede lid</dt>
              <dd>{b.partnerIbanMasked ? `${b.partnerIbanMasked} (${b.partnerAccountHolder})` : '—'}</dd>
              <dt>Stand</dt>
              <dd>{breakStatusLabels[b.status] ?? b.status}</dd>
            </dl>
            <div className="actions">
              <button
                type="button"
                className="button secondary small"
                onClick={() => {
                  setReason('');
                  setRejecting({ kind: 'break', id: b.id, name: `${b.payerName} en ${b.partnerName}` });
                }}
              >
                Afwijzen
              </button>
              <button
                type="button"
                className="button small"
                disabled={busy || b.status !== 'AwaitingApproval'}
                aria-label={`Verbreken ${b.payerName} en ${b.partnerName} goedkeuren`}
                onClick={() =>
                  approveBreak.mutate(b.id, {
                    onSuccess: () => setMessage(`De combinatie van ${b.payerName} en ${b.partnerName} is verbroken.`),
                  })
                }
              >
                Goedkeuren
              </button>
            </div>
          </article>
        ))}
      </section>

      <Dialog open={rejecting !== null} title="Verzoek afwijzen" onClose={() => setRejecting(null)}>
        <p>Het verzoek van {rejecting?.name} wordt afgewezen. Er wordt niets gewijzigd.</p>
        <Field label="Reden" maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
        <ProblemAlert error={reject.error} />
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => setRejecting(null)}>
            Annuleren
          </button>
          <button
            type="button"
            className="button danger"
            disabled={reject.isPending}
            onClick={() =>
              reject.mutate(rejecting!, {
                onSuccess: () => {
                  setMessage('Het verzoek is afgewezen.');
                  setRejecting(null);
                },
              })
            }
          >
            Afwijzen
          </button>
        </div>
      </Dialog>
    </>
  );
}
