import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { euro } from '../api/advertisers';
import { useCarnivalYears } from '../api/hooks';
import { BarChart } from '../components/Charts';
import { ProblemAlert } from '../components/ProblemAlert';

/** Keuze van het carnavalsjaar; leeg = het actieve jaar (de backend kiest dat zelf). */
function YearFilter({ value, onChange }: { value: number | undefined; onChange: (id: number | undefined) => void }) {
  const years = useCarnivalYears();
  return (
    <div className="field">
      <label htmlFor="finance-year">Carnavalsjaar</label>
      <select
        id="finance-year"
        value={value ?? ''}
        onChange={(event) => onChange(event.target.value ? Number(event.target.value) : undefined)}
      >
        <option value="">Huidig carnavalsjaar</option>
        {years.data?.map((y) => (
          <option key={y.id} value={y.id}>
            {y.name}
            {y.active ? ' (actief)' : ''}
          </option>
        ))}
      </select>
    </div>
  );
}

/** Groei als tekst, bijv. "+13,1%"; null wanneer er geen vorig jaar is om mee te vergelijken. */
function growth(percent: number | null | undefined) {
  if (percent === null || percent === undefined) return '—';
  const text = percent.toFixed(1).replace('.', ',');
  return `${percent > 0 ? '+' : ''}${text}%`;
}

/**
 * Totaaloverzicht (fase 4c): de inkomsten van een carnavalsjaar uit adverteerders, Dansgardefestival en kaartverkoop
 * bij elkaar, met per bron het verwachte bedrag, wat betaald is en wat nog openstaat.
 */
export function FinanceOverviewPage() {
  const api = useApi();
  const [carnivalYearId, setCarnivalYearId] = useState<number | undefined>(undefined);

  const overview = useQuery({
    queryKey: ['finance', 'overview', carnivalYearId ?? 'current'],
    queryFn: async () =>
      (await api.GET('/api/v1/admin/finance/overview', { params: { query: { carnivalYearId } } })).data!,
  });

  const r = overview.data;
  return (
    <>
      <div className="page-header">
        <div>
          <h1>Totaaloverzicht{r?.year ? ` ${r.year}` : ''}</h1>
          <p className="muted">
            De inkomsten van het carnavalsjaar uit adverteerders, Dansgardefestival en kaartverkoop bij elkaar, met per
            bron de stand.
          </p>
        </div>
        <YearFilter value={carnivalYearId} onChange={setCarnivalYearId} />
      </div>
      <ProblemAlert error={overview.error} />

      {r ? (
        <>
          <section className="kpis" aria-label="Kerncijfers">
            <div className="kpi">
              <span className="kpi-label">Totaal incl. btw</span>
              <span className="kpi-value">{euro(r.total)}</span>
              <span className="kpi-hint">Adverteerders, festival en kaartverkoop</span>
            </div>
            <div className="kpi">
              <span className="kpi-label">Betaald</span>
              <span className="kpi-value">{euro(r.paid)}</span>
            </div>
            <div className="kpi">
              <span className="kpi-label">Openstaand</span>
              <span className="kpi-value">{euro(r.open)}</span>
            </div>
          </section>

          <section className="card">
            <h2>Inkomsten per bron</h2>
            <div className="table-scroll" tabIndex={0} role="region" aria-label="Inkomsten per bron">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">Bron</th>
                    <th scope="col">Verwacht</th>
                    <th scope="col">Betaald</th>
                    <th scope="col">Openstaand</th>
                  </tr>
                </thead>
                <tbody>
                  {r.sources.map((s) => (
                    <tr key={s.source}>
                      <td>{s.source}</td>
                      <td>{euro(s.total)}</td>
                      <td>{euro(s.paid)}</td>
                      <td>{euro(s.total - s.paid)}</td>
                    </tr>
                  ))}
                  <tr className="total-row">
                    <th scope="row">Totaal</th>
                    <td>{euro(r.total)}</td>
                    <td>{euro(r.paid)}</td>
                    <td>{euro(r.open)}</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </section>
        </>
      ) : (
        <p>Laden…</p>
      )}
    </>
  );
}

