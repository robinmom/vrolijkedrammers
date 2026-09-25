import { useDashboard } from '../api/hooks';
import { ProblemAlert } from '../components/ProblemAlert';
import { formatDateTime } from '../format';

const statusLabels: Record<string, string> = { Healthy: 'In orde', Degraded: 'Verminderd', Unhealthy: 'Storing' };

export function DashboardPage() {
  const dashboard = useDashboard(true);
  const d = dashboard.data;
  return (
    <>
      <h1>Dashboard</h1>
      <ProblemAlert error={dashboard.error} />
      {d ? (
        <div className="grid">
          <section className="card" aria-labelledby="kerncijfers">
            <h2 id="kerncijfers">Kerncijfers</h2>
            <dl className="stats">
              <div>
                <dt>Actieve accounts</dt>
                <dd>{d.activeUsers}</dd>
              </div>
              <div>
                <dt>Geblokkeerd</dt>
                <dd>{d.blockedUsers}</dd>
              </div>
              <div>
                <dt>Carnavalsjaar</dt>
                <dd>{d.carnivalYear ?? '—'}</dd>
              </div>
              <div>
                <dt>Dagen tot carnaval</dt>
                <dd>{d.daysUntilCarnival ?? '—'}</dd>
              </div>
            </dl>
          </section>
          <section className="card" aria-labelledby="systeem">
            <h2 id="systeem">Systeemstatus: {statusLabels[d.systemStatus] ?? d.systemStatus}</h2>
            <ul className="plain">
              {d.checks.map((check) => (
                <li key={check.name}>
                  <span className={`badge ${check.status === 'Healthy' ? 'ok' : 'warn'}`}>
                    {statusLabels[check.status] ?? check.status}
                  </span>{' '}
                  {check.name}
                </li>
              ))}
            </ul>
            <p className="muted">
              Laatste heartbeat: {formatDateTime(d.lastHeartbeat)} · Wachtende berichten: {d.outboxBacklog}
            </p>
          </section>
        </div>
      ) : dashboard.isLoading ? (
        <p>Laden…</p>
      ) : null}
    </>
  );
}
