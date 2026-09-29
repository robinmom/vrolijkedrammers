import { Link } from '@tanstack/react-router';
import { useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, useMe, useTickets, type TicketAction, type TicketSummary } from '../api/hooks';
import { DataTable, columnHelper } from '../components/DataTable';
import { Dialog } from '../components/Dialog';
import { Pagination } from '../components/Pagination';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDateTime } from '../format';

/** Leesbare naam van het beveiligingsniveau van de sleutel op het toestel (ADR-005, OQ-68). */
const securityLabels: Record<string, string> = {
  SecureEnclave: 'Hardwaresleutel (Secure Enclave)',
  StrongBox: 'Hardwaresleutel (StrongBox)',
  TrustedEnvironment: 'Hardwaresleutel (TEE)',
  UnknownSecure: 'Beveiligde sleutel',
  Software: 'Softwaresleutel',
};

const actionLabels: Record<TicketAction, string> = {
  Block: 'Blokkeren',
  Unblock: 'Deblokkeren',
  Reissue: 'Opnieuw uitgeven',
  ResetRebinds: 'Overzetten vrijgeven',
};

const actionExplanations: Record<TicketAction, string> = {
  Block: 'Het ticket is direct ongeldig; het lid ziet "Ticket geblokkeerd". Vul een reden in.',
  Unblock: 'Het ticket is weer geldig.',
  Reissue:
    'Alle bestaande codes zijn direct ongeldig (bijvoorbeeld bij een verloren telefoon). Het lid koppelt het ticket opnieuw aan een toestel.',
  ResetRebinds: 'Het lid kan het ticket weer 3 keer naar een ander toestel overzetten.',
};

/**
 * Ledentickets (fase 13, ADR-005): één ticket per actief lid per carnavalsjaar, geldig de hele carnavalsperiode.
 * Overzicht met het gekoppelde toestel en de sleutel; blokkeren, opnieuw uitgeven en overzetten vrijgeven.
 */
