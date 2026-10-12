import { useQuery } from '@tanstack/react-query';
import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, type Schemas } from '../api/hooks';
import { formatEuro } from '../api/sales';
import { upload } from '../api/upload';
import { useAuth } from '../auth/AuthContext';
import { Dialog } from '../components/Dialog';
import { Checkbox, Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDate, formatDateTime } from '../format';

type Settings = Schemas['FestivalSettingsView'];
type Summary = Schemas['FestivalRegistrationSummary'];
type Detail = Schemas['FestivalRegistrationDetail'];
type Entry = Schemas['FestivalEntryView'];
type Decision = Schemas['FestivalDecision'];

const KEYS = {
  list: ['dance-festival'],
  settings: ['dance-festival', 'settings'],
  detail: (id: string) => ['dance-festival', id],
};

const typeLabels: Record<string, string> = { Garde: 'Gardedans', Show: 'Showdans' };
const formatLabels: Record<string, string> = { Solo: 'Solo', Duo: 'Duo', Group: 'Groep' };
const styleLabels: Record<string, string> = { Character: 'Karakter', Modern: 'Modern' };
const entryStatus: Record<string, [string, string]> = {
  New: ['Nieuw', 'info'],
  InfoRequested: ['Meer info gevraagd', 'warn'],
  Approved: ['Goedgekeurd', 'ok'],
  Waitlisted: ['Wachtlijst', 'info'],
  Rejected: ['Afgewezen', 'neutral'],
  Changed: ['Gewijzigd – opnieuw beoordelen', 'error'],
};
const summaryTone: Record<string, string> = {
  Nieuw: 'info',
  Gewijzigd: 'error',
  'Meer info gevraagd': 'warn',
  Goedgekeurd: 'ok',
  'Deels wachtlijst': 'info',
  Wachtlijst: 'info',
  Afgewezen: 'neutral',
};
const stateLabels: Record<string, [string, string]> = {
  Hidden: ['Pagina uit', 'neutral'],
  NotYetOpen: ['Pagina online · inschrijving nog niet open', 'warn'],
  Open: ['Pagina online · inschrijving open', 'ok'],
  Closed: ['Pagina online · inschrijving gesloten', 'neutral'],
};
const decisionLabels: Record<Decision, string> = {
  Approve: 'Goedkeuren',
  RequestInfo: 'Meer info vragen',
  Waitlist: 'Wachtlijst',
  Reject: 'Afwijzen',
};

/**
 * Dansgardefestival (2026-10-12): inschrijvingen beoordelen per dans (goedkeuren vult een plek; wachtlijst met schuiven),
 * betaallinks na goedkeuring en de instellingen (pagina aan/uit, inschrijfperiode, plekken, tarieven, documenten).
 * Jureren en het programma volgen later.
 */
export function DanceFestivalPage() {
  const api = useApi();
  const [tab, setTab] = useState<'registrations' | 'settings'>('registrations');
  const list = useQuery({
    queryKey: KEYS.list,
    queryFn: async () => (await api.GET('/api/v1/admin/dance-festival/registrations')).data ?? null,
  });
  const festival = list.data?.festival ?? null;
  const [stateText, stateTone] = stateLabels[festival?.state ?? 'Hidden'] ?? ['Pagina uit', 'neutral'];

  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>{festival?.name ?? 'Dansgardefestival'}</h1>
          {festival ? (
            <p className="page-subtitle">
              {formatDate(festival.festivalDate)} · {festival.location}
              {festival.registrationClosesAt
                ? ` · inschrijven t/m ${formatDate(lastDay(festival.registrationClosesAt))}`
                : ''}
            </p>
          ) : null}
        </div>
        {festival ? <span className={`badge ${stateTone}`}>{stateText}</span> : null}
      </div>
      <div className="tabs" role="tablist" aria-label="Dansgardefestival">
        <button
          type="button"
          role="tab"
          className="tab"
          aria-selected={tab === 'registrations'}
          onClick={() => setTab('registrations')}
        >
          Inschrijvingen
        </button>
        <button
          type="button"
          role="tab"
          className="tab"
          aria-selected={tab === 'settings'}
          onClick={() => setTab('settings')}
        >
          Instellingen
        </button>
        <button type="button" role="tab" className="tab" aria-selected={false} disabled title="Volgt later">
          Jurering (later)
        </button>
        <button type="button" role="tab" className="tab" aria-selected={false} disabled title="Volgt later">
          Programma (later)
        </button>
      </div>
      <ProblemAlert error={list.error} />
      {tab === 'settings' || (list.isSuccess && !festival) ? (
        <SettingsTab />
      ) : list.data ? (
        <RegistrationsTab data={list.data} />
      ) : (
        <p>Laden…</p>
      )}
    </>
  );
}

