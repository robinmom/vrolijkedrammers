import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, type Schemas } from '../api/hooks';
import { ConfirmDialog } from '../components/Dialog';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { RatesCard } from '../components/RatesCard';
import { formatDateTime } from '../format';

type SplitCandidate = Schemas['SplitCandidate'];
type SplitInviteResult = Schemas['SplitInviteResult'];

const stateLabels: Record<SplitCandidate['state'], string> = {
  NotInvited: 'Nog niet gemaild',
  Invited: 'Gemaild',
  Applied: 'Aanmelding ingediend',
};

/**
 * Lidmaatschappen (fase 25): aantallen per soort, de tarieven en het splitsen van tweepersoonslidmaatschappen. Het
 * hoofdlid krijgt een mail met een persoonlijke link naar het aanmeldformulier; na goedkeuring van die aanmelding wordt
 * het tweede lid een eigen lid in een combinatie (het hoofdlid betaalt).
 */
export function MembershipsPage() {
  const api = useApi();
  const overview = useQuery({
    queryKey: ['memberships', 'overview'],
    queryFn: async () => (await api.GET('/api/v1/admin/memberships/overview')).data!,
  });
  const splits = useQuery({
    queryKey: ['memberships', 'splits'],
    queryFn: async () => (await api.GET('/api/v1/admin/memberships/splits')).data!,
  });
  const [confirmAll, setConfirmAll] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  const invite = useApiMutation(
    async (memberIds: string[] | null) =>
      (await api.POST('/api/v1/admin/memberships/splits/invite', { body: { memberIds } })).data,
    [['memberships']],
  );

  function runInvite(memberIds: string[] | null) {
    setMessage(null);
    invite.mutate(memberIds, {
      onSuccess: (data) => {
        setConfirmAll(false);
        const result = data as SplitInviteResult | undefined;
        if (!result) return;
        const parts = [`${result.invited} ${result.invited === 1 ? 'mail' : 'mails'} verstuurd`];
        if (result.withoutEmail > 0) parts.push(`${result.withoutEmail} zonder e-mailadres overgeslagen`);
        if (result.skipped > 0) parts.push(`${result.skipped} met een lopende aanmelding overgeslagen`);
        setMessage(`${parts.join(', ')}.`);
      },
    });
  }

  const candidates = splits.data ?? [];
  const notInvited = candidates.filter((c) => c.state === 'NotInvited' && c.email).length;

  return (
    <>
      <div className="page-header">
        <h1>Lidmaatschappen</h1>
      </div>
      <ProblemAlert error={overview.error ?? splits.error ?? invite.error} />
      <SuccessMessage message={message} />

      {overview.data ? (
        <section className="kpis" aria-label="Actieve leden per soort lidmaatschap">
          {overview.data.byKind.map((k) => (
            <div key={k.label} className="kpi">
              <span className="kpi-label">{k.label}</span>
              <span className="kpi-value">{k.count}</span>
            </div>
          ))}
          <div className="kpi">
            <span className="kpi-label">Vrijgesteld (bijv. Convent)</span>
            <span className="kpi-value">{overview.data.exempt}</span>
            <span className="kpi-hint">van {overview.data.active} actieve leden</span>
          </div>
        </section>
      ) : null}

      <section className="card" aria-labelledby="splitsen">
        <div className="card-header">
          <h2 id="splitsen">Tweepersoonsleden splitsen</h2>
          <button
            type="button"
            className="button"
            disabled={notInvited === 0 || invite.isPending}
            onClick={() => setConfirmAll(true)}
          >
            Iedereen mailen die nog niet gemaild is ({notInvited})
          </button>
        </div>
        <p className="card-hint">
          Het hoofdlid krijgt een mail met een persoonlijke link (90 dagen geldig) om het tweede lid te registreren. Het
          formulier is dan al ingevuld met het e-mailadres en, als die bekend zijn, de naam uit e-Boekhouden (vrij veld
          &quot;Tweede lid&quot;) en het adres. De aanmelding komt daarna bij Aanmeldingen. Antwoorden gaan naar het
          secretariaat.
        </p>
        {splits.data && candidates.length === 0 ? (
          <p className="muted">Alle tweepersoonslidmaatschappen zijn gesplitst.</p>
        ) : (
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Tweepersoonsleden">
            <table className="table compact">
              <caption className="visually-hidden">Tweepersoonsleden die nog niet gesplitst zijn</caption>
              <thead>
                <tr>
                  <th scope="col">Lidnummer</th>
                  <th scope="col">Hoofdlid</th>
                  <th scope="col">Tweede lid (e-Boekhouden)</th>
                  <th scope="col">E-mailadres</th>
                  <th scope="col">Stand</th>
                  <th scope="col">
                    <span className="visually-hidden">Acties</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {candidates.map((c) => (
                  <tr key={c.memberId}>
                    <td>{c.memberNumber}</td>
                    <td>
                      <Link to="/leden/$id" params={{ id: c.memberId }}>
                        {c.fullName}
                      </Link>
                    </td>
                    <td>{c.secondMemberName ?? <span className="muted">onbekend</span>}</td>
                    <td>{c.email ?? <span className="muted">geen e-mailadres</span>}</td>
                    <td>
                      {stateLabels[c.state]}
                      {c.invitedAt ? (
                        <div className="muted small-text">
                          {formatDateTime(c.invitedAt)}
                          {c.timesInvited > 1 ? ` (${c.timesInvited}×)` : ''}
                        </div>
                      ) : null}
                    </td>
                    <td>
                      {c.state === 'Applied' && c.applicationId ? (
                        <Link to="/aanmeldingen/$id" params={{ id: c.applicationId }} className="button ghost small">
                          Aanmelding bekijken
                        </Link>
                      ) : c.email ? (
                        <button
                          type="button"
                          className="button ghost small"
                          disabled={invite.isPending}
                          aria-label={`${c.state === 'Invited' ? 'Opnieuw mailen' : 'Mail sturen'}: ${c.fullName}`}
                          onClick={() => runInvite([c.memberId])}
                        >
                          {c.state === 'Invited' ? 'Opnieuw mailen' : 'Mail sturen'}
                        </button>
                      ) : null}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <RatesCard />

      <ConfirmDialog
        open={confirmAll}
        title="Hoofdleden mailen?"
        message={`Er gaat een mail met een persoonlijke link naar ${notInvited} ${notInvited === 1 ? 'hoofdlid' : 'hoofdleden'} die nog niet gemaild zijn.`}
        confirmLabel="Mails versturen"
        busy={invite.isPending}
        onConfirm={() => runInvite(null)}
        onCancel={() => setConfirmAll(false)}
      />
    </>
  );
}
