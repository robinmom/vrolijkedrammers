import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useAccountRequests, useApiMutation, useProvisioning, type AccountRequest, type AccountRequestStatus } from '../api/hooks';
import { DataTable, columnHelper } from '../components/DataTable';
import { Dialog } from '../components/Dialog';
import { Field } from '../components/Field';
import { Pagination } from '../components/Pagination';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { accountRequestStatusLabels, formatDateTime, mismatchReasonLabels, provisioningStepLabels } from '../format';

const statusTone: Record<string, string> = { Pending: 'warn', Approved: 'ok', Rejected: 'neutral', Duplicate: 'info' };

/**
 * Accountverzoeken (fase 9, ADR-014): "Ik ben al lid"-aanvragen zonder exacte match met e-Boekhouden. Het bestuur
 * corrigeert zo nodig het e-mailadres in e-Boekhouden, synchroniseert en maakt dan het account aan; of wijst af.
 * Daaronder de provisioning die nog loopt of is mislukt, met "opnieuw proberen".
 */
export function AccountRequestsPage() {
  const api = useApi();
  const [status, setStatus] = useState<AccountRequestStatus | ''>('Pending');
  const [page, setPage] = useState(1);
  const [rejecting, setRejecting] = useState<AccountRequest | null>(null);
  const [reason, setReason] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const requests = useAccountRequests(status, page);
  const provisioning = useProvisioning();

  const approve = useApiMutation(
    (r: AccountRequest) => api.POST('/api/v1/admin/account-requests/{id}/approve', { params: { path: { id: r.id } }, body: { memberId: r.member!.id } }),
    [['account-requests'], ['account-provisioning']],
  );
  const reject = useApiMutation(
    (r: AccountRequest) => api.POST('/api/v1/admin/account-requests/{id}/reject', { params: { path: { id: r.id } }, body: { reason: reason || null } }),
    [['account-requests']],
  );
  const retry = useApiMutation(
    (id: string) => api.POST('/api/v1/admin/account-provisioning/{id}/retry', { params: { path: { id } } }),
    [['account-provisioning']],
  );

  const helper = columnHelper<AccountRequest>();
  const columns = [
    helper.accessor('requestedAt', { header: 'Aangevraagd', cell: (info) => formatDateTime(info.getValue()) }),
    helper.accessor('memberNumber', { header: 'Lidnummer' }),
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
      header: 'Lid in e-Boekhouden',
      enableSorting: false,
      cell: (info) => {
        const m = info.getValue();
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
        const canApprove = Boolean(r.member?.email) && r.member?.status === 'Active';
        return (
          <div className="actions">
            <button
              type="button"
              className="button small"
              disabled={!canApprove || approve.isPending}
              title={canApprove ? undefined : 'Alleen voor een actief lid met een e-mailadres in e-Boekhouden'}
              aria-label={`Account aanmaken voor ${r.member?.fullName ?? r.memberNumber}`}
              onClick={() =>
                approve.mutate(r, { onSuccess: () => setMessage(`Het account voor ${r.member!.fullName} wordt aangemaakt met ${r.member!.email}.`) })
              }
            >
              Account aanmaken
            </button>
            <button type="button" className="button secondary small" aria-label={`Verzoek ${r.memberNumber} afwijzen`} onClick={() => setRejecting(r)}>
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
            Leden die met &quot;Ik ben al lid&quot; een account vroegen, maar niet exact overeenkwamen met e-Boekhouden. Klopt het
            e-mailadres niet? Laat het secretariaat het in e-Boekhouden aanpassen, synchroniseer, en maak dan het account aan. Het
            account krijgt altijd het e-mailadres uit e-Boekhouden.
          </p>
        </div>
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={requests.error ?? approve.error ?? reject.error ?? retry.error} />

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
            Lidnummer {rejecting?.memberNumber}, {rejecting?.email}. De aanvrager krijgt hiervan geen bericht; neem zo nodig zelf contact
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
