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
  season,
  seasonShort,
  useAdvertiser,
  useAdvertiserCollectors,
  useAdvertisers,
  useAdvertiserInfo,
  useAdvertiserStatus,
  useCampaignYear,
  type AdvertiserFilters,
  type AdvertiserImportPreview,
  type AdvertiserKind,
  type AdvertiserPayment,
  type AdvertiserRequest,
  type AdvertiserYearStatus,
} from '../api/advertisers';
import { useApiMutation, type Schemas } from '../api/hooks';
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
  const campaign = useCampaignYear();
  const historyYears = campaign.data ? [0, 1, 2, 3, 4].map((i) => campaign.data!.year - 4 + i) : [];
  const set = (change: Partial<AdvertiserFilters>) => setFilters({ ...filters, ...change });
  const rows = advertisers.data ?? [];
  return (
    <>
      <div className="page-header">
        <div>
          <h1>Adverteerders</h1>
          <p className="muted">
            Adverteerders en gevers van de Drammerskrant, met hun collectant (kaderlid of collectant).
          </p>
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
              {historyYears.map((y) => (
                <th key={y} scope="col" className="numeric">
                  <abbr title={`Carnavalsjaar ${season(y)}`}>{seasonShort(y)}</abbr>
                </th>
              ))}
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
                {historyYears.map((y) => {
                  const h = a.history.find((x) => x.year === y);
                  return (
                    <td key={y} className="numeric">
                      {!h ? (
                        '—'
                      ) : h.isFree ? (
                        'gratis'
                      ) : h.status === 'Stopped' ? (
                        'stopt'
                      ) : h.status === 'Open' ? (
                        <span className="muted">open</span>
                      ) : (
                        euro(h.amount)
                      )}
                    </td>
                  );
                })}
              </tr>
            ))}
            {advertisers.data?.length === 0 ? (
              <tr>
                <td colSpan={5 + historyYears.length} className="muted">
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
          Collectant in het Excel-bestand: {a.importedCollectorName}. Kies hieronder het kaderlid of de collectant.
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
            label="Collectant (kaderlid of rol Collectant)"
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
          <h2 id="bijdragen-kop">Bijdragen per carnavalsjaar</h2>
          <ContributionChart years={a.years} />
          <p>
            In totaal{' '}
            <strong>
              {euro(a.years.reduce((sum, y) => sum + (y.status === 'Collected' && !y.isFree ? (y.amount ?? 0) : 0), 0))}
            </strong>{' '}
            in {a.years.filter((y) => y.status === 'Collected').length} jaar.
          </p>
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Bijdragen per jaar">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">Carnavalsjaar</th>
                  <th scope="col">Bedrag</th>
                  <th scope="col">Stand</th>
                  <th scope="col">Gewijzigd</th>
                </tr>
              </thead>
              <tbody>
                {a.years.map((y) => (
                  <tr key={y.year}>
                    <td>{season(y.year)}</td>
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

/**
 * Collectieperiode (fase 27i) en informatie voor de collectanten (fase 27h) van dit campagnejaar: tarieven, inleverdatum
 * en contactpersoon. De collectant ziet de periode bovenaan en de informatie onder de knop Info in de app.
 */
function CollectorInfoCard({ year }: { year: number }) {
  const api = useApi();
  const info = useAdvertiserInfo(year);
  const [text, setText] = useState<string | null>(null);
  const [start, setStart] = useState<string | null>(null);
  const [end, setEnd] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const save = useApiMutation(
    (body: { text: string; startDate: string | null; endDate: string | null }) =>
      api.PUT('/api/v1/admin/advertisers/info/{year}', { params: { path: { year } }, body }),
    ADVERTISER_KEYS,
  );
  const value = text ?? info.data?.text ?? '';
  const startDate = start ?? info.data?.startDate ?? '';
  const endDate = end ?? info.data?.endDate ?? '';
  const changed = () => setSaved(false);

  return (
    <section className="card" aria-labelledby="info-kop">
      <h2 id="info-kop">Collectieperiode en informatie {season(year)}</h2>
      <p className="muted">
        Wanneer de collectanten langsgaan, met tarieven, inleverdatum en contactpersoon. De collectant ziet dit in de
        app; de informatie onder de knop Info.
      </p>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          setSaved(false);
          save.mutate(
            { text: value, startDate: startDate || null, endDate: endDate || null },
            { onSuccess: () => setSaved(true) },
          );
        }}
      >
        <div className="form-grid">
          <Field
            label="Startdatum"
            type="date"
            value={startDate}
            onChange={(e) => {
              setStart(e.target.value);
              changed();
            }}
          />
          <Field
            label="Einddatum"
            type="date"
            min={startDate || undefined}
            value={endDate}
            onChange={(e) => {
              setEnd(e.target.value);
              changed();
            }}
          />
        </div>
        <div className="field">
          <label htmlFor="collectant-info">Informatie</label>
          <textarea
            id="collectant-info"
            rows={10}
            maxLength={1000}
            value={value}
            onChange={(e) => {
              setText(e.target.value);
              setSaved(false);
            }}
          />
        </div>
        <ProblemAlert error={info.error ?? save.error} />
        <div className="actions">
          <button type="submit" className="button" disabled={save.isPending || info.isLoading}>
            Opslaan
          </button>
          {saved ? <span className="muted">Opgeslagen.</span> : null}
        </div>
      </form>
    </section>
  );
}

type CampaignRow = Schemas['AdvertiserStatusRow'];
type CampaignColumn = 'company' | 'collector' | 'previous' | 'amount' | 'status' | 'round' | 'cash' | 'note';
interface CampaignSort {
  column: CampaignColumn;
  desc: boolean;
}

const CAMPAIGN_COLUMNS: [CampaignColumn, string][] = [
  ['company', 'Bedrijf'],
  ['collector', 'Collectant'],
  ['previous', 'Vorig jaar'],
  ['amount', 'Bedrag'],
  ['status', 'Stand'],
  ['round', 'Ronde'],
  ['cash', 'Contant ontvangen'],
  ['note', 'Opmerking'],
];

const STATUS_ORDER: Record<AdvertiserYearStatus, number> = { Open: 0, Collected: 1, Stopped: 2 };

const sortValue: Record<CampaignColumn, (r: CampaignRow) => string | number | null> = {
  company: (r) => r.companyName,
  collector: (r) => r.collectorName ?? null,
  previous: (r) => r.previousAmount ?? null,
  amount: (r) => (r.isFree ? 0 : (r.amount ?? null)),
  status: (r) => STATUS_ORDER[r.status],
  round: (r) => r.round ?? null,
  cash: (r) => r.paidAt ?? null,
  note: (r) => r.note || null,
};

/** Sorteren op een kolom (fase 27j); lege waarden staan altijd onderaan, bij gelijke waarden op bedrijfsnaam. */
function sortCampaignRows(rows: CampaignRow[], sort: CampaignSort | null): CampaignRow[] {
  if (!sort) return rows;
  const value = sortValue[sort.column];
  const text = new Intl.Collator('nl', { sensitivity: 'base', numeric: true });
  return [...rows].sort((a, b) => {
    const [x, y] = [value(a), value(b)];
    if (x === null || y === null) return x === y ? 0 : x === null ? 1 : -1;
    const order = typeof x === 'number' && typeof y === 'number' ? x - y : text.compare(String(x), String(y));
    return (sort.desc ? -order : order) || text.compare(a.companyName, b.companyName);
  });
}

/** Kolomkop met sorteerknop, zoals in de generieke beheertabel: eerst oplopend, dan aflopend. */
function SortHeader({
  column,
  label,
  sort,
  onSort,
}: {
  column: CampaignColumn;
  label: string;
  sort: CampaignSort | null;
  onSort: (sort: CampaignSort) => void;
}) {
  const active = sort?.column === column ? sort : null;
  return (
    <th scope="col" aria-sort={active ? (active.desc ? 'descending' : 'ascending') : undefined}>
      <button
        type="button"
        className="sort-button"
        onClick={() => onSort({ column, desc: active ? !active.desc : false })}
      >
        {label}
        <span aria-hidden="true">{active ? (active.desc ? ' ▼' : ' ▲') : ''}</span>
        <span className="visually-hidden">{active ? `, ${active.desc ? 'aflopend' : 'oplopend'} gesorteerd` : ''}</span>
      </button>
    </th>
  );
}

/** Ronde 1, 2 en 3: dezelfde kleuren als de regels in de Excel-export (lichtgroen, lichtblauw, lichtgeel). */
const ROUND_COLORS: Record<number, string> = { 1: '#D9EAD3', 2: '#DDEBF7', 3: '#FFF2CC' };
/** Stopt: lichtrood, gaat voor de ronde (zoals in de export). */
const STOPPED_COLOR = '#F4CCCC';

type StatusRow = NonNullable<ReturnType<typeof useAdvertiserStatus>['data']>['rows'][number];

const rowColor = (r: StatusRow) =>
  r.status === 'Stopped' ? STOPPED_COLOR : r.round ? ROUND_COLORS[r.round] : undefined;

/** Adverteerders → Campagne: wie is opgehaald, met een voortgangsbalk en een filter op collectant. */
export function AdvertiserStatusPage() {
  const api = useApi();
  const campaign = useCampaignYear();
  const [year, setYear] = useState<number | null>(null);
  const [collector, setCollector] = useState('');
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<AdvertiserYearStatus | ''>('');
  const [roundFilter, setRoundFilter] = useState('');
  const [sort, setSort] = useState<CampaignSort | null>(null);
  const shownYear = year ?? campaign.data?.year ?? null;
  const status = useAdvertiserStatus(shownYear, collector);
  const [confirmAll, setConfirmAll] = useState(false);
  const [reminded, setReminded] = useState<string | null>(null);
  const remind = useApiMutation(
    async (collectorMemberIds: string[] | null) =>
      (await api.POST('/api/v1/admin/advertisers/reminders', { body: { year: shownYear, collectorMemberIds } })).data,
    [],
  );
  const sendReminder = (ids: string[] | null) => {
    setReminded(null);
    remind.mutate(ids, {
      onSuccess: (data) => {
        const sent = (data as { sent: number } | undefined)?.sent ?? 0;
        setReminded(
          sent === 0
            ? 'Niemand heeft nog adverteerders open staan.'
            : `Herinnering verstuurd naar ${sent} collectant${sent === 1 ? '' : 'en'}.`,
        );
      },
    });
  };
  const setStatus = useApiMutation(
    (v: { id: string; status: AdvertiserYearStatus; amount: number | null; note: string | null }) =>
      api.PUT('/api/v1/admin/advertisers/{id}/years/{year}', {
        params: { path: { id: v.id, year: shownYear! } },
        // De opmerking van de collectant (app) blijft staan.
        body: { status: v.status, amount: v.amount, note: v.note },
      }),
    ADVERTISER_KEYS,
  );
  const setRound = useApiMutation(
    (v: { id: string; round: number | null }) =>
      api.PUT('/api/v1/admin/advertisers/{id}/years/{year}/round', {
        params: { path: { id: v.id, year: shownYear! } },
        body: { round: v.round },
      }),
    ADVERTISER_KEYS,
  );
  const setCash = useApiMutation(
    (v: { id: string; received: boolean }) =>
      api.PUT('/api/v1/admin/advertisers/{id}/years/{year}/cash', {
        params: { path: { id: v.id, year: shownYear! } },
        body: { received: v.received },
      }),
    ADVERTISER_KEYS,
  );
  const [exportError, setExportError] = useState<unknown>(null);
  async function exportExcel() {
    setExportError(null);
    try {
      const { data } = await api.GET('/api/v1/admin/advertisers/export', {
        params: { query: { year: shownYear ?? undefined } },
        parseAs: 'blob',
      });
      if (data) {
        const url = URL.createObjectURL(data);
        const link = document.createElement('a');
        link.href = url;
        link.download = `adverteerders-${shownYear ?? ''}.xlsx`;
        link.click();
        URL.revokeObjectURL(url);
      }
    } catch (error) {
      setExportError(error);
    }
  }
  const makeCampaignYear = useApiMutation(
    () => api.PUT('/api/v1/admin/advertisers/campaign-year', { body: { year: shownYear! } }),
    ADVERTISER_KEYS,
  );

  const report = status.data;
  const totals = report?.totals;
  const term = search.trim().toLowerCase();
  const rows = (report?.rows ?? []).filter(
    (r) =>
      (!term || `${r.number} ${r.companyName} ${r.city ?? ''}`.toLowerCase().includes(term)) &&
      (!statusFilter || r.status === statusFilter) &&
      (!roundFilter || (roundFilter === 'none' ? !r.round : r.round === Number(roundFilter))),
  );
  const sortedRows = sortCampaignRows(rows, sort);
  const handled = totals ? totals.collected + totals.stopped : 0;
  const percent = totals && totals.total > 0 ? Math.round((100 * handled) / totals.total) : 0;

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Campagne {shownYear ? season(shownYear) : ''}</h1>
          <p className="muted">Wie is opgehaald, wie stopt en bij wie de collectant nog langs moet.</p>
        </div>
        <button type="button" className="button secondary" onClick={() => void exportExcel()}>
          Exporteren (Excel)
        </button>
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
        <Field
          label="Zoeken"
          type="search"
          placeholder="Bedrijf, plaats of nummer"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <div className="field">
          <label htmlFor="filter-stand">Stand</label>
          <select
            id="filter-stand"
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value as AdvertiserYearStatus | '')}
          >
            <option value="">Alle standen</option>
            {Object.entries(STATUS_LABELS).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </div>
        <CollectorSelect id="status-collectant" label="Collectant" value={collector} onChange={setCollector} />
        <div className="field">
          <label htmlFor="filter-ronde">Ronde</label>
          <select id="filter-ronde" value={roundFilter} onChange={(e) => setRoundFilter(e.target.value)}>
            <option value="">Alle rondes</option>
            <option value="1">Ronde 1</option>
            <option value="2">Ronde 2</option>
            <option value="3">Ronde 3</option>
            <option value="none">Zonder ronde</option>
          </select>
        </div>
        {campaign.data && shownYear !== campaign.data.year ? (
          <button type="button" className="button secondary" onClick={() => makeCampaignYear.mutate(undefined)}>
            Maak {shownYear} het campagnejaar
          </button>
        ) : null}
      </div>
      <ProblemAlert
        error={
          status.error ?? setStatus.error ?? setRound.error ?? setCash.error ?? makeCampaignYear.error ?? exportError
        }
      />

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
            <div className="kpi">
              <span className="kpi-label">Contant ontvangen</span>
              <span className="kpi-value">{euro(totals.cashReceived)}</span>
              <span className="kpi-hint">nog te ontvangen: {euro(totals.cashOutstanding)}</span>
            </div>
          </section>
        </section>
      ) : null}

      {shownYear ? <CollectorInfoCard key={shownYear} year={shownYear} /> : null}

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
                  <th scope="col">
                    <span className="visually-hidden">Herinnering</span>
                  </th>
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
                    <td>
                      {c.collectorMemberId && c.totals.open > 0 ? (
                        <button
                          type="button"
                          className="link-button"
                          aria-label={`Herinnering naar ${c.name}`}
                          disabled={remind.isPending}
                          onClick={() => sendReminder([c.collectorMemberId!])}
                        >
                          Herinnering
                        </button>
                      ) : null}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <ProblemAlert error={remind.error} />
          <SuccessMessage message={reminded} />
          {report.perCollector.some((c) => c.collectorMemberId && c.totals.open > 0) ? (
            <div className="actions">
              <button
                type="button"
                className="button secondary"
                disabled={remind.isPending}
                onClick={() => setConfirmAll(true)}
              >
                Herinnering aan iedereen met open adverteerders
              </button>
            </div>
          ) : null}
          <ConfirmDialog
            open={confirmAll}
            title="Herinnering sturen"
            message="Alle collectanten die nog adverteerders open hebben staan krijgen een pushmelding met hun aantal en de einddatum."
            confirmLabel="Versturen"
            busy={remind.isPending}
            onConfirm={() => {
              setConfirmAll(false);
              sendReminder(null);
            }}
            onCancel={() => setConfirmAll(false)}
          />
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
                {CAMPAIGN_COLUMNS.map(([key, label]) => (
                  <SortHeader key={key} column={key} label={label} sort={sort} onSort={setSort} />
                ))}
              </tr>
            </thead>
            <tbody>
              {sortedRows.map((r) => (
                <tr key={r.id} style={rowColor(r) ? { background: rowColor(r) } : undefined}>
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
                        setStatus.mutate({
                          id: r.id,
                          status: e.target.value as AdvertiserYearStatus,
                          amount: null,
                          note: r.note ?? null,
                        })
                      }
                    >
                      {Object.entries(STATUS_LABELS).map(([value, label]) => (
                        <option key={value} value={value}>
                          {label}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td>
                    <label className="visually-hidden" htmlFor={`ronde-${r.id}`}>
                      Ronde van {r.companyName}
                    </label>
                    <select
                      id={`ronde-${r.id}`}
                      value={r.round ?? ''}
                      disabled={setRound.isPending}
                      onChange={(e) =>
                        setRound.mutate({ id: r.id, round: e.target.value ? Number(e.target.value) : null })
                      }
                    >
                      <option value="">—</option>
                      <option value="1">Ronde 1</option>
                      <option value="2">Ronde 2</option>
                      <option value="3">Ronde 3</option>
                    </select>
                  </td>
                  <td>
                    {r.payment === 'Cash' && r.status === 'Collected' && !r.isFree ? (
                      <label className="checkbox">
                        <input
                          type="checkbox"
                          checked={r.paidAt !== null}
                          disabled={setCash.isPending}
                          onChange={(e) => setCash.mutate({ id: r.id, received: e.target.checked })}
                        />
                        <span className="visually-hidden">Contant ontvangen van {r.companyName}</span>
                        {r.paidAt ? formatDate(r.paidAt.slice(0, 10)) : 'nog niet'}
                      </label>
                    ) : (
                      <span className="muted">{r.payment === 'Mandate' ? 'machtiging' : '—'}</span>
                    )}
                  </td>
                  <td>{r.note ?? <span className="muted">—</span>}</td>
                </tr>
              ))}
              {rows.length === 0 ? (
                <tr>
                  <td colSpan={8} className="muted">
                    {report.rows.length === 0 ? 'Geen adverteerders.' : 'Geen adverteerders met deze filters.'}
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
        stand die in het portal of de app is gezet, blijft staan. Bij betaling mag M (machtiging), C (contant) of R
        (rekening) staan. De kolom BIJDRAGE 2026 hoort bij carnavalsjaar 2025/2026.
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
          <h1>Incasso adverteerders {year ? season(year) : ''}</h1>
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

/** Een bestand van de API downloaden (PDF), met het access token. */
async function downloadBlob(promise: Promise<{ data?: Blob }>, fileName: string) {
  const { data } = await promise;
  if (data) {
    const url = URL.createObjectURL(data);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(url);
  }
}

/**
 * Adverteerders → Facturen (fase 27e): per campagnejaar een factuur voor elke opgehaalde bijdrage, als PDF per e-mail
 * namens de penningmeester; wie geen e-mailadres heeft, krijgt een PDF om te printen.
 */
export function AdvertiserInvoicesPage() {
  const api = useApi();
  const campaign = useCampaignYear();
  const year = campaign.data?.year;
  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [message, setMessage] = useState<string | null>(null);
  const [confirmSend, setConfirmSend] = useState(false);
  const [downloadError, setDownloadError] = useState<unknown>(null);
  const [settingsForm, setSettingsForm] = useState<{ address: string; kvk: string } | null>(null);

  const overview = useQuery({
    queryKey: ['advertisers', 'invoices', year],
    enabled: year !== undefined,
    queryFn: async () => (await api.GET('/api/v1/admin/advertisers/invoices', { params: { query: { year } } })).data!,
  });
  const settings = useQuery({
    queryKey: ['advertisers', 'invoices', 'settings'],
    queryFn: async () => (await api.GET('/api/v1/admin/advertisers/invoices/settings')).data!,
  });
  useEffect(() => {
    if (settings.data) setSettingsForm({ address: settings.data.address ?? '', kvk: settings.data.kvk ?? '' });
  }, [settings.data]);

  const saveSettings = useApiMutation(
    () =>
      api.PUT('/api/v1/admin/advertisers/invoices/settings', {
        body: { address: settingsForm?.address || null, kvk: settingsForm?.kvk || null },
      }),
    ADVERTISER_KEYS,
  );
  const create = useApiMutation(
    async () =>
      (await api.POST('/api/v1/admin/advertisers/invoices', { body: { date, year: year ?? null } })).data?.count,
    ADVERTISER_KEYS,
  );
  const send = useApiMutation(
    async () =>
      (await api.POST('/api/v1/admin/advertisers/invoices/send', { params: { query: { year } } })).data?.count,
    ADVERTISER_KEYS,
  );

  async function download(path: 'all' | 'print' | string, fileName: string) {
    setDownloadError(null);
    try {
      await downloadBlob(
        path === 'all' || path === 'print'
          ? api.GET('/api/v1/admin/advertisers/invoices/pdf', {
              params: { query: { year, withoutEmail: path === 'print' } },
              parseAs: 'blob',
            })
          : api.GET('/api/v1/admin/advertisers/invoices/{id}/pdf', { params: { path: { id: path } }, parseAs: 'blob' }),
        fileName,
      );
    } catch (error) {
      setDownloadError(error);
    }
  }

  const o = overview.data;
  return (
    <>
      <div className="page-header">
        <div>
          <h1>Facturen {year ? season(year) : ''}</h1>
          <p className="muted">
            Een factuur voor elke opgehaalde bijdrage (niet gratis) van het campagnejaar, verstuurd namens de
            penningmeester.
          </p>
        </div>
      </div>
      <ProblemAlert error={overview.error ?? create.error ?? send.error ?? saveSettings.error ?? downloadError} />
      <SuccessMessage message={message} />

      {o ? (
        <section className="card" aria-labelledby="facturen-kop">
          <h2 id="facturen-kop">Facturen</h2>
          <section className="kpis" aria-label="Tellers">
            <div className="kpi">
              <span className="kpi-label">Nog te maken</span>
              <span className="kpi-value">{o.toCreate}</span>
            </div>
            <div className="kpi">
              <span className="kpi-label">Nog te versturen</span>
              <span className="kpi-value">{o.toSend}</span>
            </div>
            <div className="kpi">
              <span className="kpi-label">Zonder e-mailadres</span>
              <span className="kpi-value">{o.withoutEmail}</span>
              <span className="kpi-hint">PDF om te printen</span>
            </div>
          </section>
          <div className="toolbar">
            <Field label="Factuurdatum" type="date" required value={date} onChange={(e) => setDate(e.target.value)} />
            <button
              type="button"
              className="button"
              disabled={o.toCreate === 0 || create.isPending || date.length !== 10}
              onClick={() =>
                create.mutate(undefined, { onSuccess: (count) => setMessage(`${count ?? 0} facturen gemaakt.`) })
              }
            >
              Facturen maken ({o.toCreate})
            </button>
            <button
              type="button"
              className="button secondary"
              disabled={o.toSend === 0 || send.isPending}
              onClick={() => setConfirmSend(true)}
            >
              Versturen per e-mail ({o.toSend})
            </button>
            <button
              type="button"
              className="button secondary"
              disabled={o.withoutEmail === 0}
              onClick={() => void download('print', `Facturen ${year} zonder e-mail.pdf`)}
            >
              PDF zonder e-mailadres
            </button>
          </div>
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Facturen">
            <table className="table compact">
              <thead>
                <tr>
                  <th scope="col">Factuur</th>
                  <th scope="col">Bedrijf</th>
                  <th scope="col">Bedrag</th>
                  <th scope="col">Betaling</th>
                  <th scope="col">E-mail</th>
                  <th scope="col">Verstuurd</th>
                </tr>
              </thead>
              <tbody>
                {o.rows.map((r) => (
                  <tr key={r.advertiserId}>
                    <td>
                      {r.invoiceId && r.invoiceNumber ? (
                        <button
                          type="button"
                          className="link-button"
                          onClick={() => void download(r.invoiceId!, `Factuur ${r.invoiceNumber}.pdf`)}
                        >
                          {r.invoiceNumber}
                        </button>
                      ) : (
                        <span className="muted">nog niet gemaakt</span>
                      )}
                    </td>
                    <td>
                      <Link to="/adverteerders/$id" params={{ id: r.advertiserId }}>
                        {r.companyName}
                      </Link>
                    </td>
                    <td>{euro(r.amount)}</td>
                    <td>{PAYMENT_LABELS[r.payment]}</td>
                    <td>{r.email ?? <span className="badge warn">geen e-mail</span>}</td>
                    <td>{r.sentAt ? formatDateTime(r.sentAt) : r.invoiceId ? 'nog niet' : '—'}</td>
                  </tr>
                ))}
                {o.rows.length === 0 ? (
                  <tr>
                    <td colSpan={6} className="muted">
                      Nog geen opgehaalde bijdragen in {o.year}.
                    </td>
                  </tr>
                ) : null}
              </tbody>
            </table>
          </div>
        </section>
      ) : null}

      {settingsForm ? (
        <form
          className="card"
          aria-labelledby="factuurgegevens-kop"
          onSubmit={(e) => {
            e.preventDefault();
            saveSettings.mutate(undefined, { onSuccess: () => setMessage('Gegevens op de factuur opgeslagen.') });
          }}
        >
          <h2 id="factuurgegevens-kop">Gegevens van de vereniging op de factuur</h2>
          <p className="card-hint">Naam en IBAN komen van de incasso (Leden → Incasso).</p>
          <div className="form-grid">
            <div className="field">
              <label htmlFor="factuur-adres">Adres (één regel per regel)</label>
              <textarea
                id="factuur-adres"
                rows={3}
                maxLength={300}
                value={settingsForm.address}
                onChange={(e) => setSettingsForm({ ...settingsForm, address: e.target.value })}
              />
            </div>
            <Field
              label="KvK-nummer"
              maxLength={12}
              value={settingsForm.kvk}
              onChange={(e) => setSettingsForm({ ...settingsForm, kvk: e.target.value })}
            />
          </div>
          <div className="actions">
            <button type="submit" className="button secondary" disabled={saveSettings.isPending}>
              Opslaan
            </button>
          </div>
        </form>
      ) : null}

      <ConfirmDialog
        open={confirmSend}
        title="Facturen versturen?"
        message={`${o?.toSend ?? 0} facturen gaan als PDF per e-mail naar de adverteerders, namens penningmeester@vrolijkedrammers.nl.`}
        confirmLabel="Versturen"
        busy={send.isPending}
        onConfirm={() =>
          send.mutate(undefined, {
            onSuccess: (count) => {
              setConfirmSend(false);
              setMessage(`${count ?? 0} facturen worden verstuurd.`);
            },
          })
        }
        onCancel={() => setConfirmSend(false)}
      />
    </>
  );
}

/** Staafjes per carnavalsjaar (opgehaald = blauw, gratis = goud, stopt/open = leeg), van oud naar nieuw. */
function ContributionChart({
  years,
}: {
  years: { year: number; amount: number | null; isFree: boolean; status: AdvertiserYearStatus }[];
}) {
  const sorted = [...years].sort((a, b) => a.year - b.year);
  const max = Math.max(1, ...sorted.map((y) => (y.status === 'Collected' ? (y.amount ?? 0) : 0)));
  const width = Math.max(1, sorted.length) * 22;
  return (
    <svg
      className="contribution-chart"
      viewBox={`0 0 ${width} 90`}
      role="img"
      aria-label={`Bijdragen ${sorted.map((y) => `${season(y.year)}: ${y.isFree ? 'gratis' : y.status === 'Collected' ? euro(y.amount) : STATUS_LABELS[y.status]}`).join(', ')}`}
    >
      {sorted.map((y, i) => {
        const value = y.status === 'Collected' ? (y.amount ?? 0) : 0;
        const height = y.isFree ? 8 : Math.round((70 * value) / max);
        return (
          <g key={y.year}>
            <rect
              x={i * 22 + 3}
              y={74 - height}
              width={16}
              height={Math.max(height, 1)}
              rx={3}
              className={y.isFree ? 'bar-free' : 'bar'}
            />
            <text x={i * 22 + 11} y={87} textAnchor="middle" className="bar-label">
              {String(y.year).slice(2)}
            </text>
          </g>
        );
      })}
    </svg>
  );
}
