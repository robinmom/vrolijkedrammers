import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useMemberReport, type MemberReport } from '../api/hooks';
import { ProblemAlert } from '../components/ProblemAlert';

type Rows = MemberReport['byStatus'];

function ReportTable({ title, header, rows }: { title: string; header: string; rows: Rows }) {
  const id = `rapport-${header.toLowerCase().replace(/\W+/g, '-')}`;
  return (
    <section className="card" aria-labelledby={id}>
      <h2 id={id}>{title}</h2>
      {rows.length === 0 ? (
        <p className="muted">Geen gegevens.</p>
      ) : (
        <div className="table-scroll" tabIndex={0} role="region" aria-label={title}>
          <table className="table compact">
            <caption className="visually-hidden">{title}</caption>
            <thead>
              <tr>
                <th scope="col">{header}</th>
                <th scope="col">Aantal</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.label}>
                  <td>{r.label}</td>
                  <td>{r.count}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

/** Basisrapportage leden (fase 8c): alleen aantallen, geen namen. */
export function ReportsPage() {
  const api = useApi();
  const report = useMemberReport();
  const [exportError, setExportError] = useState<unknown>(null);

  async function exportExcel() {
    setExportError(null);
    try {
      const { data } = await api.GET('/api/v1/admin/reports/members/export', { parseAs: 'blob' });
      if (data) {
        const url = URL.createObjectURL(data);
        const link = document.createElement('a');
        link.href = url;
        link.download = 'ledenrapport.xlsx';
        link.click();
        URL.revokeObjectURL(url);
      }
    } catch (error) {
      setExportError(error);
    }
  }

  return (
    <>
      <div className="page-header">
        <h1>Rapportage leden</h1>
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => void exportExcel()}>
            Exporteren (Excel)
          </button>
        </div>
      </div>
      <ProblemAlert error={exportError ?? report.error} />
      {report.data ? (
        <>
          <p>
            {report.data.total} leden in de app, waarvan {report.data.active} actief. Rollen, leeftijd, inschrijfjaar en groepen tellen
            alleen actieve leden.
          </p>
          <div className="grid-2">
            <ReportTable title="Per status" header="Status" rows={report.data.byStatus} />
            <ReportTable title="Per leeftijdsklasse" header="Leeftijd" rows={report.data.byAgeClass} />
            <ReportTable title="Per rol (met app-account)" header="Rol" rows={report.data.byRole} />
            <ReportTable title="Per groep" header="Groep" rows={report.data.byGroup} />
            <ReportTable title="Per inschrijfjaar" header="Inschrijfjaar" rows={report.data.byJoinYear} />
          </div>
        </>
      ) : report.error ? null : (
        <p>Laden…</p>
      )}
    </>
  );
}
