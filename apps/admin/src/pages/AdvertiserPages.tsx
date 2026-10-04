import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import {
  ADVERTISER_KEYS,
  KIND_LABELS,
  PAYMENT_LABELS,
  STATUS_LABELS,
  euro,
  useAdvertiser,
  useAdvertiserCollectors,
  useAdvertisers,
  useAdvertiserStatus,
  useCampaignYear,
  type AdvertiserFilters,
  type AdvertiserImportPreview,
  type AdvertiserKind,
  type AdvertiserPayment,
  type AdvertiserRequest,
  type AdvertiserYearStatus,
} from '../api/advertisers';
import { useApiMutation } from '../api/hooks';
import { uploadJson } from '../api/upload';
import { useAuth } from '../auth/AuthContext';
import { ConfirmDialog } from '../components/Dialog';
import { Checkbox, Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDate, formatDateTime } from '../format';
import { PreviewTable } from './CollectionsPage';

function CollectorSelect({
  id,
  value,
  onChange,
  label,
}: {
  id: string;
  value: string;
  onChange: (value: string) => void;
  label: string;
}) {
  const collectors = useAdvertiserCollectors();
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <select id={id} value={value} onChange={(e) => onChange(e.target.value)}>
        <option value="">{label === 'Collectant' ? 'Alle collectanten' : 'Geen collectant'}</option>
        {(collectors.data ?? []).map((c) => (
          <option key={c.memberId} value={c.memberId}>
            {c.name}
          </option>
        ))}
      </select>
    </div>
  );
}