/** De sluitdatum is exclusief (00:00 de dag erna); in het portal tonen we de laatste dag. */
function lastDay(iso: string): string {
  const d = new Date(new Date(iso).getTime() - 60_000);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

function RegistrationsTab({ data }: { data: Schemas['FestivalAdminList'] }) {
  const api = useApi();
  const [filter, setFilter] = useState<'all' | 'review' | 'approved' | 'waitlist'>('review');
  const [selected, setSelected] = useState<string | null>(null);
  const stats = data.stats;
  const rows = data.registrations.filter((r) =>
    filter === 'review'
      ? r.toReview > 0 || r.status === 'Meer info gevraagd'
      : filter === 'approved'
        ? r.approved > 0
        : filter === 'waitlist'
          ? r.waitlisted > 0
          : true,
  );
  const count = (f: typeof filter) =>
    data.registrations.filter((r) =>
      f === 'review'
        ? r.toReview > 0 || r.status === 'Meer info gevraagd'
        : f === 'approved'
          ? r.approved > 0
          : f === 'waitlist'
            ? r.waitlisted > 0
            : true,
    ).length;

  async function exportCsv() {
    const response = await api.GET('/api/v1/admin/dance-festival/export', { parseAs: 'blob' });
    if (response.data) {
      const url = URL.createObjectURL(response.data as Blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = 'dansgardefestival-inschrijvingen.csv';
      a.click();
      URL.revokeObjectURL(url);
    }
  }

  return (
    <>
      <div className="kpis">
        <div className="kpi">
          <span className="kpi-label">Plekken bezet</span>
          <span className="kpi-value">
            {stats.placesTaken} / {stats.capacity}
          </span>
          <span className="kpi-hint">goedgekeurde dansen</span>
        </div>
        <div className="kpi">
          <span className="kpi-label">Te beoordelen</span>
          <span className="kpi-value">{stats.toReview}</span>
          <span className="kpi-hint">nieuwe of gewijzigde dansen</span>
        </div>
        <div className="kpi">
          <span className="kpi-label">Wachtlijst</span>
          <span className="kpi-value">{stats.waitlisted}</span>
          <span className="kpi-hint">dansen</span>
        </div>
        <div className="kpi">
          <span className="kpi-label">Betaald</span>
          <span className="kpi-value">{formatEuro(stats.paidCents)}</span>
          <span className="kpi-hint">van {formatEuro(stats.requestedCents)} aan betaallinks</span>
        </div>
      </div>
      <div className="festival-split">
        <section className="card table-wrapper" aria-label="Inschrijvingen">
          <div className="table-toolbar">
            <div className="chips" role="group" aria-label="Filter">
              {(
                [
                  ['all', 'Alle'],
                  ['review', 'Te beoordelen'],
                  ['approved', 'Goedgekeurd'],
                  ['waitlist', 'Wachtlijst'],
                ] as const
              ).map(([key, label]) => (
                <button
                  key={key}
                  type="button"
                  className={`chip${filter === key ? ' active' : ''}`}
                  aria-pressed={filter === key}
                  onClick={() => setFilter(key)}
                >
                  {label} ({count(key)})
                </button>
              ))}
            </div>
            <button type="button" className="button ghost small" onClick={() => void exportCsv()}>
              Exporteren ↓
            </button>
          </div>
          <div className="table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th>Nr.</th>
                  <th>Vereniging</th>
                  <th>Dansen</th>
                  <th>Status</th>
                  <th>Betaling</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r: Summary) => (
                  <tr key={r.id} className={selected === r.id ? 'selected' : undefined}>
                    <td>{r.number}</td>
                    <td>
                      <button type="button" className="link-button" onClick={() => setSelected(r.id)}>
                        {r.clubName}
                      </button>
                    </td>
                    <td>{r.dances}</td>
                    <td>
                      <span className={`badge ${summaryTone[r.status] ?? 'neutral'}`}>
                        {r.status}
                        {r.firstWaitlistPosition ? ` (${r.firstWaitlistPosition})` : ''}
                      </span>
                    </td>
                    <td>
                      <span
                        className={`badge ${r.paymentStatus === 'Betaald' ? 'ok' : r.paymentStatus === 'Link verstuurd' ? 'warn' : 'neutral'}`}
                      >
                        {r.paymentStatus}
                      </span>
                    </td>
                  </tr>
                ))}
                {rows.length === 0 ? (
                  <tr>
                    <td colSpan={5} className="muted">
                      Geen inschrijvingen.
                    </td>
                  </tr>
                ) : null}
              </tbody>
            </table>
          </div>
        </section>
        {selected ? (
          <RegistrationDetail id={selected} paymentsEnabled={data.festival?.paymentsEnabled ?? false} />
        ) : (
          <section className="card muted">Kies een inschrijving om te beoordelen.</section>
        )}
      </div>
    </>
  );
}

