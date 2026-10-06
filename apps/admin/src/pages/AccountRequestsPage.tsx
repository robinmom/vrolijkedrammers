import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useAccountRequests, useApiMutation, useProvisioning, type AccountRequest, type AccountRequestStatus, type Schemas } from '../api/hooks';
import { DataTable, columnHelper } from '../components/DataTable';
import { Dialog } from '../components/Dialog';
import { Field } from '../components/Field';
import { Pagination } from '../components/Pagination';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { accountRequestStatusLabels, formatDateTime, mismatchReasonLabels, provisioningStepLabels } from '../format';

type RecheckResult = Schemas['RecheckResult'];

const statusTone: Record<string, string> = { Pending: 'warn', Approved: 'ok', Rejected: 'neutral', Duplicate: 'info' };

/**
 * Accountverzoeken (fase 9, ADR-014): "Ik ben al lid"-aanvragen zonder exacte match met een lid (uit e-Boekhouden of
 * lokaal aangemaakt). Het bestuur corrigeert zo nodig het lid, controleert opnieuw of maakt het account aan; of wijst af.
 * Na een sync of een gewijzigd lid controleert de API de open verzoeken zelf opnieuw. Daaronder de provisioning die nog
 * loopt of is mislukt, met "opnieuw proberen".
 */
