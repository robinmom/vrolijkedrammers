import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import {
  useAdminNews,
  useCurrentCarnivalYear,
  useDashboard,
  useGuardianRequests,
  useGuardianSuggestions,
  useMe,
  useMemberSummary,
  useSyncConflicts,
  type Schemas,
} from '../api/hooks';
import { useApi } from '../api/ApiContext';
import { season, useAdvertiserStatus } from '../api/advertisers';
import appIcon from '../assets/logo-cirkel.png';
import { AccessDashboardCard } from '../components/AccessDashboardCard';
import { useSharedEmailConflicts } from '../components/SharedEmailCard';
import { ProblemAlert } from '../components/ProblemAlert';
import { formatDateTime } from '../format';

const statusLabels: Record<string, string> = { Healthy: 'Werkt', Degraded: 'Verminderd', Unhealthy: 'Storing' };
const checkLabels: Record<string, string> = { sql: 'Database', worker: 'Achtergrondtaken', keyvault: 'Key Vault', blob: 'Bestandsopslag' };

const zone = 'Europe/Amsterdam';
const hourFormat = new Intl.DateTimeFormat('nl-NL', { hour: 'numeric', hourCycle: 'h23', timeZone: zone });

function greeting(now: Date): string {
  const h = Number(hourFormat.format(now));
  return h < 12 ? 'Goedemorgen' : h < 18 ? 'Goedemiddag' : 'Goedenavond';
}

/** Middernacht in Loil op de eerste carnavalsdag (carnaval valt altijd in de wintertijd: UTC+1). */
function carnivalStart(date: string): Date {
  const [y, m, d] = date.split('-').map(Number);
  return new Date(Date.UTC(y ?? 1970, (m ?? 1) - 1, d ?? 1) - 60 * 60 * 1000);
}

function useNow(intervalMs: number) {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), intervalMs);
    return () => clearInterval(timer);
  }, [intervalMs]);
  return now;
}

interface Attention {
  key: string;
  tone: string;
  badge: string;
  text: string;
  link?: 'ledensync' | 'leden' | 'nieuws' | 'koppelverzoeken' | 'accountverzoeken';
  action?: string;
}

/**
 * Dashboard (Figma "Dashboard"): welkom met countdown, kengetallen, openstaande aanmeldingen (2026-10-15, in plaats van de
 * activiteiten), aandachtspunten en systeemstatus.
 */