function RegistrationDetail({ id, paymentsEnabled }: { id: string; paymentsEnabled: boolean }) {
  const api = useApi();
  const detail = useQuery({
    queryKey: KEYS.detail(id),
    queryFn: async () =>
      (await api.GET('/api/v1/admin/dance-festival/registrations/{id}', { params: { path: { id } } })).data ?? null,
  });
  const [checked, setChecked] = useState<string[]>([]);
  const [decision, setDecision] = useState<Decision | null>(null);
  const [message, setMessage] = useState('');
  const [notice, setNotice] = useState<string | null>(null);
  const [amount, setAmount] = useState('');
  const [version, setVersion] = useState<{ number: number; json: string } | null>(null);
  const keys = [KEYS.list, KEYS.detail(id)];

  useEffect(() => {
    setChecked([]);
    setNotice(null);
  }, [id]);
  useEffect(() => {
    if (detail.data)
      setAmount(
        detail.data.suggestedAmountCents ? (detail.data.suggestedAmountCents / 100).toFixed(2).replace('.', ',') : '',
      );
  }, [detail.data]);

  const decide = useApiMutation(
    () =>
      api.POST('/api/v1/admin/dance-festival/registrations/{id}/decisions', {
        params: { path: { id } },
        body: { entryIds: checked, decision: decision!, message: message.trim() || null },
      }),
    keys,
  );
  const move = useApiMutation(
    (args: { entryId: string; direction: number }) =>
      api.POST('/api/v1/admin/dance-festival/entries/{id}/waitlist-move', {
        params: { path: { id: args.entryId } },
        body: { direction: args.direction },
      }),
    keys,
  );
  const requestPayment = useApiMutation(
    (cents: number) =>
      api.POST('/api/v1/admin/dance-festival/registrations/{id}/payments', {
        params: { path: { id } },
        body: { amountCents: cents },
      }),
    keys,
  );
  const refresh = useApiMutation(
    (paymentId: string) =>
      api.POST('/api/v1/admin/dance-festival/payments/{id}/refresh', { params: { path: { id: paymentId } } }),
    keys,
  );
  const cancel = useApiMutation(
    (paymentId: string) =>
      api.POST('/api/v1/admin/dance-festival/payments/{id}/cancel', { params: { path: { id: paymentId } } }),
    keys,
  );

  const d: Detail | null | undefined = detail.data;
  if (!d) return <section className="card">{detail.error ? <ProblemAlert error={detail.error} /> : 'Laden…'}</section>;
  const active = d.entries.filter((e) => !e.removedAt);
  const removed = d.entries.filter((e) => e.removedAt);
  const toggle = (entryId: string) =>
    setChecked((c) => (c.includes(entryId) ? c.filter((x) => x !== entryId) : [...c, entryId]));

  async function showVersion(number: number) {
    const response = await api.GET('/api/v1/admin/dance-festival/registrations/{id}/versions/{version}', {
      params: { path: { id, version: number } },
      parseAs: 'text',
    });
    setVersion({ number, json: response.data ? JSON.stringify(JSON.parse(response.data as string), null, 2) : '' });
  }

  function confirmDecision(event: FormEvent) {
    event.preventDefault();
    decide.mutate(undefined, {
      onSuccess: () => {
        setNotice(`${decisionLabels[decision!]}: ${checked.length} dans(en). De vereniging krijgt een e-mail.`);
        setChecked([]);
        setDecision(null);
        setMessage('');
      },
    });
  }

  const cents = Math.round(Number(amount.replace(',', '.')) * 100);
  const approved = active.some((e) => e.status === 'Approved');

  return (
    <section className="card festival-detail" aria-label={`Inschrijving ${d.number}`}>
      <div className="detail-head">
        <h2>
          #{d.number} {d.clubName}
        </h2>
      </div>
      <p className="muted">
        {d.contactName} · {d.phone} · <a href={`mailto:${d.email}`}>{d.email}</a>
        <br />
        {d.addressLine}, {d.postalCode} {d.city} · ingediend {formatDateTime(d.submittedAt)}
        {d.changedAt ? `, gewijzigd ${formatDateTime(d.changedAt)}` : ''}
      </p>
      <SuccessMessage message={notice} />
      {d.infoRequest ? (
        <p className="note warn">
          <strong>Gevraagd:</strong> {d.infoRequest}
        </p>
      ) : null}
      {d.versions.length > 0 ? (
        <p className="note error">
          Gewijzigd door de vereniging (versie {d.version}). Eerdere versies:{' '}
          {d.versions.map((v) => (
            <button key={v.version} type="button" className="link-button" onClick={() => void showVersion(v.version)}>
              versie {v.version} ({formatDateTime(v.createdAt)})
            </button>
          ))}
        </p>
      ) : null}

      <h3>Dansen</h3>
      <ul className="dance-list">
        {active.map((e: Entry) => {
          const [label, tone] = entryStatus[e.status] ?? [e.status, 'neutral'];
          return (
            <li key={e.id}>
              <input
                type="checkbox"
                aria-label={`Selecteer ${e.names}`}
                checked={checked.includes(e.id)}
                onChange={() => toggle(e.id)}
              />
              <div className="grow">
                <strong>
                  {typeLabels[e.danceType]} · {formatLabels[e.format]} · {e.category}
                  {e.showStyle ? ` · ${styleLabels[e.showStyle]}` : ''}
                </strong>
                <div className="muted">
                  {e.title ? `“${e.title}” · ` : ''}
                  {e.names} · {e.dancerCount} danser{e.dancerCount === 1 ? '' : 's'} · gem.{' '}
                  {String(e.averageAge).replace('.', ',')} jr · {e.leaderName}
                </div>
              </div>
              <span className={`badge ${tone}`}>
                {label}
                {e.waitlistPosition ? ` ${e.waitlistPosition}` : ''}
                {e.status === 'Changed' && e.holdsPlace ? ' (houdt plek)' : ''}
              </span>
              {e.status === 'Waitlisted' ? (
                <span className="move">
                  <button
                    type="button"
                    className="button ghost small"
                    aria-label="Omhoog op de wachtlijst"
                    onClick={() => move.mutate({ entryId: e.id, direction: -1 })}
                  >
                    ↑
                  </button>
                  <button
                    type="button"
                    className="button ghost small"
                    aria-label="Omlaag op de wachtlijst"
                    onClick={() => move.mutate({ entryId: e.id, direction: 1 })}
                  >
                    ↓
                  </button>
                </span>
              ) : null}
            </li>
          );
        })}
      </ul>
      {removed.length ? (
        <p className="muted small">Verwijderd door de vereniging: {removed.map((e) => e.names).join(', ')}</p>
      ) : null}
      <div className="consents">
        <span className={`badge ${d.photoConsent ? 'ok' : 'warn'}`}>
          Foto/video: {d.photoConsent ? 'toestemming' : 'geen toestemming'}
        </span>
        <span className="badge ok">Reglement akkoord</span>
        <span className="badge ok">Privacyverklaring gelezen</span>
      </div>
      {d.remarks ? <p className="muted">Opmerking vereniging: “{d.remarks}”</p> : null}

      <div className="actions">
        <button
          type="button"
          className="button small"
          disabled={!checked.length}
          onClick={() => setDecision('Approve')}
        >
          Goedkeuren
        </button>
        <button
          type="button"
          className="button secondary small"
          disabled={!checked.length}
          onClick={() => setDecision('RequestInfo')}
        >
          Meer info vragen
        </button>
        <button
          type="button"
          className="button secondary small"
          disabled={!checked.length}
          onClick={() => setDecision('Waitlist')}
        >
          Wachtlijst
        </button>
        <button
          type="button"
          className="button danger small"
          disabled={!checked.length}
          onClick={() => setDecision('Reject')}
        >
          Afwijzen
        </button>
        <button type="button" className="link-button" onClick={() => setChecked(active.map((e) => e.id))}>
          Alle dansen
        </button>
      </div>
      {!checked.length ? <p className="muted small">Vink de dansen aan waarover je beslist.</p> : null}
      <ProblemAlert error={move.error} />

      <div className="payment-box">
        <h3>Betaling (iDEAL)</h3>
        {d.payments.map((p) => (
          <div key={p.id} className="payment-row">
            <span>
              {formatEuro(p.amountCents)} · {formatDateTime(p.createdAt)}
            </span>
            <span className={`badge ${p.status === 'Paid' ? 'ok' : p.status === 'Open' ? 'warn' : 'neutral'}`}>
              {p.status === 'Paid'
                ? `Betaald ${formatDateTime(p.paidAt)}`
                : p.status === 'Open'
                  ? `Open${p.mollieStatus ? ` (${p.mollieStatus})` : ''}`
                  : 'Ingetrokken'}
            </span>
            {p.status === 'Open' ? (
              <>
                <button type="button" className="link-button" onClick={() => refresh.mutate(p.id)}>
                  Status vernieuwen
                </button>
                <button type="button" className="link-button danger" onClick={() => cancel.mutate(p.id)}>
                  Intrekken
                </button>
              </>
            ) : null}
          </div>
        ))}
        {!paymentsEnabled ? (
          <p className="muted small">Betalen via iDEAL staat uit (Instellingen).</p>
        ) : !approved ? (
          <p className="muted small">Een betaallink kan na goedkeuring van minstens één dans.</p>
        ) : (
          <form
            className="payment-form"
            onSubmit={(event) => {
              event.preventDefault();
              requestPayment.mutate(cents, {
                onSuccess: () => setNotice(`Betaallink van ${formatEuro(cents)} verstuurd naar ${d.email}.`),
              });
            }}
          >
            <Field
              label="Bedrag (€)"
              inputMode="decimal"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
              hint="Voorstel uit de tarieven; aan te passen."
            />
            <button type="submit" className="button small" disabled={!(cents > 0) || requestPayment.isPending}>
              Betaallink versturen
            </button>
          </form>
        )}
        <ProblemAlert error={requestPayment.error ?? refresh.error ?? cancel.error} />
      </div>

      <Dialog
        open={decision !== null}
        title={
          decision ? `${decisionLabels[decision]} (${checked.length} dans${checked.length === 1 ? '' : 'en'})` : ''
        }
        onClose={() => setDecision(null)}
      >
        <form onSubmit={confirmDecision}>
          <p>
            De vereniging krijgt hierover een e-mail
            {decision === 'RequestInfo' ? ' met een link om de inschrijving aan te passen' : ''}.
          </p>
          <div className="field">
            <label htmlFor="festival-message">
              {decision === 'RequestInfo'
                ? 'Welke informatie heb je nodig?'
                : 'Toelichting in de mail (niet verplicht)'}
            </label>
            <textarea
              id="festival-message"
              rows={4}
              maxLength={2000}
              required={decision === 'RequestInfo'}
              value={message}
              onChange={(e) => setMessage(e.target.value)}
            />
          </div>
          <ProblemAlert error={decide.error} />
          <div className="actions">
            <button type="button" className="button secondary" onClick={() => setDecision(null)}>
              Annuleren
            </button>
            <button
              type="submit"
              className={`button${decision === 'Reject' ? ' danger' : ''}`}
              disabled={decide.isPending}
            >
              {decision ? decisionLabels[decision] : ''}
            </button>
          </div>
        </form>
      </Dialog>
      <Dialog open={version !== null} title={`Versie ${version?.number ?? ''}`} onClose={() => setVersion(null)}>
        <pre className="version-json">{version?.json}</pre>
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => setVersion(null)}>
            Sluiten
          </button>
        </div>
      </Dialog>
    </section>
  );
}