export function AccountRequestsPage() {
  const api = useApi();
  const [status, setStatus] = useState<AccountRequestStatus | ''>('Pending');
  const [page, setPage] = useState(1);
  const [rejecting, setRejecting] = useState<AccountRequest | null>(null);
  const [reason, setReason] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  // Alleen een e-mailadres met meerdere leden (fase 24): het bestuur kiest het lid.
  const [chosen, setChosen] = useState<Record<string, string>>({});
  const target = (r: AccountRequest) => r.member ?? r.candidates?.find((c) => c.id === chosen[r.id]) ?? null;
  const requests = useAccountRequests(status, page);
  const provisioning = useProvisioning();

  const approve = useApiMutation(
    (r: AccountRequest) => api.POST('/api/v1/admin/account-requests/{id}/approve', { params: { path: { id: r.id } }, body: { memberId: target(r)!.id } }),
    [['account-requests'], ['account-provisioning']],
  );
  const reject = useApiMutation(
    (r: AccountRequest) => api.POST('/api/v1/admin/account-requests/{id}/reject', { params: { path: { id: r.id } }, body: { reason: reason || null } }),
    [['account-requests']],
  );
  const recheck = useApiMutation(
    async (id: string | null) =>
      (id
        ? await api.POST('/api/v1/admin/account-requests/{id}/recheck', { params: { path: { id } } })
        : await api.POST('/api/v1/admin/account-requests/recheck')
      ).data,
    [['account-requests'], ['account-provisioning']],
  );
  const recheckDone = (data: unknown) => {
    const result = data as RecheckResult | undefined;
    setMessage(
      !result
        ? null
        : result.approved + result.alreadyHasAccount === 0
          ? 'Opnieuw gecontroleerd: nog geen passend actief lid gevonden.'
          : `Opnieuw gecontroleerd: ${result.approved} account(s) worden aangemaakt` +
            (result.alreadyHasAccount ? `, ${result.alreadyHasAccount} had(den) al een account` : '') +
            (result.stillPending ? `, ${result.stillPending} nog open.` : '.'),
    );
  };
  const retry = useApiMutation(
    (id: string) => api.POST('/api/v1/admin/account-provisioning/{id}/retry', { params: { path: { id } } }),
    [['account-provisioning']],
  );

  const helper = columnHelper<AccountRequest>();
  const columns = [
    helper.accessor('requestedAt', { header: 'Aangevraagd', cell: (info) => formatDateTime(info.getValue()) }),
    helper.accessor('memberNumber', { header: 'Lidnummer', cell: (info) => info.getValue() ?? '—' }),
    helper.accessor('email', { header: 'Ingevuld e-mailadres' }),
    helper.accessor('status', {
      header: 'Status',
      cell: (info) => {
        const r = info.row.original;
        return (
          <>
            <span className={`badge ${statusTone[r.status] ?? 'neutral'}`}>{accountRequestStatusLabels[r.status] ?? r.status}</span>
            {r.mismatchReason ? <div className="muted small-text">{mismatchReasonLabels[r.mismatchReason] ?? r.mismatchReason}</div> : null}
          </>
        );
      },
    }),
    helper.accessor('member', {
      header: 'Lid',
      enableSorting: false,
      cell: (info) => {
        const m = info.getValue();
        const r = info.row.original;
        if (!m && (r.candidates?.length ?? 0) > 0) {
          return (
            <select
              aria-label={`Kies het lid voor ${r.email}`}
              value={chosen[r.id] ?? ''}
              onChange={(e) => setChosen({ ...chosen, [r.id]: e.target.value })}
            >
              <option value="">Kies een lid…</option>
              {r.candidates!.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.fullName} ({c.memberNumber})
                </option>
              ))}
            </select>
          );
        }
        return m ? (
          <>
            <Link to="/leden/$id" params={{ id: m.id }}>
              {m.fullName}
            </Link>
            <div className="muted small-text">{m.email ?? 'geen e-mailadres'}</div>
          </>
        ) : (
          <span className="muted">Niet gevonden</span>
        );
      },
    }),
    helper.display({
      id: 'acties',
      header: () => <span className="visually-hidden">Acties</span>,
      cell: (info) => {
        const r = info.row.original;
        if (r.status !== 'Pending') {
          return <span className="muted small-text">{formatDateTime(r.decidedAt)}</span>;
        }
        const member = target(r);
        const canApprove = Boolean(member?.email) && member?.status === 'Active';
        return (
          <div className="actions">
            <button
              type="button"
              className="button small"
              disabled={!canApprove || approve.isPending}
              title={canApprove ? undefined : 'Alleen voor een actief lid met een e-mailadres'}
              aria-label={`Account aanmaken voor ${member?.fullName ?? r.memberNumber ?? r.email}`}
              onClick={() =>
                approve.mutate(r, { onSuccess: () => setMessage(`Het account voor ${member!.fullName} wordt aangemaakt met ${member!.email}.`) })
              }
            >
              Account aanmaken
            </button>
            <button
              type="button"
              className="button secondary small"
              disabled={recheck.isPending}
              aria-label={`Verzoek ${r.memberNumber ?? r.email} opnieuw controleren`}
              onClick={() => recheck.mutate(r.id, { onSuccess: recheckDone })}
            >
              Opnieuw controleren
            </button>
            <button type="button" className="button secondary small" aria-label={`Verzoek ${r.memberNumber ?? r.email} afwijzen`} onClick={() => setRejecting(r)}>
              Afwijzen
            </button>
          </div>
        );
      },
    }),
  ];

  const open = provisioning.data ?? [];

  return (
    <>
      <div className="page-header">
        <div>
          <h1 className="page-title">Accountverzoeken</h1>
          <p className="page-subtitle">
            Leden die met &quot;Ik ben al lid&quot; een account vroegen, maar niet exact overeenkwamen met een lid: uit e-Boekhouden of
            hier aangemaakt (bijvoorbeeld via &quot;lid worden&quot;). Is het lid later aangemaakt, actief gezet of heeft het een ander
            e-mailadres gekregen? Dan wordt het verzoek vanzelf opnieuw gecontroleerd, of klik op Opnieuw controleren. Het account
            krijgt altijd het e-mailadres van het lid.
          </p>
        </div>
        <div className="actions">
          <button
            type="button"
            className="button secondary"
            disabled={recheck.isPending}
            onClick={() => recheck.mutate(null, { onSuccess: recheckDone })}
          >
            Alles opnieuw controleren
          </button>
        </div>
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={requests.error ?? approve.error ?? reject.error ?? recheck.error ?? retry.error} />

      {open.length > 0 ? (
        <section className="card" aria-labelledby="provisioning">
          <h2 id="provisioning">Accounts in aanmaak</h2>
          <ul className="list">
            {open.map((p) => (
              <li key={p.id} className="list-row">
                <div>
                  <strong>{p.memberName ?? p.memberNumber ?? 'Onbekend lid'}</strong>{' '}
                  <span className={`badge ${p.lastError ? 'error' : 'info'}`}>{p.lastError ? 'Mislukt' : (provisioningStepLabels[p.step] ?? p.step)}</span>
                  {p.lastError ? <div className="muted small-text">{p.lastError}</div> : null}
                </div>
                {p.lastError ? (
                  <button type="button" className="button secondary small" disabled={retry.isPending} onClick={() => retry.mutate(p.id)}>
                    Opnieuw proberen
                  </button>
                ) : null}
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      <DataTable
        caption="Accountverzoeken"
        columns={columns}
        data={requests.data?.items ?? []}
        emptyText={status === 'Pending' ? 'Geen verzoeken die op beoordeling wachten.' : 'Geen verzoeken.'}
        header={
          <div className="toolbar">
            <div className="field">
            <label htmlFor="verzoek-status">Status</label>
            <select
              id="verzoek-status"
              value={status}
              onChange={(e) => {
                setStatus(e.target.value as AccountRequestStatus | '');
                setPage(1);
              }}
            >
              <option value="Pending">Wacht op beoordeling</option>
              <option value="Approved">Goedgekeurd</option>
              <option value="Rejected">Afgewezen</option>
              <option value="Duplicate">Had al een account</option>
              <option value="">Alle</option>
            </select>
            </div>
          </div>
        }
        footer={
          requests.data ? (
            <Pagination page={requests.data.page} pageSize={requests.data.pageSize} totalCount={requests.data.totalCount} onPage={setPage} noun="verzoeken" />
          ) : null
        }
      />

      <Dialog open={rejecting !== null} title="Verzoek afwijzen" onClose={() => setRejecting(null)}>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            reject.mutate(rejecting!, {
              onSuccess: () => {
                setMessage('Het verzoek is afgewezen.');
                setRejecting(null);
                setReason('');
              },
            });
          }}
        >
          <p>
            {rejecting?.memberNumber ? `Lidnummer ${rejecting.memberNumber}, ` : ''}{rejecting?.email}. De aanvrager krijgt hiervan geen bericht; neem zo nodig zelf contact
            op.
          </p>
          <Field label="Reden (intern, optioneel)" value={reason} maxLength={500} onChange={(e) => setReason(e.target.value)} />
          <div className="actions">
            <button type="button" className="button secondary" onClick={() => setRejecting(null)}>
              Annuleren
            </button>
            <button type="submit" className="button danger" disabled={reject.isPending}>
              Afwijzen
            </button>
          </div>
        </form>
      </Dialog>
    </>
  );
}