export function DashboardPage() {
  const me = useMe();
  const permissions = me.data?.permissions ?? [];
  const can = (p: string) => permissions.includes(p);
  const now = useNow(60_000);
  const dashboard = useDashboard(true);
  const year = useCurrentCarnivalYear();
  const summary = useMemberSummary(can('member.read'));
  const conflicts = useSyncConflicts(can('import.run'));
  const pending = usePendingApplications();
  const news = useAdminNews(can('news.manage'));
  const guardianRequests = useGuardianRequests('Pending', can('member.update'));
  const guardianSuggestions = useGuardianSuggestions(can('member.update'));
  const advertisers = useAdvertiserStatus(null, '', can('advertiser.manage'));
  const sharedEmail = useSharedEmailConflicts(can('member.approve'));
  const d = dashboard.data;

  const firstName = (me.data?.displayName ?? '').split(' ')[0];
  const start = year.data ? carnivalStart(year.data.carnivalStartDate) : null;
  const left = start ? Math.max(0, start.getTime() - now.getTime()) : 0;
  const countdown = {
    days: Math.floor(left / 86_400_000),
    hours: Math.floor((left % 86_400_000) / 3_600_000),
    minutes: Math.floor((left % 3_600_000) / 60_000),
  };

  const publishedNews = (news.data ?? []).filter((n) => n.status === 'Published').length;
  const scheduledNews = (news.data ?? []).filter((n) => n.status === 'Scheduled');

  // Zonder e-Boekhouden-koppeling wordt de ledenadministratie hier bijgehouden; de sync-aandachtspunten vervallen dan.
  const eBoekhouden = me.data?.eBoekhoudenEnabled !== false;
  const attention: Attention[] = [];
  if (eBoekhouden && conflicts.data?.length) {
    const n = conflicts.data.length;
    attention.push({ key: 'conflicten', tone: 'warn', badge: `${n} conflict${n === 1 ? '' : 'en'}`, text: 'De ledensync heeft conflicten die beoordeeld moeten worden.', link: 'ledensync', action: 'Bekijken' });
  }
  if (eBoekhouden && summary.data?.missingInEBoekhouden) {
    const n = summary.data.missingInEBoekhouden;
    attention.push({ key: 'ontbrekend', tone: 'warn', badge: `${n} ${n === 1 ? 'lid' : 'leden'}`, text: 'Staan niet meer in e-Boekhouden en worden bij de volgende sync inactief.', link: 'leden', action: 'Bekijken' });
  }
  const linkCount = (guardianRequests.data?.length ?? 0) + (guardianSuggestions.data?.length ?? 0);
  if (linkCount) {
    const r = guardianRequests.data?.length ?? 0;
    const v = guardianSuggestions.data?.length ?? 0;
    attention.push({
      key: 'koppelverzoeken',
      tone: 'info',
      badge: `${linkCount} ouder-kind`,
      text: [r ? `${r} koppelverzoek${r === 1 ? '' : 'en'} uit de app` : null, v ? `${v} voorstel${v === 1 ? '' : 'len'} (zelfde e-mail)` : null]
        .filter(Boolean)
        .join(' en ') + ' wachten op beoordeling.',
      link: 'koppelverzoeken',
      action: 'Bekijken',
    });
  }
  if (sharedEmail.data?.length) {
    const n = sharedEmail.data.length;
    attention.push({
      key: 'gedeeld-adres',
      tone: 'warn',
      badge: `${n} e-mailadres${n === 1 ? '' : 'sen'}`,
      text: 'Meerdere accounts met hetzelfde e-mailadres: die kunnen niet allebei inloggen.',
      link: 'accountverzoeken',
      action: 'Bekijken',
    });
  }
  for (const item of scheduledNews.slice(0, 2)) {
    attention.push({ key: item.id, tone: 'info', badge: 'Gepland', text: `Nieuwsbericht "${item.title}" verschijnt op ${formatDateTime(item.publishAt)}.`, link: 'nieuws', action: 'Openen' });
  }
  if (d && d.systemStatus !== 'Healthy') {
    attention.push({ key: 'systeem', tone: 'error', badge: 'Systeem', text: 'Niet alle onderdelen werken normaal; zie de systeemstatus.' });
  }

  return (
    <>
      <section className="hero" aria-labelledby="welkom">
        <img src={appIcon} alt="" className="hero-icon" width={72} height={72} />
        <div className="hero-text">
          <h1 id="welkom">
            {greeting(now)}
            {firstName ? `, ${firstName}` : ''}
          </h1>
          <p>Alaaf! Hier zie je in één oogopslag wat er speelt bij De Vrolijke Drammers.</p>
        </div>
        {start && left > 0 ? (
          <div className="countdown" role="timer" aria-label={`Nog ${countdown.days} dagen tot carnaval ${start.getUTCFullYear()}`}>
            <span>Nog tot carnaval {start.getUTCFullYear()}</span>
            <div className="countdown-tiles" aria-hidden="true">
              <div className="countdown-tile">
                <strong>{countdown.days}</strong>
                <span>dagen</span>
              </div>
              <div className="countdown-tile">
                <strong>{countdown.hours}</strong>
                <span>uur</span>
              </div>
              <div className="countdown-tile">
                <strong>{countdown.minutes}</strong>
                <span>min</span>
              </div>
            </div>
          </div>
        ) : null}
      </section>

      <ProblemAlert error={dashboard.error} />

      <section className="kpis" aria-label="Kengetallen">
        {summary.data ? (
          <div className="kpi">
            <span className="kpi-label">Actieve leden</span>
            <span className="kpi-value">{summary.data.active}</span>
            <span className="kpi-hint">{eBoekhouden ? 'Volgens e-Boekhouden' : 'In de ledenadministratie'}</span>
          </div>
        ) : null}
        {d ? (
          <div className="kpi">
            <span className="kpi-label">App-accounts</span>
            <span className="kpi-value">{d.activeUsers}</span>
            <span className="kpi-hint">{d.blockedUsers ? `${d.blockedUsers} geblokkeerd` : 'Actieve accounts'}</span>
          </div>
        ) : null}
        {news.data ? (
          <div className="kpi">
            <span className="kpi-label">Gepubliceerd nieuws</span>
            <span className="kpi-value">{publishedNews}</span>
            <span className="kpi-hint">{scheduledNews.length} gepland</span>
          </div>
        ) : null}
      </section>

      <div className="columns">
        <div>
          {pending.data ? <PendingApplicationsCard pending={pending.data} /> : null}

          {can('task.manage') ? <TasksDashboardCard /> : null}

          <section className="card" aria-labelledby="aandacht">
            <h2 id="aandacht">Aandacht nodig</h2>
            {attention.length === 0 ? (
              <p className="muted">Alles is bijgewerkt.</p>
            ) : (
              <ul className="list">
                {attention.map((a) => (
                  <li key={a.key} className="list-row">
                    <span className={`badge ${a.tone}`}>{a.badge}</span>
                    <p className="grow">{a.text}</p>
                    {a.link === 'ledensync' ? (
                      <Link to="/ledensync" className="button ghost small">
                        {a.action}
                      </Link>
                    ) : a.link === 'leden' ? (
                      <Link to="/leden" className="button ghost small">
                        {a.action}
                      </Link>
                    ) : a.link === 'koppelverzoeken' ? (
                      <Link to="/koppelverzoeken" className="button ghost small">
                        {a.action}
                      </Link>
                    ) : a.link === 'accountverzoeken' ? (
                      <Link to="/accountverzoeken" className="button ghost small">
                        {a.action}
                      </Link>
                    ) : a.link === 'nieuws' ? (
                      <Link to="/nieuws" className="button ghost small">
                        {a.action}
                      </Link>
                    ) : null}
                  </li>
                ))}
              </ul>
            )}
          </section>

          {advertisers.data ? <AdvertiserProgressCard report={advertisers.data} /> : null}

          {can('ticket.read') ? <AccessDashboardCard /> : null}
        </div>

        <div>
          {can('news.manage') || can('event.manage') || can('photo.manage') || can('import.run') ? (
            <section className="card quick-links" aria-labelledby="snel">
              <h2 id="snel">Snel naar</h2>
              {can('news.manage') ? (
                <Link to="/nieuws/$id" params={{ id: 'nieuw' }} className="button block">
                  Nieuws plaatsen
                </Link>
              ) : null}
              {can('event.manage') ? (
                <Link to="/agenda/$id" params={{ id: 'nieuw' }} className="button block secondary">
                  Activiteit toevoegen
                </Link>
              ) : null}
              {can('photo.manage') ? (
                <Link to="/fotos" className="button block secondary">
                  Foto&apos;s uploaden
                </Link>
              ) : null}
              {can('import.run') ? (
                <Link to="/ledensync" className="button block secondary">
                  Leden synchroniseren
                </Link>
              ) : null}
            </section>
          ) : null}

          {d ? (
            <section className="card" aria-labelledby="systeem">
              <h2 id="systeem">Systeemstatus</h2>
              <ul className="list">
                {d.checks.map((check) => (
                  <li key={check.name} className="list-row">
                    <span className="grow">{checkLabels[check.name] ?? check.name}</span>
                    <span className={`badge ${check.status === 'Healthy' ? 'ok' : check.status === 'Degraded' ? 'warn' : 'error'}`}>
                      {statusLabels[check.status] ?? check.status}
                    </span>
                  </li>
                ))}
              </ul>
              <p className="muted small-text">
                Laatste heartbeat {formatDateTime(d.lastHeartbeat)} · {d.outboxBacklog} wachtende berichten
              </p>
            </section>
          ) : null}
        </div>
      </div>
    </>
  );
}