const toDateInput = (iso: string | null | undefined) => (iso ? lastDayOrSame(iso) : '');
function lastDayOrSame(iso: string): string {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}
/** Begin van de dag (lokale tijd) als ISO. */
const startOfDay = (date: string) => (date ? new Date(`${date}T00:00`).toISOString() : null);
/** "t/m" een datum: tot 00:00 de dag erna. */
const endOfDay = (date: string) => {
  if (!date) return null;
  const d = new Date(`${date}T00:00`);
  d.setDate(d.getDate() + 1);
  return d.toISOString();
};
const toEuroInput = (cents: number) => (cents / 100).toFixed(2).replace('.', ',');
const fromEuroInput = (value: string) => Math.round(Number(value.replace(',', '.')) * 100);

function SettingsTab() {
  const api = useApi();
  const auth = useAuth();
  const settings = useQuery({
    queryKey: KEYS.settings,
    queryFn: async () =>
      ((await api.GET('/api/v1/admin/dance-festival/settings')).data as Settings | undefined) ?? null,
  });
  const [form, setForm] = useState<Record<string, string | boolean> | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [uploadError, setUploadError] = useState<unknown>(null);

  useEffect(() => {
    if (settings.isSuccess && form === null) {
      const s = settings.data;
      setForm({
        name: s?.name ?? 'Dansgardefestival',
        festivalDate: s?.festivalDate ?? '',
        location: s?.location ?? '’t Hart van Loil, Wehlseweg 36, Loil',
        pageEnabled: s?.pageEnabled ?? false,
        opensOn: toDateInput(s?.registrationOpensAt),
        closesOn: s?.registrationClosesAt ? lastDay(s.registrationClosesAt) : '',
        capacity: String(s?.capacity ?? 40),
        paymentsEnabled: s?.paymentsEnabled ?? false,
        priceSoloDuo: toEuroInput(s?.priceSoloDuoCents ?? 1000),
        priceGroupOne: toEuroInput(s?.priceGroupOneDanceCents ?? 1750),
        priceGroupTwo: toEuroInput(s?.priceGroupTwoDancesCents ?? 2750),
        notificationEmail: s?.notificationEmail ?? 'secretaris@vrolijkedrammers.nl',
      });
    }
  }, [settings.isSuccess, settings.data, form]);

  const save = useApiMutation(
    () =>
      api.PUT('/api/v1/admin/dance-festival/settings', {
        body: {
          name: String(form!.name),
          festivalDate: String(form!.festivalDate),
          location: String(form!.location),
          pageEnabled: Boolean(form!.pageEnabled),
          registrationOpensAt: startOfDay(String(form!.opensOn)),
          registrationClosesAt: endOfDay(String(form!.closesOn)),
          capacity: Number(form!.capacity),
          paymentsEnabled: Boolean(form!.paymentsEnabled),
          priceSoloDuoCents: fromEuroInput(String(form!.priceSoloDuo)),
          priceGroupOneDanceCents: fromEuroInput(String(form!.priceGroupOne)),
          priceGroupTwoDancesCents: fromEuroInput(String(form!.priceGroupTwo)),
          notificationEmail: String(form!.notificationEmail),
        },
      }),
    [KEYS.settings, KEYS.list],
  );

  if (!form) return settings.error ? <ProblemAlert error={settings.error} /> : <p>Laden…</p>;
  const set = (key: string, value: string | boolean) => setForm({ ...form, [key]: value });
  const s = settings.data;

  async function uploadDocument(kind: 'reglement' | 'privacyverklaring', file: File | undefined) {
    if (!file) return;
    setUploadError(null);
    const data = new FormData();
    data.append('file', file);
    try {
      await upload(auth, 'POST', `/api/v1/admin/dance-festival/documents/${kind}`, data);
      setMessage(`${kind === 'reglement' ? 'Reglement' : 'Privacyverklaring'} opgeslagen.`);
      await settings.refetch();
    } catch (error) {
      setUploadError(error);
    }
  }

  return (
    <form
      className="card"
      onSubmit={(event) => {
        event.preventDefault();
        save.mutate(undefined, { onSuccess: () => setMessage('Instellingen opgeslagen.') });
      }}
    >
      <SuccessMessage message={message} />
      <h2>Pagina en inschrijving</h2>
      <Checkbox
        label="Pagina op de website (onder Doe mee)"
        checked={Boolean(form.pageEnabled)}
        onChange={(e) => set('pageEnabled', e.target.checked)}
      />
      <div className="form-grid">
        <Field
          label="Naam"
          required
          value={String(form.name)}
          onChange={(e) => set('name', e.target.value)}
          hint="Bijv. Dansgardefestival 2027"
        />
        <Field
          label="Datum festival"
          type="date"
          required
          value={String(form.festivalDate)}
          onChange={(e) => set('festivalDate', e.target.value)}
        />
        <Field
          label="Locatie"
          required
          value={String(form.location)}
          onChange={(e) => set('location', e.target.value)}
        />
      </div>
      <div className="form-grid">
        <Field
          label="Inschrijven vanaf"
          type="date"
          value={String(form.opensOn)}
          onChange={(e) => set('opensOn', e.target.value)}
          hint="Leeg = nog niet open"
        />
        <Field
          label="Inschrijven t/m"
          type="date"
          value={String(form.closesOn)}
          onChange={(e) => set('closesOn', e.target.value)}
        />
        <Field
          label="Aantal plekken"
          type="number"
          min={0}
          max={1000}
          required
          value={String(form.capacity)}
          onChange={(e) => set('capacity', e.target.value)}
          hint="Eén plek per goedgekeurde dans"
        />
      </div>
      <h2>Tarieven en betalen</h2>
      <Checkbox
        label="Betalen via iDEAL (betaallink na goedkeuring)"
        checked={Boolean(form.paymentsEnabled)}
        onChange={(e) => set('paymentsEnabled', e.target.checked)}
      />
      <div className="form-grid">
        <Field
          label="Solo of duo (€)"
          inputMode="decimal"
          value={String(form.priceSoloDuo)}
          onChange={(e) => set('priceSoloDuo', e.target.value)}
        />
        <Field
          label="Groep, 1 dans (€)"
          inputMode="decimal"
          value={String(form.priceGroupOne)}
          onChange={(e) => set('priceGroupOne', e.target.value)}
        />
        <Field
          label="Groep, 2 dansen (€)"
          inputMode="decimal"
          value={String(form.priceGroupTwo)}
          onChange={(e) => set('priceGroupTwo', e.target.value)}
        />
      </div>
      <h2>Meldingen</h2>
      <Field
        label="E-mailadres voor meldingen"
        type="email"
        required
        value={String(form.notificationEmail)}
        onChange={(e) => set('notificationEmail', e.target.value)}
        hint="Nieuwe en gewijzigde inschrijvingen komen hier binnen; antwoorden van verenigingen ook."
      />
      <ProblemAlert error={save.error} />
      <div className="actions">
        <button type="submit" className="button" disabled={save.isPending}>
          Opslaan
        </button>
      </div>

      <h2>Documenten (PDF)</h2>
      {s ? (
        <div className="form-grid">
          <div className="field">
            <label htmlFor="doc-reglement">
              Reglement{' '}
              {s.hasRules ? <span className="badge ok">aanwezig</span> : <span className="badge warn">ontbreekt</span>}
            </label>
            <input
              id="doc-reglement"
              type="file"
              accept="application/pdf"
              onChange={(e) => void uploadDocument('reglement', e.target.files?.[0])}
            />
          </div>
          <div className="field">
            <label htmlFor="doc-privacy">
              Privacyverklaring{' '}
              {s.hasPrivacy ? (
                <span className="badge ok">aanwezig</span>
              ) : (
                <span className="badge warn">ontbreekt</span>
              )}
            </label>
            <input
              id="doc-privacy"
              type="file"
              accept="application/pdf"
              onChange={(e) => void uploadDocument('privacyverklaring', e.target.files?.[0])}
            />
          </div>
        </div>
      ) : (
        <p className="muted">Sla eerst de instellingen op; daarna kun je de documenten toevoegen.</p>
      )}
      <ProblemAlert error={uploadError} />
    </form>
  );
}
