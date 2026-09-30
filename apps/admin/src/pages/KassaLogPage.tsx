import { useState } from 'react';
import { useKassaLog, type KassaLogRow } from '../api/sales';
import { DataTable, columnHelper } from '../components/DataTable';
import { ProblemAlert } from '../components/ProblemAlert';
import { formatDate } from '../format';

/** Leesbare reden bij een geweigerde kassascan. */
const reasonLabels: Record<string, string> = {
  AlreadyIssued: 'al uitgegeven',
  Blocked: 'geannuleerd of geblokkeerd',
  WrongDevice: 'ander toestel',
  Expired: 'verlopen code',
  InvalidSignature: 'nagemaakte code',
  WrongPurpose: 'geen munten-QR',
  UnknownTicket: 'onbekende code',
  Unreadable: 'onleesbaar',
  MembershipInactive: 'geen actief lid',
  Reissued: 'oude code',
};

function time(value: string | null | undefined) {
  return value ? new Date(value).toLocaleTimeString('nl-NL', { hour: '2-digit', minute: '2-digit', timeZone: 'Europe/Amsterdam' }) : '';
}

function resultBadge(r: KassaLogRow) {
  if (r.outcome === 'Issued') return <span className="badge ok">Uitgegeven</span>;
  if (r.outcome === 'Ready') return <span className="badge warn">Gescand, niet uitgegeven</span>;
  return <span className="badge error">Geweigerd: {reasonLabels[r.reason ?? ''] ?? r.reason ?? 'onbekend'}</span>;
}

/**
 * Kassalog (fase 19c, Figma "Portal / Kassalog"): elke scan van een munten-QR bij de kassa en elke keer dat "Bestelling
 * uitgegeven" is ingedrukt, per dag (tot 06:00 de volgende ochtend). Alleen-lezen.
 */
export function KassaLogPage() {
  const [day, setDay] = useState('');
  const log = useKassaLog(day);
  const d = log.data;

  const helper = columnHelper<KassaLogRow>();
  const columns = [
    helper.accessor('scannedAt', {
      header: 'Tijd',
      cell: (info) => (
        <>
          <strong>{time(info.getValue())}</strong>
          {info.row.original.issuedAt ? (
            <div className="muted small-text">uitgegeven {time(info.row.original.issuedAt)}</div>
          ) : null}
        </>
      ),
    }),
    helper.accessor('memberName', {
      header: 'Lid',
      cell: (info) => (
        <>
          <strong>{info.getValue() ?? '—'}</strong>
          {info.row.original.memberGroup ? <div className="muted small-text">{info.row.original.memberGroup}</div> : null}
        </>
      ),
    }),
    helper.accessor('quantity', { header: 'Munten', cell: (info) => info.getValue() ?? '—' }),
    helper.accessor('orderNumber', {
      header: 'Bestelling',
      cell: (info) => (
        <>
          {info.getValue() ?? '—'}
          {info.row.original.paidWith ? <div className="muted small-text">betaald met {info.row.original.paidWith}</div> : null}
        </>
      ),
    }),
    helper.display({ id: 'resultaat', header: 'Resultaat', cell: (info) => resultBadge(info.row.original) }),
    helper.accessor('operatorName', {
      header: 'Kassa / medewerker',
      cell: (info) => [info.row.original.deviceName, info.getValue()].filter(Boolean).join(' · '),
    }),
  ];

  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>Kassalog</h1>
          <p className="page-subtitle">
            Elke scan van een munten-QR bij de kassa en elke keer dat &quot;Bestelling uitgegeven&quot; is ingedrukt. Een
            geweigerde scan staat er ook in, met de reden. Alleen-lezen.
          </p>
        </div>
        <div className="field">
          <label htmlFor="kassalog-dag">Dag</label>
          <input id="kassalog-dag" type="date" value={day || d?.day || ''} onChange={(e) => setDay(e.target.value)} />
        </div>
      </div>
      <ProblemAlert error={log.error} />
      {d ? (
        <section className="kpis" aria-label="Kerncijfers kassa">
          <div className="kpi">
            <span className="kpi-label">Uitgegeven</span>
            <span className="kpi-value">{d.issued}</span>
            <span className="kpi-hint">bestellingen</span>
          </div>
          <div className="kpi">
            <span className="kpi-label">Munten</span>
            <span className="kpi-value">{d.tokensIssued}</span>
            <span className="kpi-hint">uitgegeven</span>
          </div>
          <div className="kpi">
            <span className="kpi-label">Geweigerd</span>
            <span className="kpi-value">{d.refused}</span>
            <span className="kpi-hint">scans</span>
          </div>
        </section>
      ) : null}
      <section className="card" aria-labelledby="kassalog-kop">
        <h2 id="kassalog-kop">{d ? `Scans op ${formatDate(d.day)}` : 'Scans'}</h2>
        <p className="card-hint">Nieuwste bovenaan. De dag loopt tot 06:00 de volgende ochtend.</p>
        <DataTable caption="Kassalog" columns={columns} data={d?.rows ?? []} emptyText="Nog geen kassascans op deze dag." />
        <p className="card-hint">
          Rol Kassa: mag munten-QR&apos;s scannen en bestellingen uitgeven. Ziet verder geen ledengegevens en kan niets
          terugdraaien; correcties doet het bestuur.
        </p>
      </section>
    </>
  );
}
