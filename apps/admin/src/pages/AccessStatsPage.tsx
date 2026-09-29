import { useEffect, useState } from 'react';
import { useAccessOverview, useAccessStats } from '../api/hooks';
import { BarChart, PieChart } from '../components/Charts';
import { ProblemAlert } from '../components/ProblemAlert';
import { formatDateTime } from '../format';
import { reasonLabels } from './AccessLogPage';

const hour = new Intl.DateTimeFormat('nl-NL', { hour: '2-digit', hourCycle: 'h23', timeZone: 'Europe/Amsterdam' });
const percent = (part: number, total: number) => (total > 0 ? `${Math.round((part / total) * 100)}%` : '–');

/**
 * Toegangsstatistieken (<c>ticket.read</c>): per carnavalsdag of activiteit met toegangscontrole hoeveel leden er binnen
 * kwamen, wanneer (per uur), hoe (QR, inchecken, offline) en waarom er geweigerd werd; plus een vergelijking van alle momenten.
 */
export function AccessStatsPage() {
  const overview = useAccessOverview();
  const [key, setKey] = useState('');
  useEffect(() => {
    if (!key && overview.data?.[0]) setKey(overview.data[0].moment.key);
  }, [overview.data, key]);
  const stats = useAccessStats(key);
  const s = stats.data;
  const first = s ? s.inside : 0;

  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>Toegangsstatistieken</h1>
          <p className="page-subtitle">
            Hoeveel actieve leden er binnen kwamen, wanneer het druk was en hoe er gescand werd — per carnavalsdag of
            activiteit met toegangscontrole.
          </p>
        </div>
      </div>
      <ProblemAlert error={overview.error ?? stats.error} />
      {overview.data && overview.data.length === 0 ? (
        <p className="muted">Er is nog niet gescand of ingecheckt.</p>
      ) : null}
      {overview.data && overview.data.length > 0 ? (
        <div className="toolbar">
          <div className="field grow-field">
            <label htmlFor="stats-moment">Carnavalsdag of activiteit</label>
            <select id="stats-moment" value={key} onChange={(e) => setKey(e.target.value)}>
              {overview.data.map((o) => (
                <option key={o.moment.key} value={o.moment.key}>
                  {o.moment.title} · {formatDateTime(o.moment.startAt)}
                </option>
              ))}
            </select>
          </div>
        </div>
      ) : null}

      {s ? (
        <>
          <section className="kpis" aria-label="Kerncijfers">
            <div className="kpi">
              <span className="kpi-label">Binnen</span>
              <span className="kpi-value">{s.inside}</span>
              <span className="kpi-hint">
                {percent(s.inside, s.activeMembers)} van {s.activeMembers} actieve leden
              </span>
            </div>
            <div className="kpi">
              <span className="kpi-label">Scans en inchecks</span>
              <span className="kpi-value">{s.scans}</span>
              <span className="kpi-hint">Inclusief herhaald en geweigerd</span>
            </div>
            <div className="kpi">
              <span className="kpi-label">Geweigerd</span>
              <span className="kpi-value">{s.refused}</span>
              <span className="kpi-hint">{percent(s.refused, s.scans)} van de scans</span>
            </div>
            <div className="kpi">
              <span className="kpi-label">Offline gescand</span>
              <span className="kpi-value">{s.offlineScans}</span>
              <span className="kpi-hint">{s.offlineConflicts} conflict(en) na synchroniseren</span>
            </div>
          </section>

          <div className="columns">
            <div>
              <section className="card" aria-labelledby="per-uur">
                <h2 id="per-uur">Aankomsten per uur</h2>
                {s.perHour.length === 0 ? (
                  <p className="muted">Nog geen scans.</p>
                ) : (
                  <BarChart
                    title="Aankomsten per uur"
                    primaryLabel="Aankomsten"
                    secondaryLabel="Alle scans"
                    bars={s.perHour.map((h) => ({
                      label: `${hour.format(new Date(h.hourStart))}u`,
                      value: h.arrivals,
                      secondary: h.scans,
                    }))}
                  />
                )}
              </section>
              <section className="card" aria-labelledby="weigeringen">
                <h2 id="weigeringen">Redenen van weigeren</h2>
                {s.refusalReasons.length === 0 ? (
                  <p className="muted">Niemand geweigerd.</p>
                ) : (
                  <ul className="list">
                    {s.refusalReasons.map((r) => (
                      <li key={r.reason} className="list-row">
                        <p className="grow">{reasonLabels[r.reason] ?? r.reason}</p>
                        <span className="badge error">{r.count}</span>
                      </li>
                    ))}
                  </ul>
                )}
              </section>
            </div>
            <div>
              <section className="card" aria-labelledby="binnen">
                <h2 id="binnen">Binnen van het totaal</h2>
                <PieChart
                  title="Binnen van het totaal aantal actieve leden"
                  slices={[
                    { label: 'Binnen', value: s.inside, color: 'var(--dvd-success-text)' },
                    {
                      label: 'Niet binnen',
                      value: Math.max(0, s.activeMembers - s.inside),
                      color: 'var(--dvd-border)',
                    },
                  ]}
                />
              </section>
              <section className="card" aria-labelledby="hoe">
                <h2 id="hoe">Hoe kwamen leden binnen</h2>
                <PieChart
                  title="Eerste binnenkomst via QR of inchecken"
                  size={120}
                  slices={[
                    { label: 'Mijn QR', value: s.viaQr, color: 'var(--dvd-link-text)' },
                    { label: 'Ingecheckt via ledenlijst', value: s.viaCheckIn, color: 'var(--dvd-feestgoud)' },
                  ]}
                />
                <h3 className="subheading">Opnieuw gescand</h3>
                <ul className="list">
                  <li className="list-row">
                    <p className="grow">Eerste keer binnen</p>
                    <strong>{first}</strong>
                  </li>
                  <li className="list-row">
                    <p className="grow">Opnieuw, zelfde toestel</p>
                    <strong>{s.repeatsSameDevice}</strong>
                  </li>
                  <li className="list-row">
                    <p className="grow">Opnieuw, ander toestel (oranje)</p>
                    <strong>{s.repeatsOtherDevice}</strong>
                  </li>
                </ul>
              </section>
            </div>
          </div>
        </>
      ) : null}

      {overview.data && overview.data.length > 1 ? (
        <section className="card" aria-labelledby="vergelijking">
          <h2 id="vergelijking">Alle momenten vergeleken</h2>
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Alle momenten">
            <table className="table">
              <caption className="visually-hidden">Alle momenten vergeleken</caption>
              <thead>
                <tr>
                  <th scope="col">Moment</th>
                  <th scope="col">Binnen</th>
                  <th scope="col">Opkomst</th>
                  <th scope="col">Scans</th>
                  <th scope="col">Geweigerd</th>
                  <th scope="col">Ingecheckt</th>
                  <th scope="col">Offline</th>
                </tr>
              </thead>
              <tbody>
                {overview.data.map((o) => (
                  <tr key={o.moment.key}>
                    <td>
                      <button type="button" className="button ghost small" onClick={() => setKey(o.moment.key)}>
                        {o.moment.title}
                      </button>
                    </td>
                    <td>{o.inside}</td>
                    <td>{percent(o.inside, o.activeMembers)}</td>
                    <td>{o.scans}</td>
                    <td>{o.refused}</td>
                    <td>{o.viaCheckIn}</td>
                    <td>{o.offlineScans}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      ) : null}
    </>
  );
}
