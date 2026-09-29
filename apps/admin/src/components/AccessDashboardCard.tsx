import { Link } from '@tanstack/react-router';
import { useAccessDashboard } from '../api/hooks';
import { PieChart } from './Charts';

/**
 * Dashboardblok "Toegang": hoeveel actieve leden er binnen zijn (taartdiagram) bij het huidige of laatste toegangsmoment,
 * de belangrijkste scancijfers en "Klaar voor de deur" (leden met of zonder gekoppelde Mijn QR).
 */
export function AccessDashboardCard() {
  const dashboard = useAccessDashboard(true);
  const d = dashboard.data;
  if (!d) return null;
  const stats = d.stats;
  const r = d.readiness;

  return (
    <section className="card" aria-labelledby="toegang">
      <div className="card-header">
        <h2 id="toegang">Toegang</h2>
        <Link to="/toegang/statistieken" className="button ghost small">
          Statistieken
        </Link>
      </div>
      {stats ? (
        <>
          <p>
            {d.live ? <span className="badge ok">Nu bezig</span> : <span className="badge">Laatste moment</span>}{' '}
            <strong>{stats.moment.title}</strong>
          </p>
          <div className="access-overview">
            <PieChart
              title="Binnen van het totaal aantal actieve leden"
              slices={[
                { label: 'Binnen', value: stats.inside, color: 'var(--dvd-success-text)' },
                {
                  label: 'Nog niet binnen',
                  value: Math.max(0, stats.activeMembers - stats.inside),
                  color: 'var(--dvd-border)',
                },
              ]}
            />
            <dl className="mini-kpis">
              <div>
                <dt>Scans</dt>
                <dd>{stats.scans}</dd>
              </div>
              <div>
                <dt>Geweigerd</dt>
                <dd>{stats.refused}</dd>
              </div>
              <div>
                <dt>Ingecheckt</dt>
                <dd>{stats.viaCheckIn}</dd>
              </div>
            </dl>
          </div>
        </>
      ) : (
        <p className="muted">
          Er is nog niet gescand. Zodra een carnavalsdag of activiteit met toegangscontrole begint, zie je hier wie er
          binnen is.
        </p>
      )}
      <h3 className="subheading">Klaar voor de deur</h3>
      <p>
        <strong>{r.bound}</strong> van de {r.activeMembers} actieve leden hebben Mijn QR gekoppeld.{' '}
        {r.notBound > 0 ? (
          <>
            <span className="badge warn">{r.notBound} nog niet</span> — die check je bij de deur in via de{' '}
            <Link to="/leden">ledenlijst</Link>.
          </>
        ) : null}
      </p>
    </section>
  );
}