/** Adverteerders → Overzicht (fase 27b). */
export function AdvertisersPage() {
  const [filters, setFilters] = useState<AdvertiserFilters>({ search: '', collector: '', kind: '', payment: '' });
  const advertisers = useAdvertisers(filters);
  const set = (change: Partial<AdvertiserFilters>) => setFilters({ ...filters, ...change });
  const rows = advertisers.data ?? [];
  return (
    <>
      <div className="page-header">
        <div>
          <h1>Adverteerders</h1>
          <p className="muted">Adverteerders en gevers van de Drammerskrant, met hun collectant uit het kader.</p>
        </div>
        <div className="actions">
          <Link to="/adverteerders/import" className="button secondary">
            Excel inlezen
          </Link>
          <Link to="/adverteerders/$id" params={{ id: 'nieuw' }} className="button">
            Adverteerder toevoegen
          </Link>
        </div>
      </div>
      <div className="toolbar">
        <Field
          label="Zoeken"
          type="search"
          placeholder="Bedrijf, contactpersoon, plaats of nummer"
          value={filters.search}
          onChange={(e) => set({ search: e.target.value })}
        />
        <CollectorSelect
          id="filter-collectant"
          label="Collectant"
          value={filters.collector}
          onChange={(collector) => set({ collector })}
        />
        <div className="field">
          <label htmlFor="filter-soort">Soort</label>
          <select
            id="filter-soort"
            value={filters.kind}
            onChange={(e) => set({ kind: e.target.value as AdvertiserKind | '' })}
          >
            <option value="">Alle soorten</option>
            {Object.entries(KIND_LABELS).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label htmlFor="filter-betaling">Betaling</label>
          <select
            id="filter-betaling"
            value={filters.payment}
            onChange={(e) => set({ payment: e.target.value as AdvertiserPayment | '' })}
          >
            <option value="">Alle</option>
            {Object.entries(PAYMENT_LABELS).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </div>
      </div>
      <ProblemAlert error={advertisers.error} />
      <p className="muted">{rows.length} adverteerders</p>
      <div className="table-scroll table-wrapper" tabIndex={0} role="region" aria-label="Adverteerders">
        <table className="table">
          <caption className="visually-hidden">Adverteerders</caption>
          <thead>
            <tr>
              <th scope="col">Nr.</th>
              <th scope="col">Bedrijf</th>
              <th scope="col">Soort</th>
              <th scope="col">Betaling</th>
              <th scope="col">Collectant</th>
              <th scope="col">Laatste bijdrage</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((a) => (
              <tr key={a.id} className={a.active ? undefined : 'muted'}>
                <td>{a.number}</td>
                <td>
                  <Link to="/adverteerders/$id" params={{ id: a.id }}>
                    {a.companyName}
                  </Link>
                  {a.city ? <span className="muted"> · {a.city}</span> : null}
                  {a.addedViaApp ? <span className="badge warn">nieuw via app</span> : null}
                  {a.active ? null : <span className="badge">niet actief</span>}
                </td>
                <td>{KIND_LABELS[a.kind]}</td>
                <td>
                  {PAYMENT_LABELS[a.payment]}
                  {a.payment === 'Mandate' && !(a.hasIban && a.hasMandate) ? (
                    <span className="badge warn">{a.hasIban ? 'geen machtigingsnr.' : 'geen IBAN'}</span>
                  ) : null}
                </td>
                <td>
                  {a.collectorName ??
                    (a.importedCollectorName ? (
                      <span className="badge warn">{a.importedCollectorName} (niet gekoppeld)</span>
                    ) : (
                      '—'
                    ))}
                </td>
                <td>{a.lastYear ? `${a.lastYear}: ${a.lastFree ? 'gratis' : euro(a.lastAmount)}` : '—'}</td>
              </tr>
            ))}
            {advertisers.data?.length === 0 ? (
              <tr>
                <td colSpan={6} className="muted">
                  Geen adverteerders gevonden. Lees eerst het Excel-overzicht in.
                </td>
              </tr>
            ) : null}
          </tbody>
        </table>
      </div>
    </>
  );
}

const empty: AdvertiserRequest = {
  number: 0,
  companyName: '',
  contactName: null,
  phone: null,
  mobile: null,
  email: null,
  addressLine: null,
  postalCode: null,
  city: null,
  website: null,
  page: null,
  kind: 'Advertisement',
  payment: 'Mandate',
  iban: null,
  mandateReference: null,
  collectorMemberId: null,
  notes: null,
  active: true,
};

/** Een adverteerder toevoegen of wijzigen; de IBAN is alleen op verzoek voluit te zien. */
export function AdvertiserEditorPage() {
  const { id } = useParams({ from: '/adverteerders/$id' });
  const isNew = id === 'nieuw';
  const api = useApi();
  const navigate = useNavigate();
  const existing = useAdvertiser(isNew ? null : id);
  const [form, setForm] = useState<AdvertiserRequest>(empty);
  const [fullIban, setFullIban] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    const a = existing.data;
    if (a) {
      setForm({
        number: a.number,
        companyName: a.companyName,
        contactName: a.contactName,
        phone: a.phone,
        mobile: a.mobile,
        email: a.email,
        addressLine: a.addressLine,
        postalCode: a.postalCode,
        city: a.city,
        website: a.website,
        page: a.page,
        kind: a.kind,
        payment: a.payment,
        iban: null,
        mandateReference: a.mandateReference,
        collectorMemberId: a.collectorMemberId,
        notes: a.notes,
        active: a.active,
      });
    }
  }, [existing.data]);

  const save = useApiMutation(
    async (body: AdvertiserRequest) =>
      isNew
        ? (await api.POST('/api/v1/admin/advertisers', { body })).data?.id
        : (await api.PUT('/api/v1/admin/advertisers/{id}', { params: { path: { id } }, body }), id),
    ADVERTISER_KEYS,
  );
  const showIban = useApiMutation(
    async () => (await api.GET('/api/v1/admin/advertisers/{id}/iban', { params: { path: { id } } })).data?.iban ?? null,
    [],
  );
  const set = (change: Partial<AdvertiserRequest>) => setForm({ ...form, ...change });
  const text = (key: keyof AdvertiserRequest) => (form[key] as string | null) ?? '';

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate(form, {
      onSuccess: (newId) => {
        setMessage('Adverteerder opgeslagen.');
        setForm({ ...form, iban: null });
        if (isNew && typeof newId === 'string') void navigate({ to: '/adverteerders/$id', params: { id: newId } });
      },
    });
  }

  const a = existing.data;
  return (
    <>
      <p>
        <Link to="/adverteerders">← Adverteerders</Link>
      </p>
      <h1>{isNew ? 'Adverteerder toevoegen' : form.companyName || 'Adverteerder'}</h1>
      <SuccessMessage message={message} />
      {a?.addedViaApp ? <p className="badge warn">Aangemeld via de app; kijk de gegevens na en sla op.</p> : null}
      {a?.importedCollectorName && !a.collectorMemberId ? (
        <p className="muted">
          Collectant in het Excel-bestand: {a.importedCollectorName}. Kies hieronder het kaderlid.
        </p>
      ) : null}
      <form className="card" onSubmit={submit}>
        <div className="form-grid">
          <Field
            label="Nummer"
            type="number"
            required
            min={1}
            value={form.number || ''}
            onChange={(e) => set({ number: Number(e.target.value) })}
          />
          <Field
            label="Naam bedrijf"
            required
            maxLength={200}
            value={form.companyName}
            onChange={(e) => set({ companyName: e.target.value })}
          />
          <Field
            label="Contactpersoon"
            maxLength={150}
            value={text('contactName')}
            onChange={(e) => set({ contactName: e.target.value || null })}
          />
          <Field
            label="E-mailadres"
            type="email"
            maxLength={254}
            value={text('email')}
            onChange={(e) => set({ email: e.target.value || null })}
          />
          <Field
            label="Telefoon"
            maxLength={30}
            value={text('phone')}
            onChange={(e) => set({ phone: e.target.value || null })}
          />
          <Field
            label="Mobiel"
            maxLength={30}
            value={text('mobile')}
            onChange={(e) => set({ mobile: e.target.value || null })}
          />
          <Field
            label="Adres"
            maxLength={200}
            value={text('addressLine')}
            onChange={(e) => set({ addressLine: e.target.value || null })}
          />
          <Field
            label="Postcode"
            maxLength={10}
            value={text('postalCode')}
            onChange={(e) => set({ postalCode: e.target.value || null })}
          />
          <Field
            label="Plaats"
            maxLength={100}
            value={text('city')}
            onChange={(e) => set({ city: e.target.value || null })}
          />
          <Field
            label="Website"
            maxLength={200}
            value={text('website')}
            onChange={(e) => set({ website: e.target.value || null })}
          />
          <Field
            label="Pagina in de krant"
            maxLength={50}
            value={text('page')}
            onChange={(e) => set({ page: e.target.value || null })}
          />
          <CollectorSelect
            id="adverteerder-collectant"
            label="Collectant (kaderlid)"
            value={form.collectorMemberId ?? ''}
            onChange={(value) => set({ collectorMemberId: value || null })}
          />
          <div className="field">
            <label htmlFor="adverteerder-soort">Soort (A/V/G)</label>
            <select
              id="adverteerder-soort"
              value={form.kind}
              onChange={(e) => set({ kind: e.target.value as AdvertiserKind })}
            >
              {Object.entries(KIND_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor="adverteerder-betaling">Betaling</label>
            <select
              id="adverteerder-betaling"
              value={form.payment}
              onChange={(e) => set({ payment: e.target.value as AdvertiserPayment })}
            >
              {Object.entries(PAYMENT_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </div>
          <Field
            label="IBAN"
            hint={
              a?.maskedIban
                ? `Nu: ${fullIban ?? a.maskedIban}. Leeg laten = ongewijzigd; "-" = weghalen.`
                : 'Nodig voor de incasso bij een machtiging.'
            }
            maxLength={40}
            value={text('iban')}
            onChange={(e) => set({ iban: e.target.value || null })}
          />
          <Field
            label="Machtigingsnummer"
            maxLength={35}
            value={text('mandateReference')}
            onChange={(e) => set({ mandateReference: e.target.value || null })}
          />
        </div>
        {a?.maskedIban && !fullIban ? (
          <p>
            <button
              type="button"
              className="button secondary small"
              onClick={() =>
                showIban.mutate(undefined, { onSuccess: (iban) => setFullIban(typeof iban === 'string' ? iban : null) })
              }
            >
              IBAN voluit tonen
            </button>
          </p>
        ) : null}
        <div className="field">
          <label htmlFor="adverteerder-opmerking">Opmerkingen</label>
          <textarea
            id="adverteerder-opmerking"
            rows={3}
            maxLength={2000}
            value={text('notes')}
            onChange={(e) => set({ notes: e.target.value || null })}
          />
        </div>
        <Checkbox
          label="Actief (telt mee in de campagne)"
          checked={form.active ?? true}
          onChange={(e) => set({ active: e.target.checked })}
        />
        <ProblemAlert error={save.error ?? showIban.error} />
        <div className="actions">
          <button type="submit" className="button" disabled={save.isPending}>
            Opslaan
          </button>
        </div>
      </form>

      {a && a.years.length > 0 ? (
        <section className="card" aria-labelledby="bijdragen-kop">
          <h2 id="bijdragen-kop">Bijdragen</h2>
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Bijdragen per jaar">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">Jaar</th>
                  <th scope="col">Bedrag</th>
                  <th scope="col">Stand</th>
                  <th scope="col">Gewijzigd</th>
                </tr>
              </thead>
              <tbody>
                {a.years.map((y) => (
                  <tr key={y.year}>
                    <td>{y.year}</td>
                    <td>{y.isFree ? 'gratis' : euro(y.amount)}</td>
                    <td>
                      {STATUS_LABELS[y.status]}
                      {y.note ? <span className="muted"> · {y.note}</span> : null}
                    </td>
                    <td>{y.statusChangedAt ? formatDateTime(y.statusChangedAt) : 'uit Excel'}</td>
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

/** Adverteerders → Campagne: wie is opgehaald, met een voortgangsbalk en een filter op collectant. */
export function AdvertiserStatusPage() {
  const api = useApi();
  const campaign = useCampaignYear();
  const [year, setYear] = useState<number | null>(null);
  const [collector, setCollector] = useState('');
  const shownYear = year ?? campaign.data?.year ?? null;
  const status = useAdvertiserStatus(shownYear, collector);
  const setStatus = useApiMutation(
    (v: { id: string; status: AdvertiserYearStatus; amount: number | null }) =>
      api.PUT('/api/v1/admin/advertisers/{id}/years/{year}', {
        params: { path: { id: v.id, year: shownYear! } },
        body: { status: v.status, amount: v.amount, note: null },
      }),
    ADVERTISER_KEYS,
  );
  const makeCampaignYear = useApiMutation(
    () => api.PUT('/api/v1/admin/advertisers/campaign-year', { body: { year: shownYear! } }),
    ADVERTISER_KEYS,
  );

  const report = status.data;
  const totals = report?.totals;
  const handled = totals ? totals.collected + totals.stopped : 0;
  const percent = totals && totals.total > 0 ? Math.round((100 * handled) / totals.total) : 0;

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Campagne {shownYear ?? ''}</h1>
          <p className="muted">Wie is opgehaald, wie stopt en bij wie de collectant nog langs moet.</p>
        </div>
      </div>
      <div className="toolbar">
        <Field
          label="Jaar"
          type="number"
          min={2000}
          max={2100}
          value={shownYear ?? ''}
          onChange={(e) => setYear(e.target.value ? Number(e.target.value) : null)}
        />
        <CollectorSelect id="status-collectant" label="Collectant" value={collector} onChange={setCollector} />
        {campaign.data && shownYear !== campaign.data.year ? (
          <button type="button" className="button secondary" onClick={() => makeCampaignYear.mutate(undefined)}>
            Maak {shownYear} het campagnejaar
          </button>
        ) : null}
      </div>
      <ProblemAlert error={status.error ?? setStatus.error ?? makeCampaignYear.error} />

      {totals ? (
        <section className="card" aria-labelledby="voortgang-kop">
          <h2 id="voortgang-kop">Voortgang: {percent}%</h2>
          <div
            className="progress"
            role="progressbar"
            aria-label="Afgehandeld"
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={percent}
          >
            <span style={{ width: `${percent}%` }} />
          </div>
          <section className="kpis" aria-label="Tellers">
            <div className="kpi">
              <span className="kpi-label">Opgehaald</span>
              <span className="kpi-value">{totals.collected}</span>
              <span className="kpi-hint">{euro(totals.collectedAmount)}</span>
            </div>
            <div className="kpi">
              <span className="kpi-label">Stopt</span>
              <span className="kpi-value">{totals.stopped}</span>
            </div>
            <div className="kpi">
              <span className="kpi-label">Nog open</span>
              <span className="kpi-value">{totals.open}</span>
            </div>
            <div className="kpi">
              <span className="kpi-label">Verwacht</span>
              <span className="kpi-value">{euro(totals.expectedAmount)}</span>
              <span className="kpi-hint">van {totals.total} adverteerders</span>
            </div>
          </section>
        </section>
      ) : null}

      {report && !collector ? (
        <section className="card" aria-labelledby="per-collectant-kop">
          <h2 id="per-collectant-kop">Per collectant</h2>
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Per collectant">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">Collectant</th>
                  <th scope="col">Opgehaald</th>
                  <th scope="col">Stopt</th>
                  <th scope="col">Open</th>
                  <th scope="col">Bedrag</th>
                </tr>
              </thead>
              <tbody>
                {report.perCollector.map((c) => (
                  <tr key={c.collectorMemberId ?? c.name}>
                    <td>
                      {c.collectorMemberId ? (
                        <button
                          type="button"
                          className="link-button"
                          onClick={() => setCollector(c.collectorMemberId!)}
                        >
                          {c.name}
                        </button>
                      ) : (
                        c.name
                      )}
                    </td>
                    <td>
                      {c.totals.collected} van {c.totals.total}
                    </td>
                    <td>{c.totals.stopped}</td>
                    <td>{c.totals.open}</td>
                    <td>{euro(c.totals.collectedAmount)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      ) : null}

      {report ? (
        <div
          className="table-scroll table-wrapper"
          tabIndex={0}
          role="region"
          aria-label="Adverteerders in de campagne"
        >
          <table className="table">
            <caption className="visually-hidden">Adverteerders in de campagne {report.year}</caption>
            <thead>
              <tr>
                <th scope="col">Bedrijf</th>
                <th scope="col">Collectant</th>
                <th scope="col">Vorig jaar</th>
                <th scope="col">Bedrag</th>
                <th scope="col">Stand</th>
              </tr>
            </thead>
            <tbody>
              {report.rows.map((r) => (
                <tr key={r.id}>
                  <td>
                    <Link to="/adverteerders/$id" params={{ id: r.id }}>
                      {r.number}. {r.companyName}
                    </Link>
                    {r.city ? <span className="muted"> · {r.city}</span> : null}
                  </td>
                  <td>{r.collectorName ?? '—'}</td>
                  <td>{euro(r.previousAmount)}</td>
                  <td>{r.isFree ? 'gratis' : euro(r.amount)}</td>
                  <td>
                    <label className="visually-hidden" htmlFor={`stand-${r.id}`}>
                      Stand van {r.companyName}
                    </label>
                    <select
                      id={`stand-${r.id}`}
                      value={r.status}
                      disabled={setStatus.isPending}
                      onChange={(e) =>
                        setStatus.mutate({ id: r.id, status: e.target.value as AdvertiserYearStatus, amount: null })
                      }
                    >
                      {Object.entries(STATUS_LABELS).map(([value, label]) => (
                        <option key={value} value={value}>
                          {label}
                        </option>
                      ))}
                    </select>
                  </td>
                </tr>
              ))}
              {report.rows.length === 0 ? (
                <tr>
                  <td colSpan={5} className="muted">
                    Geen adverteerders.
                  </td>
                </tr>
              ) : null}
            </tbody>
          </table>
        </div>
      ) : null}
    </>
  );
}

/** Adverteerders → Excel inlezen: eerst controleren, dan inlezen (alleen zonder fouten). */
export function AdvertiserImportPage() {
  const auth = useAuth();
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<AdvertiserImportPreview | null>(null);
  const [done, setDone] = useState<AdvertiserImportPreview | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  async function send(path: string, onResult: (result: AdvertiserImportPreview) => void) {
    if (!file) return;
    setBusy(true);
    setError(null);
    const form = new FormData();
    form.append('file', file);
    try {
      onResult(await uploadJson<AdvertiserImportPreview>(auth, path, form));
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  }

  const issues = (title: string, list: { row: number; message: string }[], className: string) =>
    list.length > 0 ? (
      <section className={`card ${className}`} aria-label={title}>
        <h2>
          {title} ({list.length})
        </h2>
        <ul>
          {list.map((i, index) => (
            <li key={index}>
              {i.row > 0 ? `Regel ${i.row}: ` : ''}
              {i.message}
            </li>
          ))}
        </ul>
      </section>
    ) : null;

  return (
    <>
      <p>
        <Link to="/adverteerders">← Adverteerders</Link>
      </p>
      <h1>Excel inlezen</h1>
      <p className="muted">
        Lees het advertentie-overzicht (.xlsx of .xlsm) in. Bestaande adverteerders worden bijgewerkt op nummer; de
        stand die in het portal of de app is gezet, blijft staan. Bij betaling mag alleen M (machtiging) of C (contant)
        staan.
      </p>
      <div className="card">
        <div className="field">
          <label htmlFor="import-bestand">Excel-bestand</label>
          <input
            id="import-bestand"
            type="file"
            accept=".xlsx,.xlsm"
            onChange={(e) => {
              setFile(e.target.files?.[0] ?? null);
              setPreview(null);
              setDone(null);
            }}
          />
        </div>
        <div className="actions">
          <button
            type="button"
            className="button secondary"
            disabled={!file || busy}
            onClick={() => send('/api/v1/admin/advertisers/import/preview', setPreview)}
          >
            Controleren
          </button>
          <button
            type="button"
            className="button"
            disabled={!preview || preview.errors.length > 0 || busy}
            onClick={() => send('/api/v1/admin/advertisers/import', setDone)}
          >
            Inlezen
          </button>
        </div>
        <ProblemAlert error={error} />
        {done ? <SuccessMessage message={`Ingelezen: ${done.new} nieuw en ${done.updated} bijgewerkt.`} /> : null}
      </div>
      {preview ? (
        <>
          <p>
            {preview.rows} regels: {preview.new} nieuw, {preview.updated} bijgewerkt. Bijdragen uit{' '}
            {preview.years.join(', ') || 'geen jaren'}.
          </p>
          {issues('Fouten', preview.errors, 'card-error')}
          {issues('Waarschuwingen', preview.warnings, '')}
        </>
      ) : null}
    </>
  );
}

/**
 * Adverteerders → Incasso (fase 27c): alle opgehaalde bijdragen met een machtiging van het campagnejaar in één
 * pain.008-bestand. Wie al in een incasso van dat jaar zat, gaat er niet nog een keer in.
 */
export function AdvertiserCollectionsPage() {
  const api = useApi();
  const queryClient = useQueryClient();
  const campaign = useCampaignYear();
  const [date, setDate] = useState('');
  const [description, setDescription] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [confirm, setConfirm] = useState(false);
  const [removing, setRemoving] = useState<string | null>(null);
  const [downloadError, setDownloadError] = useState<unknown>(null);
  const year = campaign.data?.year;

  const preview = useQuery({
    queryKey: ['advertisers', 'collections', 'preview', date, year],
    enabled: date.length === 10 && year !== undefined,
    queryFn: async () =>
      (await api.GET('/api/v1/admin/advertisers/collections/preview', { params: { query: { date, year } } })).data!,
  });
  const runs = useQuery({
    queryKey: ['advertisers', 'collections', 'runs'],
    queryFn: async () => (await api.GET('/api/v1/admin/advertisers/collections')).data!,
  });
  const create = useApiMutation(
    () =>
      api.POST('/api/v1/admin/advertisers/collections', {
        body: { date, year: year ?? null, description: description.trim() || null },
      }),
    ADVERTISER_KEYS,
  );
  const remove = useApiMutation(
    (id: string) => api.DELETE('/api/v1/admin/advertisers/collections/{id}', { params: { path: { id } } }),
    ADVERTISER_KEYS,
  );

  async function download(id: string, collectionDate: string) {
    setDownloadError(null);
    try {
      const { data } = await api.GET('/api/v1/admin/advertisers/collections/{id}/file', {
        params: { path: { id } },
        parseAs: 'blob',
      });
      if (data) {
        const url = URL.createObjectURL(data);
        const link = document.createElement('a');
        link.href = url;
        link.download = `incasso-adverteerders-${collectionDate.replaceAll('-', '')}.xml`;
        link.click();
        URL.revokeObjectURL(url);
        await queryClient.invalidateQueries({ queryKey: ['advertisers', 'collections', 'runs'] });
      }
    } catch (error) {
      setDownloadError(error);
    }
  }

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Incasso adverteerders {year ?? ''}</h1>
          <p className="muted">
            Alle adverteerders met een machtiging die in {year ?? 'het campagnejaar'} op opgehaald staan. De gegevens
            van de vereniging (IBAN en incassant-ID) zijn dezelfde als bij de contributie.
          </p>
        </div>
      </div>
      <ProblemAlert error={runs.error ?? create.error ?? remove.error ?? downloadError} />
      <SuccessMessage message={message} />

      <section className="card" aria-labelledby="nieuwe-incasso">
        <h2 id="nieuwe-incasso">Nieuwe incasso</h2>
        <div className="form-grid">
          <Field label="Incassodatum" type="date" required value={date} onChange={(e) => setDate(e.target.value)} />
          <Field
            label="Omschrijving (op het afschrift)"
            hint={`Leeg = Drammerskrant ${year ?? '[jaar]'} CV De Vrolijke Drammers`}
            maxLength={100}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
        </div>
        <ProblemAlert error={preview.error} />
        {preview.data ? <PreviewTable preview={preview.data} advertisers /> : null}
        {preview.data ? (
          <div className="actions">
            <button
              type="button"
              className="button"
              disabled={!preview.data.creditorComplete || preview.data.count === 0 || create.isPending}
              onClick={() => setConfirm(true)}
            >
              Incassorun maken ({preview.data.count})
            </button>
          </div>
        ) : null}
      </section>

      <section className="card" aria-labelledby="incassoruns">
        <h2 id="incassoruns">Incassoruns</h2>
        <p className="card-hint">
          Lever het bestand aan in Rabo Internetbankieren (Betalen → Incasso&apos;s → bestand aanleveren). Er wordt pas
          geïncasseerd nadat je het ondertekent.
        </p>
        {runs.data && runs.data.length === 0 ? <p className="muted">Nog geen incassoruns.</p> : null}
        {runs.data && runs.data.length > 0 ? (
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Incassoruns adverteerders">
            <table className="table compact">
              <thead>
                <tr>
                  <th scope="col">Incassodatum</th>
                  <th scope="col">Omschrijving</th>
                  <th scope="col">Aantal</th>
                  <th scope="col">Totaal</th>
                  <th scope="col">Gedownload</th>
                  <th scope="col">
                    <span className="visually-hidden">Acties</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {runs.data.map((r) => (
                  <tr key={r.id}>
                    <td>{formatDate(r.collectionDate)}</td>
                    <td>{r.description}</td>
                    <td>{r.lineCount}</td>
                    <td>{euro(r.total)}</td>
                    <td>{r.exportedAt ? formatDateTime(r.exportedAt) : 'Nog niet'}</td>
                    <td>
                      <div className="actions">
                        <button
                          type="button"
                          className="button secondary small"
                          aria-label={`Bestand downloaden: incasso ${formatDate(r.collectionDate)}`}
                          onClick={() => void download(r.id, r.collectionDate)}
                        >
                          Bestand downloaden
                        </button>
                        <button
                          type="button"
                          className="button ghost small"
                          aria-label={`Incasso ${formatDate(r.collectionDate)} verwijderen`}
                          onClick={() => setRemoving(r.id)}
                        >
                          Verwijderen
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </section>

      <ConfirmDialog
        open={confirm}
        title="Incassorun maken?"
        message={`Er wordt een run gemaakt met ${preview.data?.count ?? 0} incasso's (${euro(preview.data?.total ?? 0)}) op ${formatDate(date)}. Daarna download je het bestand voor de bank.`}
        confirmLabel="Run maken"
        busy={create.isPending}
        onConfirm={() =>
          create.mutate(undefined, {
            onSuccess: () => {
              setConfirm(false);
              setMessage('De incassorun is gemaakt. Download het bestand hieronder.');
            },
          })
        }
        onCancel={() => setConfirm(false)}
      />
      <ConfirmDialog
        open={removing !== null}
        title="Incassorun verwijderen?"
        message="Verwijder alleen een run die niet bij de bank is aangeleverd, of die je daar hebt geannuleerd. De adverteerders komen dan weer in het voorbeeld."
        confirmLabel="Verwijderen"
        busy={remove.isPending}
        onConfirm={() => remove.mutate(removing!, { onSuccess: () => setRemoving(null) })}
        onCancel={() => setRemoving(null)}
      />
    </>
  );
}