export function TicketsPage() {
  const api = useApi();
  const me = useMe();
  const canManage = (me.data?.permissions ?? []).includes('ticket.manage');
  // Naar de ledenpagina alleen met het recht om leden te bekijken.
  const canOpenMember = (me.data?.permissions ?? []).includes('member.read');
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('');
  const [page, setPage] = useState(1);
  const [pending, setPending] = useState<{ ticket: TicketSummary; action: TicketAction } | null>(null);
  const [reason, setReason] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const tickets = useTickets(search, status, page);
  const issue = useApiMutation(async () => (await api.POST('/api/v1/admin/tickets/issue')).data, [['tickets']]);
  const act = useApiMutation(
    (v: { id: string; action: TicketAction; reason: string | null }) =>
      api.POST('/api/v1/admin/tickets/{id}/action', {
        params: { path: { id: v.id } },
        body: { action: v.action, reason: v.reason },
      }),
    [['tickets']],
  );

  function confirm(event: FormEvent) {
    event.preventDefault();
    if (!pending) return;
    act.mutate(
      { id: pending.ticket.id, action: pending.action, reason: reason.trim() || null },
      {
        onSuccess: () => {
          setMessage(`${actionLabels[pending.action]}: ${pending.ticket.memberName}.`);
          setPending(null);
        },
      },
    );
  }

  const helper = columnHelper<TicketSummary>();
  const columns = [
    helper.accessor('memberName', {
      header: 'Lid',
      cell: (info) => (
        <>
          {canOpenMember ? (
            <Link to="/leden/$id" params={{ id: info.row.original.memberId }}>
              {info.getValue()}
            </Link>
          ) : (
            info.getValue()
          )}
          {info.row.original.memberNumber ? (
            <div className="muted small-text">Lidnummer {info.row.original.memberNumber}</div>
          ) : null}
        </>
      ),
    }),
    helper.accessor('status', {
      header: 'Status',
      cell: (info) =>
        info.getValue() === 'Blocked' ? (
          <span className="badge error" title={info.row.original.blockedReason ?? undefined}>
            Geblokkeerd
          </span>
        ) : info.row.original.membershipActive ? (
          <span className="badge ok">Geldig</span>
        ) : (
          <span className="badge">Lidmaatschap niet actief</span>
        ),
    }),
    helper.accessor('boundDeviceName', {
      header: 'Toestel',
      cell: (info) =>
        info.getValue() ? (
          <>
            {info.getValue()}
            <div className="muted small-text">
              {info.row.original.deviceSecurityLevel
                ? (securityLabels[info.row.original.deviceSecurityLevel] ?? info.row.original.deviceSecurityLevel)
                : 'Zonder sleutel (code van de server)'}
              {info.row.original.boundAt ? ` · sinds ${formatDateTime(info.row.original.boundAt)}` : ''}
            </div>
          </>
        ) : (
          <span className="muted">Nog niet gekoppeld</span>
        ),
    }),
    helper.accessor('rebindCount', { header: 'Overgezet', cell: (info) => `${info.getValue()} van 3` }),
    helper.accessor('credentialVersion', { header: 'Versie' }),
    ...(canManage
      ? [
          helper.display({
            id: 'acties',
            header: 'Acties',
            cell: (info) => {
              const t = info.row.original;
              const actions: TicketAction[] = [
                t.status === 'Blocked' ? 'Unblock' : 'Block',
                'Reissue',
                ...(t.rebindCount > 0 ? (['ResetRebinds'] as TicketAction[]) : []),
              ];
              return (
                <div className="actions">
                  {actions.map((a) => (
                    <button
                      key={a}
                      type="button"
                      className={a === 'Block' ? 'button danger small' : 'button secondary small'}
                      onClick={() => {
                        setReason('');
                        act.reset();
                        setPending({ ticket: t, action: a });
                      }}
                    >
                      {actionLabels[a]} <span className="visually-hidden">{t.memberName}</span>
                    </button>
                  ))}
                </div>
              );
            },
          }),
        ]
      : []),
  ];

  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>Ledentickets</h1>
          <p className="page-subtitle">
            Elk actief lid heeft één ticket voor het carnavalsjaar, geldig tijdens de hele carnaval. Het lid ziet de
            QR-code in de app onder Mijn QR; die is gekoppeld aan één toestel en ververst elke 30 seconden.
          </p>
        </div>
        {canManage ? (
          <div className="actions">
            <button
              type="button"
              className="button"
              disabled={issue.isPending}
              onClick={() =>
                issue.mutate(undefined, {
                  onSuccess: (data) =>
                    setMessage(
                      `${(data as { issued: number } | undefined)?.issued ?? 0} tickets uitgegeven aan actieve leden zonder ticket.`,
                    ),
                })
              }
            >
              Tickets uitgeven
            </button>
          </div>
        ) : null}
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={issue.error} />
      <div className="toolbar">
        <div className="field grow-field">
          <label htmlFor="ticket-zoeken">Zoeken op naam of lidnummer</label>
          <input
            id="ticket-zoeken"
            type="search"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
          />
        </div>
        <div className="field">
          <label htmlFor="ticket-status">Status</label>
          <select
            id="ticket-status"
            value={status}
            onChange={(e) => {
              setStatus(e.target.value);
              setPage(1);
            }}
          >
            <option value="">Alle</option>
            <option value="Active">Actief</option>
            <option value="Blocked">Geblokkeerd</option>
          </select>
        </div>
      </div>
      <ProblemAlert error={tickets.error} />
      <DataTable
        caption="Ledentickets"
        columns={columns}
        data={tickets.data?.items ?? []}
        emptyText="Geen tickets gevonden. Leden krijgen hun ticket bij het openen van Mijn QR, of via Tickets uitgeven."
        footer={
          tickets.data ? (
            <Pagination
              page={tickets.data.page}
              pageSize={tickets.data.pageSize}
              totalCount={tickets.data.totalCount}
              onPage={setPage}
              noun="tickets"
            />
          ) : null
        }
      />
      <Dialog
        open={pending !== null}
        title={pending ? `${actionLabels[pending.action]}: ${pending.ticket.memberName}` : ''}
        onClose={() => setPending(null)}
      >
        <form onSubmit={confirm}>
          <p>{pending ? actionExplanations[pending.action] : ''}</p>
          {pending?.action === 'Block' ? (
            <div className="field">
              <label htmlFor="ticket-reden">Reden</label>
              <textarea
                id="ticket-reden"
                rows={3}
                required
                maxLength={500}
                value={reason}
                onChange={(e) => setReason(e.target.value)}
              />
            </div>
          ) : null}
          <ProblemAlert error={act.error} />
          <div className="actions">
            <button type="button" className="button secondary" onClick={() => setPending(null)}>
              Annuleren
            </button>
            <button
              type="submit"
              className={pending?.action === 'Block' ? 'button danger' : 'button'}
              disabled={act.isPending || (pending?.action === 'Block' && !reason.trim())}
            >
              {pending ? actionLabels[pending.action] : ''}
            </button>
          </div>
        </form>
      </Dialog>
    </>
  );
}
