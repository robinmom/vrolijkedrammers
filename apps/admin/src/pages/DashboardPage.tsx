import { Link } from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import {
  useAdminEvents,
  useAdminNews,
  useCurrentCarnivalYear,
  useDashboard,
  useMe,
  useMemberSummary,
  useSyncConflicts,
} from '../api/hooks';
import appIcon from '../assets/app-icoon.png';
import { ProblemAlert } from '../components/ProblemAlert';
import { formatDateTime, visibilityLabels } from '../format';

const statusLabels: Record<string, string> = { Healthy: 'Werkt', Degraded: 'Verminderd', Unhealthy: 'Storing' };
const checkLabels: Record<string, string> = { sql: 'Database', worker: 'Achtergrondtaken', keyvault: 'Key Vault', blob: 'Bestandsopslag' };

const zone = 'Europe/Amsterdam';
const hourFormat = new Intl.DateTimeFormat('nl-NL', { hour: 'numeric', hourCycle: 'h23', timeZone: zone });
const weekday = new Intl.DateTimeFormat('nl-NL', { weekday: 'short', timeZone: zone });
const day = new Intl.DateTimeFormat('nl-NL', { day: '2-digit', timeZone: zone });
const month = new Intl.DateTimeFormat('nl-NL', { month: 'short', timeZone: zone });
const time = new Intl.DateTimeFormat('nl-NL', { hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone: zone });

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

const clean = (value: string) => value.replace('.', '').toUpperCase();

interface Attention {
  key: string;
  tone: string;
  badge: string;
  text: string;
  link?: 'ledensync' | 'leden' | 'nieuws';
  action?: string;
}

/** Dashboard (Figma "Dashboard"): welkom met countdown, kengetallen, aandachtspunten, activiteiten en systeemstatus. */
export function DashboardPage() {
  const me = useMe();
  const permissions = me.data?.permissions ?? [];
  const can = (p: string) => permissions.includes(p);
  const now = useNow(60_000);
  const dashboard = useDashboard(true);
  const year = useCurrentCarnivalYear();
  const summary = useMemberSummary(can('member.read'));
  const conflicts = useSyncConflicts(can('import.run'));
  const events = useAdminEvents(false, can('event.manage'));
  const news = useAdminNews(can('news.manage'));
  const d = dashboard.data;

  const firstName = (me.data?.displayName ?? '').split(' ')[0];
  const start = year.data ? carnivalStart(year.data.carnivalStartDate) : null;
  const left = start ? Math.max(0, start.getTime() - now.getTime()) : 0;
  const countdown = {
    days: Math.floor(left / 86_400_000),
    hours: Math.floor((left % 86_400_000) / 3_600_000),
    minutes: Math.floor((left % 3_600_000) / 60_000),
  };

  const activeEvents = (events.data ?? []).filter((e) => e.status !== 'Archived');
  const upcoming = activeEvents.filter((e) => new Date(e.startAt) >= now).slice(0, 3);
  const publishedNews = (news.data ?? []).filter((n) => n.status === 'Published').length;
  const scheduledNews = (news.data ?? []).filter((n) => n.status === 'Scheduled');

  const attention: Attention[] = [];
  if (conflicts.data?.length) {
    const n = conflicts.data.length;
    attention.push({ key: 'conflicten', tone: 'warn', badge: `${n} conflict${n === 1 ? '' : 'en'}`, text: 'De ledensync heeft conflicten die beoordeeld moeten worden.', link: 'ledensync', action: 'Bekijken' });
  }
  if (summary.data?.missingInEBoekhouden) {
    const n = summary.data.missingInEBoekhouden;
    attention.push({ key: 'ontbrekend', tone: 'warn', badge: `${n} ${n === 1 ? 'lid' : 'leden'}`, text: 'Staan niet meer in e-Boekhouden en worden bij de volgende sync inactief.', link: 'leden', action: 'Bekijken' });
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
            <span className="kpi-hint">Volgens e-Boekhouden</span>
          </div>
        ) : null}
        {d ? (
          <div className="kpi">
            <span className="kpi-label">App-accounts</span>
            <span className="kpi-value">{d.activeUsers}</span>
            <span className="kpi-hint">{d.blockedUsers ? `${d.blockedUsers} geblokkeerd` : 'Actieve accounts'}</span>
          </div>
        ) : null}
        {events.data ? (
          <div className="kpi">
            <span className="kpi-label">Komende activiteiten</span>
            <span className="kpi-value">{activeEvents.length}</span>
            <span className="kpi-hint">{d?.carnivalYear ? `Seizoen ${d.carnivalYear.replace('/', '–')}` : 'Vanaf vandaag'}</span>
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

          {events.data ? (
            <section className="card" aria-labelledby="activiteiten">
              <div className="card-header">
                <h2 id="activiteiten">Eerstvolgende activiteiten</h2>
                <Link to="/agenda" className="button ghost small">
                  Hele agenda
                </Link>
              </div>
              {upcoming.length === 0 ? (
                <p className="muted">Er staan geen activiteiten gepland.</p>
              ) : (
                <ul className="list">
                  {upcoming.map((e) => {
                    const date = new Date(e.startAt);
                    return (
                      <li key={e.id} className="list-row">
                        <span className="date-block" aria-hidden="true">
                          {clean(weekday.format(date)).slice(0, 2)}
                          <strong>{day.format(date)}</strong>
                          {clean(month.format(date)).slice(0, 3)}
                        </span>
                        <div className="grow">
                          <Link to="/agenda/$id" params={{ id: e.id }} className="cell-title">
                            {e.title}
                          </Link>
                          <p className="muted small-text">
                            {weekday.format(date)} {day.format(date)} {month.format(date)} · {time.format(date)} uur
                          </p>
                        </div>
                        <span className={`badge ${e.visibility === 'Public' ? 'ok' : 'info'}`}>{visibilityLabels[e.visibility] ?? e.visibility}</span>
                      </li>
                    );
                  })}
                </ul>
              )}
            </section>
          ) : null}
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