/**
 * Statistieken (fase 4c): alles van het carnavalsjaar — ook de contributie — met de groei t.o.v. vorig jaar, de
 * verdeling per bron en de reeks over de carnavalsjaren.
 */
export function FinanceStatisticsPage() {
  const api = useApi();
  const [carnivalYearId, setCarnivalYearId] = useState<number | undefined>(undefined);

  const stats = useQuery({
    queryKey: ['finance', 'statistics', carnivalYearId ?? 'current'],
    queryFn: async () =>
      (await api.GET('/api/v1/admin/finance/statistics', { params: { query: { carnivalYearId } } })).data!,
  });

  const r = stats.data;
  const paidShare = r && r.total > 0 ? Math.round((r.paid / r.total) * 100) : 0;
  return (
    <>
      <div className="page-header">
        <div>
          <h1>Statistieken{r?.year ? ` ${r.year}` : ''}</h1>
          <p className="muted">
            Alle inkomsten van het carnavalsjaar — inclusief contributie — met de groei, de verdeling per bron en de
            reeks per carnavalsjaar.
          </p>
        </div>
        <YearFilter value={carnivalYearId} onChange={setCarnivalYearId} />
      </div>
      <ProblemAlert error={stats.error} />

      {r ? (
        <>
          <section className="kpis" aria-label="Kerncijfers">
            <div className="kpi">
              <span className="kpi-label">Totaal {r.year}</span>
              <span className="kpi-value">{euro(r.total)}</span>
              <span className="kpi-hint">Alle bronnen incl. contributie</span>
            </div>
            <div className="kpi">
              <span className="kpi-label">Groei t.o.v. vorig jaar</span>
              <span className="kpi-value">{growth(r.growthPercent)}</span>
            </div>
            <div className="kpi">
              <span className="kpi-label">Betaald</span>
              <span className="kpi-value">{euro(r.paid)}</span>
              <span className="kpi-hint">{paidShare}% van het totaal</span>
            </div>
            <div className="kpi">
              <span className="kpi-label">Openstaand</span>
              <span className="kpi-value">{euro(r.open)}</span>
            </div>
          </section>

          <div className="finance-charts">
            <section className="card" aria-labelledby="per-bron">
              <h2 id="per-bron">Inkomsten per bron ({r.year})</h2>
              {r.sources.length === 0 ? (
                <p className="muted">Nog geen inkomsten in {r.year}.</p>
              ) : (
                <BarChart
                  title={`Inkomsten per bron in ${r.year}`}
                  primaryLabel="Inkomsten"
                  format={euro}
                  bars={r.sources.map((s) => ({ label: s.source, value: s.total }))}
                />
              )}
            </section>
            <section className="card" aria-labelledby="per-jaar">
              <h2 id="per-jaar">Totaal per carnavalsjaar</h2>
              {r.perYear.length === 0 ? (
                <p className="muted">Nog geen carnavalsjaren.</p>
              ) : (
                <BarChart
                  title="Totale inkomsten per carnavalsjaar"
                  primaryLabel="Inkomsten"
                  format={euro}
                  bars={r.perYear.map((y) => ({ label: y.year, value: y.total }))}
                />
              )}
            </section>
          </div>

          <section className="card">
            <h2>Verdeling per bron</h2>
            <div className="table-scroll" tabIndex={0} role="region" aria-label="Verdeling per bron">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">Bron</th>
                    <th scope="col">Totaal</th>
                    <th scope="col">Aandeel</th>
                  </tr>
                </thead>
                <tbody>
                  {r.sources.map((s) => (
                    <tr key={s.source}>
                      <td>{s.source}</td>
                      <td>{euro(s.total)}</td>
                      <td>{r.total > 0 ? `${Math.round((s.total / r.total) * 100)}%` : '—'}</td>
                    </tr>
                  ))}
                  <tr className="total-row">
                    <th scope="row">Totaal</th>
                    <td>{euro(r.total)}</td>
                    <td>100%</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </section>
        </>
      ) : (
        <p>Laden…</p>
      )}
    </>
  );
}
