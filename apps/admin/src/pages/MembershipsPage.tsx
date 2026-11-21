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
  Queued: 'In de wachtrij',
  Invited: 'Gemaild',
  Applied: 'Aanmelding ingediend',
  Split: 'Gesplitst',
};

/** Wie een (nieuwe) uitnodiging kan krijgen: nog niet gesplitst, geen aanmelding onderweg, en een e-mailadres. */
function canMail(c: SplitCandidate) {
  return c.state !== 'Split' && c.state !== 'Applied' && !!c.email;
}

/**
 * Lidmaatschappen (fase 25): aantallen per soort, de tarieven en het splitsen van tweepersoonslidmaatschappen. Het
 * hoofdlid krijgt een mail met een persoonlijke link naar het aanmeldformulier; na goedkeuring van die aanmelding wordt
 * het tweede lid een eigen lid in een combinatie (het hoofdlid betaalt). De tabel laat zien wie al gesplitst is en wie
 * niet, en je vinkt zelf aan wie een (nieuwe) mail krijgt (wens 2026-11-21).
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
  const [tab, setTab] = useState<'open' | 'done'>('open');
  const [selected, setSelected] = useState<string[]>([]);
  const [confirming, setConfirming] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  const invite = useApiMutation(
    async (memberIds: string[]) =>
      (await api.POST('/api/v1/admin/memberships/splits/invite', { body: { memberIds } })).data,
    [['memberships']],
  );

  const candidates = splits.data ?? [];
  const open = candidates.filter((c) => c.state !== 'Split');
  const done = candidates.filter((c) => c.state === 'Split');
  const shown = tab === 'open' ? open : done;
  const mailable = shown.filter(canMail);
  // Alleen een selectie die ook echt gemaild kan worden: wie ondertussen gesplitst is, valt eruit.
  const chosen = selected.filter((id) => candidates.some((c) => c.memberId === id && canMail(c)));
  const notInvited = open.filter((c) => c.state === 'NotInvited' && c.email);
  const toggle = (id: string) => setSelected((s) => (s.includes(id) ? s.filter((x) => x !== id) : [...s, id]));

  function runInvite() {
    setMessage(null);
    invite.mutate(chosen, {
      onSuccess: (data) => {
        setConfirming(false);
        setSelected([]);
        const result = data as SplitInviteResult | undefined;
        if (!result) return;
        const parts = [`${result.invited} ${result.invited === 1 ? 'mail' : 'mails'} in de wachtrij gezet`];
        if (result.withoutEmail > 0) parts.push(`${result.withoutEmail} zonder e-mailadres overgeslagen`);
        if (result.skipped > 0) parts.push(`${result.skipped} met een lopende aanmelding overgeslagen`);
        const last = result.invited > 1 ? ` De laatste mail gaat rond ${formatDateTime(result.lastAt)} weg.` : '';
        setMessage(`${parts.join(', ')}.${last}`);
      },
    });
  }

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
        </div>
        <p className="card-hint">
          Vink de hoofdleden aan die een mail moeten krijgen met een persoonlijke link (90 dagen geldig) om het tweede
          lid te registreren. Het formulier is dan al ingevuld met het e-mailadres en, als die bekend zijn, de naam en
          het adres. Wie al eerder een uitnodiging kreeg, krijgt een nieuwe link; de oude vervalt. De aanmelding komt
          daarna bij Aanmeldingen. Antwoorden gaan naar het secretariaat.
        </p>
        <div className="tabs" role="tablist" aria-label="Tweepersoonsleden">
          <button type="button" role="tab" className="tab" aria-selected={tab === 'open'} onClick={() => setTab('open')}>
            Nog niet gesplitst ({open.length})
          </button>
          <button type="button" role="tab" className="tab" aria-selected={tab === 'done'} onClick={() => setTab('done')}>
            Gesplitst ({done.length})
          </button>
        </div>
        {tab === 'open' ? (
          <div className="actions">
            <span className="muted grow">{chosen.length} geselecteerd</span>
            {notInvited.length > 0 ? (
              <button
                type="button"
                className="button secondary"
                onClick={() => setSelected(notInvited.map((c) => c.memberId))}
              >
                Alleen wie nog niet gemaild is ({notInvited.length})
              </button>
            ) : null}
            {chosen.length > 0 ? (
              <button type="button" className="button secondary" onClick={() => setSelected([])}>
                Selectie wissen
              </button>
            ) : null}
            <button
              type="button"
              className="button"
              disabled={chosen.length === 0 || invite.isPending}
              onClick={() => setConfirming(true)}
            >
              Mail sturen ({chosen.length})
            </button>
          </div>
        ) : null}
        {shown.length === 0 ? (
          <p className="muted">
            {candidates.length === 0
              ? 'Er zijn geen tweepersoonslidmaatschappen.'
              : tab === 'open'
                ? 'Alle tweepersoonslidmaatschappen zijn gesplitst.'
                : 'Er is nog niemand gesplitst.'}
          </p>
        ) : (
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Tweepersoonsleden">
            <table className="table compact">
              <caption className="visually-hidden">
                {tab === 'open' ? 'Tweepersoonsleden die nog niet gesplitst zijn' : 'Tweepersoonsleden die gesplitst zijn'}
              </caption>
              <thead>
                <tr>
                  {tab === 'open' ? (
                    <th scope="col">
                      <input
                        type="checkbox"
                        aria-label="Alle hoofdleden in deze lijst selecteren"
                        disabled={mailable.length === 0}
                        checked={mailable.length > 0 && mailable.every((c) => selected.includes(c.memberId))}
                        onChange={(e) =>
                          e.target.checked ? setSelected(mailable.map((c) => c.memberId)) : setSelected([])
                        }
                      />
                    </th>
                  ) : null}
                  <th scope="col">Lidnummer</th>
                  <th scope="col">Hoofdlid</th>
                  <th scope="col">Tweede lid</th>
                  <th scope="col">E-mailadres</th>
                  <th scope="col">Stand</th>
                  {tab === 'open' ? (
                    <th scope="col">
                      <span className="visually-hidden">Acties</span>
                    </th>
                  ) : null}
                </tr>
              </thead>
              <tbody>
                {shown.map((c) => (
                  <tr key={c.memberId}>
                    {tab === 'open' ? (
                      <td>
                        {canMail(c) ? (
                          <input
                            type="checkbox"
                            aria-label={`${c.fullName} selecteren`}
                            checked={selected.includes(c.memberId)}
                            onChange={() => toggle(c.memberId)}
                          />
                        ) : null}
                      </td>
                    ) : null}
                    <td>{c.memberNumber}</td>
                    <td>
                      <Link to="/leden/$id" params={{ id: c.memberId }}>
                        {c.fullName}
                      </Link>
                    </td>
                    <td>
                      {c.secondMemberId && c.secondMemberName ? (
                        <Link to="/leden/$id" params={{ id: c.secondMemberId }}>
                          {c.secondMemberName}
                        </Link>
                      ) : (
                        (c.secondMemberName ?? <span className="muted">onbekend</span>)
                      )}
                    </td>
                    <td>{c.email ?? <span className="muted">geen e-mailadres</span>}</td>
                    <td>
                      {stateLabels[c.state]}
                      {c.state === 'Split' ? (
                        c.splitAt ? (
                          <div className="muted small-text">{formatDateTime(c.splitAt)}</div>
                        ) : null
                      ) : c.invitedAt ? (
                        <div className="muted small-text">
                          {formatDateTime(c.invitedAt)}
                          {c.timesInvited > 1 ? ` (${c.timesInvited}×)` : ''}
                        </div>
                      ) : null}
                    </td>
                    {tab === 'open' ? (
                      <td>
                        {c.state === 'Applied' && c.applicationId ? (
                          <Link to="/aanmeldingen/$id" params={{ id: c.applicationId }} className="button ghost small">
                            Aanmelding bekijken
                          </Link>
                        ) : null}
                      </td>
                    ) : null}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <RatesCard />

      <ConfirmDialog
        open={confirming}
        title="Hoofdleden mailen?"
        message={`Er gaat een mail met een persoonlijke link naar ${chosen.length} ${chosen.length === 1 ? 'hoofdlid' : 'hoofdleden'}. Wie al eerder een uitnodiging kreeg, krijgt een nieuwe link; de oude vervalt. De mails gaan verdeeld over de tijd weg, dus ze staan niet meteen allemaal in de inbox.`}
        confirmLabel="Mails versturen"
        busy={invite.isPending}
        onConfirm={runInvite}
        onCancel={() => setConfirming(false)}
      />
    </>
  );
}