/** Voortgang van de adverteerderscampagne (fase 27i): afgehandeld = opgehaald of stopt, zoals op de campagnepagina. */
function AdvertiserProgressCard({ report }: { report: NonNullable<ReturnType<typeof useAdvertiserStatus>['data']> }) {
  const { total, collected, stopped } = report.totals;
  const percent = total > 0 ? Math.round((100 * (collected + stopped)) / total) : 0;
  return (
    <section className="card" aria-labelledby="adverteerders-voortgang">
      <h2 id="adverteerders-voortgang">
        Adverteerders {season(report.year)}: {percent}%
      </h2>
      <div
        className="progress"
        role="progressbar"
        aria-label="Adverteerders afgehandeld"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={percent}
      >
        <span style={{ width: `${percent}%` }} />
      </div>
    </section>
  );
}

type Pending = Schemas['PendingResponse'];

function usePendingApplications() {
  const api = useApi();
  return useQuery({
    queryKey: ['dashboard', 'pending'],
    queryFn: async () => (await api.GET('/api/v1/admin/dashboard/pending')).data ?? null,
    refetchInterval: 60_000,
  });
}

/** Openstaande aanmeldingen per onderdeel, met een knop naar de pagina waar ze beoordeeld worden. */
function PendingApplicationsCard({ pending }: { pending: Pending }) {
  const rows = [
    { key: 'leden', label: 'Leden', counts: pending.members, to: '/aanmeldingen' as const },
    { key: 'optocht', label: 'Optocht', counts: pending.parade, to: '/optocht/inschrijvingen' as const },
    { key: 'festival', label: 'Dansgardefestival', counts: pending.festival, to: '/dansgarde/festival' as const },
  ].filter((r) => r.counts);
  if (rows.length === 0) return null;
  return (
    <section className="card" aria-labelledby="aanmeldingen">
      <h2 id="aanmeldingen">Openstaande aanmeldingen</h2>
      <ul className="list">
        {rows.map((r) => {
          const c = r.counts!;
          const open = c.new + c.inReview + c.supplementReceived;
          return (
            <li key={r.key} className="list-row">
              <div className="grow">
                <strong>{r.label}</strong>
                <p className="muted small-text">
                  {open === 0
                    ? 'Niets openstaand'
                    : [
                        c.new ? `${c.new} nieuw` : null,
                        c.supplementReceived ? `${c.supplementReceived} aanvulling ontvangen` : null,
                        c.inReview ? `${c.inReview} in behandeling` : null,
                      ]
                        .filter(Boolean)
                        .join(' · ')}
                </p>
              </div>
              <span className={`badge ${open ? 'warn' : 'ok'}`}>{open}</span>
              <Link to={r.to} className="button ghost small">
                Bekijken →
              </Link>
            </li>
          );
        })}
      </ul>
    </section>
  );
}

/** Voorbereiding carnaval (2026-10-15): de totale voortgang van de takenlijst van het lopende jaar. */
function TasksDashboardCard() {
  const api = useApi();
  const tasks = useQuery({
    queryKey: ['tasks', 'year', null],
    queryFn: async () => (await api.GET('/api/v1/admin/tasks')).data ?? null,
  });
  const y = tasks.data;
  if (!y) return null;
  const s = y.summary;
  return (
    <section className="card" aria-labelledby="voorbereiding">
      <div className="card-header">
        <h2 id="voorbereiding">Voorbereiding carnaval</h2>
        <Link to="/taken" className="button ghost small">
          Takenlijst →
        </Link>
      </div>
      <p className="kpi-value">{s.percent}%</p>
      <div
        className="progress big"
        role="progressbar"
        aria-label="Voortgang voorbereiding carnaval"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={s.percent}
      >
        <span style={{ width: `${s.percent}%` }} />
      </div>
      <p className="muted small-text">
        {s.done} van {s.total - s.cancelled} taken afgerond · {s.inProgress} bezig · {s.overdue} over datum · nog{' '}
        {y.daysUntilCarnival} dagen tot carnaval
      </p>
    </section>
  );
}
