import { useEffect, useState } from 'react';
import { useAccessEvents, useAccessScans } from '../api/hooks';
import type { components } from '@drammers/api-client';
import { DataTable, columnHelper } from '../components/DataTable';
import { Pagination } from '../components/Pagination';
import { ProblemAlert } from '../components/ProblemAlert';
import { formatDateTime } from '../format';

type Row = components['schemas']['AccessScanRow'];

const outcomeLabels: Record<string, { label: string; tone: string }> = {
  Admitted: { label: 'Toegelaten', tone: 'ok' },
  AdmittedAgain: { label: 'Opnieuw (zelfde toestel)', tone: 'ok' },
  Warning: { label: 'Let op', tone: 'warn' },
  Refused: { label: 'Geweigerd', tone: 'error' },
};

const reasonLabels: Record<string, string> = {
  Unreadable: 'Onleesbare code',
  UnknownTicket: 'Onbekend ticket',
  Blocked: 'Ticket geblokkeerd',
  MembershipInactive: 'Geen actief lid',
  OutsideValidity: 'Buiten carnaval',
  Reissued: 'Oude code (heruitgegeven)',
  WrongDevice: 'Ander toestel',
  InvalidSignature: 'Nagemaakte code',
  Expired: 'Verlopen code',
  OtherDevice: 'Al gescand op ander toestel',
  CheckedInManually: 'Al ingecheckt',
};

const helper = columnHelper<Row>();
const columns = [
  helper.accessor('scannedAt', { header: 'Tijd', cell: (info) => formatDateTime(info.getValue()) }),
  helper.accessor('memberName', { header: 'Lid', cell: (info) => info.getValue() ?? '–' }),
  helper.accessor('method', { header: 'Hoe', cell: (info) => (info.getValue() === 'Manual' ? 'Ingecheckt' : 'QR') }),
  helper.accessor('outcome', {
    header: 'Uitkomst',
    cell: (info) => {
      const o = outcomeLabels[info.getValue()];
      const decision = info.row.original.decision;
      return (
        <>
          <span className={`badge ${o?.tone ?? ''}`}>{o?.label ?? info.getValue()}</span>
          {decision ? <span className="badge">{decision === 'Admitted' ? 'Toch toegelaten' : 'Geweigerd'}</span> : null}
        </>
      );
    },
  }),
  helper.accessor('reason', {
    header: 'Reden',
    cell: (info) => (info.getValue() ? (reasonLabels[info.getValue()!] ?? info.getValue()) : '–'),
  }),
  helper.accessor('operatorName', {
    header: 'Door',
    cell: (info) => `${info.getValue()}${info.row.original.deviceName ? ` · ${info.row.original.deviceName}` : ''}`,
  }),
];

/**
 * Toegangslog (fase 14, <c>ticket.read</c>): per activiteit met toegangscontrole alle scans en inchecks, ook de
 * geweigerde, met de tellers binnen / scans / geweigerd.
 */
export function AccessLogPage() {
  const events = useAccessEvents();
  const [eventId, setEventId] = useState('');
  const [page, setPage] = useState(1);
  const scans = useAccessScans(eventId, page);
  useEffect(() => {
    if (!eventId && events.data?.[0]) setEventId(events.data[0].id);
  }, [events.data, eventId]);
  const selected = events.data?.find((e) => e.id === eventId);

  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>Toegangslog</h1>
          <p className="page-subtitle">
            Alle scans en inchecks bij activiteiten met toegangscontrole, ook de geweigerde. Zet toegangscontrole aan
            bij een activiteit in de agenda; wie de rol Deurcontrole heeft, kan dan scannen en inchecken.
          </p>
        </div>
      </div>
      <ProblemAlert error={events.error} />
      {events.data && events.data.length === 0 ? (
        <p className="muted">Nog geen activiteiten met toegangscontrole.</p>
      ) : null}
      {events.data && events.data.length > 0 ? (
        <div className="toolbar">
          <div className="field grow-field">
            <label htmlFor="toegang-activiteit">Activiteit</label>
            <select
              id="toegang-activiteit"
              value={eventId}
              onChange={(e) => {
                setEventId(e.target.value);
                setPage(1);
              }}
            >
              {events.data.map((e) => (
                <option key={e.id} value={e.id}>
                  {e.title} · {formatDateTime(e.startAt)}
                </option>
              ))}
            </select>
          </div>
        </div>
      ) : null}
      {selected ? (
        <section className="kpis" aria-label="Tellers">
          <div className="kpi">
            <span className="kpi-label">Binnen</span>
            <span className="kpi-value">{selected.counts.inside}</span>
            <span className="kpi-hint">Unieke leden</span>
          </div>
          <div className="kpi">
            <span className="kpi-label">Scans en inchecks</span>
            <span className="kpi-value">{selected.counts.scans}</span>
            <span className="kpi-hint">Inclusief herhaald</span>
          </div>
          <div className="kpi">
            <span className="kpi-label">Geweigerd</span>
            <span className="kpi-value">{selected.counts.refused}</span>
            <span className="kpi-hint">Rood of Weigeren</span>
          </div>
        </section>
      ) : null}
      <ProblemAlert error={scans.error} />
      {eventId ? (
        <DataTable
          caption="Toegangslog"
          columns={columns}
          data={scans.data?.items ?? []}
          emptyText="Nog geen scans bij deze activiteit."
          footer={
            scans.data ? (
              <Pagination
                page={scans.data.page}
                pageSize={scans.data.pageSize}
                totalCount={scans.data.totalCount}
                onPage={setPage}
                noun="scans"
              />
            ) : null
          }
        />
      ) : null}
    </>
  );
}
