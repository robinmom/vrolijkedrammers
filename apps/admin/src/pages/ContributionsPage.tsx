import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { formatAmount, kindLabel, useContributions, type ContributionLine } from '../api/contributions';
import { Field } from '../components/Field';
import { ProblemAlert } from '../components/ProblemAlert';

const statusLabels: Record<ContributionLine['status'], string> = {
  Due: 'Betaalt',
  Exempt: 'Vrijgesteld',
  PaidByPartner: 'Via partner',
  Unknown: 'Onbekend',
};

type Filter = 'all' | 'Due' | 'Exempt' | 'PaidByPartner' | 'attention';

function today() {
  return new Date().toISOString().slice(0, 10);
}

/**
 * Contributie (fase 23a): wat elk actief lid betaalt op de peildatum (straks de incassodatum). Senior (65+) volgt uit de
 * leeftijd op die datum; bij twee personen alleen als beiden 65+ zijn. Lidmaatschap en vrijstelling stel je in bij het lid.
 */
export function ContributionsPage() {
  const api = useApi();
  const [date, setDate] = useState(today());
  const [filter, setFilter] = useState<Filter>('all');
  const overview = useContributions(date);
  const [exportError, setExportError] = useState<unknown>(null);

  async function exportExcel() {
    setExportError(null);
    try {
      const { data } = await api.GET('/api/v1/admin/contributions/export', {
        params: { query: { date } },
        parseAs: 'blob',
      });
      if (data) {
        const url = URL.createObjectURL(data);
        const link = document.createElement('a');
        link.href = url;
        link.download = `contributie-${date.replaceAll('-', '')}.xlsx`;
        link.click();
        URL.revokeObjectURL(url);
      }
    } catch (error) {
      setExportError(error);
    }
  }

  const o = overview.data;
  const attention = (l: ContributionLine) => l.status === 'Unknown' || !!l.note;
  const lines = (o?.lines ?? []).filter((l) =>
    filter === 'all' ? true : filter === 'attention' ? attention(l) : l.status === filter,
  );
  const count = (f: Filter) =>
    (o?.lines ?? []).filter((l) => (f === 'attention' ? attention(l) : f === 'all' ? true : l.status === f)).length;

  return (
    <>
      <div className="page-header">
        <h1>Contributie</h1>
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => void exportExcel()} disabled={!o}>
            Exporteren (Excel)
          </button>
        </div>
      </div>
      <ProblemAlert error={exportError ?? overview.error} />
      <section className="card" aria-labelledby="peildatum">
        <h2 id="peildatum" className="visually-hidden">
          Peildatum
        </h2>
        <Field
          label="Peildatum (incassodatum)"
          hint="Bepaalt het tarief en wie 65+ is"
          type="date"
          required
          value={date}
          onChange={(e) => e.target.value && setDate(e.target.value)}
        />
      </section>
      {o ? (
        <>
          <section className="kpis" aria-label="Totalen">
            {o.totals.map((t) => (
              <div key={t.label} className="kpi">
                <span className="kpi-label">{t.label}</span>
                <span className="kpi-value">{formatAmount(t.amount)}</span>
                <span className="kpi-hint">{t.count} leden</span>
              </div>
            ))}
            <div className="kpi">
              <span className="kpi-label">Totaal</span>
              <span className="kpi-value">{formatAmount(o.total)}</span>
              <span className="kpi-hint">{count('Due')} betalers</span>
            </div>
          </section>

          <section className="card" aria-labelledby="per-lid">
            <div className="card-header">
              <h2 id="per-lid">Per lid</h2>
              <select aria-label="Filter" value={filter} onChange={(e) => setFilter(e.target.value as Filter)}>
                <option value="all">Alle actieve leden ({count('all')})</option>
                <option value="Due">Betalen ({count('Due')})</option>
                <option value="Exempt">Vrijgesteld ({count('Exempt')})</option>
                <option value="PaidByPartner">Via partner ({count('PaidByPartner')})</option>
                <option value="attention">Aandacht nodig ({count('attention')})</option>
              </select>
            </div>
            <div className="table-scroll" tabIndex={0} role="region" aria-label="Contributie per lid">
              <table className="table compact">
                <caption className="visually-hidden">Contributie per lid</caption>
                <thead>
                  <tr>
                    <th scope="col">Lidnummer</th>
                    <th scope="col">Naam</th>
                    <th scope="col">Lidmaatschap</th>
                    <th scope="col">Bedrag</th>
                    <th scope="col">Status</th>
                    <th scope="col">Opmerking</th>
                  </tr>
                </thead>
                <tbody>
                  {lines.map((l) => (
                    <tr key={l.memberId}>
                      <td>{l.memberNumber}</td>
                      <td>
                        <Link to="/leden/$id" params={{ id: l.memberId }}>
                          {l.fullName}
                        </Link>
                      </td>
                      <td>
                        {kindLabel(l.kind, l.senior)}
                        {l.kindFromEBoekhouden && l.kind ? <span className="muted"> (e-Boekhouden)</span> : null}
                      </td>
                      <td>{formatAmount(l.amount)}</td>
                      <td>{statusLabels[l.status]}</td>
                      <td>{l.note ?? (l.partnerName ? `Partner: ${l.partnerName}` : '')}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        </>
      ) : overview.error ? null : (
        <p>Laden…</p>
      )}
      <p className="muted">
        De tarieven beheer je onder <Link to="/lidmaatschappen">Lidmaatschappen</Link>.
      </p>
    </>
  );
}
