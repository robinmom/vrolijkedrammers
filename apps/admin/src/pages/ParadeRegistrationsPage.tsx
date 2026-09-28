import { Link, useParams } from '@tanstack/react-router';
import { useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import {
  useApiMutation,
  useMe,
  useParadeCategories,
  useParadeLineupSummary,
  useParadeRegistration,
  useParadeRegistrations,
  type AdminRegistration,
  type LineupSummary,
  type PublishResult,
  type RegistrationFilter,
  type ReviewAction,
  type ReviewSummary,
} from '../api/hooks';
import { DataTable, columnHelper } from '../components/DataTable';
import { Dialog } from '../components/Dialog';
import { Icon } from '../components/Icon';
import { Pagination } from '../components/Pagination';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { ApiError, describeProblem } from '../api/errors';
import { formatDateTime, registrationSourceLabels, registrationStatusLabels } from '../format';

const statusTone: Record<string, string> = {
  Submitted: 'warn',
  UnderReview: 'info',
  AdditionalInformationRequired: 'warn',
  Approved: 'ok',
  StartNumberAssigned: 'ok',
  Rejected: 'error',
  Withdrawn: '',
};

function StatusBadge({ status }: { status: string }) {
  return <span className={`badge ${statusTone[status] ?? ''}`}>{registrationStatusLabels[status] ?? status}</span>;
}

const meters = (value: number | null | undefined) => (value == null ? '–' : `${String(value).replace('.', ',')} m`);

const helper = columnHelper<ReviewSummary>();
const columns = [
  helper.accessor('registrationNumber', { header: 'Nr.', cell: (info) => info.getValue() ?? '–' }),
  helper.accessor('startNumber', { header: 'Start', cell: (info) => info.getValue() ?? '–' }),
  helper.accessor('groupName', {
    header: 'Groep',
    cell: (info) => (
      <Link to="/optocht/inschrijvingen/$id" params={{ id: info.row.original.id }}>
        {info.getValue() ?? 'Zonder naam'}
      </Link>
    ),
  }),
  helper.accessor('categoryName', {
    header: 'Categorie',
    cell: (info) => (
      <>
        {info.getValue() ?? '–'}
        {info.row.original.youth ? <span className="badge info">Jeugd</span> : null}
        {info.row.original.hasVehicle ? <span className="badge">Voertuig</span> : null}
      </>
    ),
  }),
  helper.accessor('subject', { header: 'Onderwerp', cell: (info) => info.getValue() ?? '–' }),
  helper.accessor('contactName', {
    header: 'Contact',
    cell: (info) => (
      <>
        {info.getValue() ?? '–'}
        {info.row.original.contactPhone ? (
          <div className="muted small-text">{info.row.original.contactPhone}</div>
        ) : null}
      </>
    ),
  }),
  helper.accessor((r) => r.adultCount + r.childrenCount, {
    id: 'deelnemers',
    header: 'Deelnemers',
    cell: (info) =>
      `${info.getValue()} (${info.row.original.adultCount} volw. · ${info.row.original.childrenCount} kind.)`,
  }),
  helper.accessor((r) => r.measuredLengthMeters ?? r.estimatedLengthMeters ?? 0, {
    id: 'lengte',
    header: 'Lengte',
    cell: (info) =>
      info.row.original.measuredLengthMeters != null ? (
        <>
          {meters(info.row.original.measuredLengthMeters)} <span className="muted small-text">gemeten</span>
        </>
      ) : (
        <>
          {meters(info.row.original.estimatedLengthMeters)} <span className="muted small-text">geschat</span>
        </>
      ),
  }),
  helper.accessor('additionalInformation', {
    header: 'Extra info',
    cell: (info) => {
      const text = info.getValue();
      return text ? <span title={text}>{text.length > 40 ? `${text.slice(0, 40)}…` : text}</span> : '–';
    },
  }),
  helper.accessor('status', {
    header: 'Status',
    cell: (info) => (
      <>
        <StatusBadge status={info.getValue()} />
        {info.row.original.supplementReceived ? <span className="badge warn">Aanvulling ontvangen</span> : null}
        {info.row.original.hasWarnings ? <span className="badge warn">Let op</span> : null}
      </>
    ),
  }),
  helper.accessor('source', {
    header: 'Via',
    cell: (info) => registrationSourceLabels[info.getValue()] ?? info.getValue(),
  }),
  helper.accessor('submittedAt', { header: 'Ingediend', cell: (info) => formatDateTime(info.getValue()) }),
];

const missingLabels: Record<string, string> = {
  StartNumber: 'Zonder startnummer',
  MeasuredLength: 'Zonder gemeten lengte',
  Warnings: 'Met waarschuwingen',
  JuryElsewhere: 'Jury op ander adres',
  Documents: 'Zonder documenten',
};

/** Snelle weergaven (docs/13 §7.1): een vaste combinatie van filters. */
const views: { label: string; filter: RegistrationFilter }[] = [
  { label: 'Alles', filter: {} },
  { label: 'Te beoordelen', filter: { status: 'Submitted' } },
  { label: 'Aanvulling gevraagd', filter: { status: 'AdditionalInformationRequired' } },
  { label: 'Goedgekeurd zonder startnummer', filter: { status: 'Approved', missing: 'StartNumber' } },
];

function LineupKpis({ summary }: { summary: LineupSummary }) {
  return (
    <section className="kpis" aria-label="Totalen van de optocht">
      <div className="kpi">
        <span className="kpi-label">Inschrijvingen</span>
        <span className="kpi-value">{summary.active}</span>
        <span className="kpi-hint">Zonder afgewezen en ingetrokken</span>
      </div>
      <div className="kpi">
        <span className="kpi-label">Goedgekeurd</span>
        <span className="kpi-value">{summary.approved}</span>
        <span className="kpi-hint">
          {summary.withStartNumber} met startnummer · {summary.published} gepubliceerd
        </span>
      </div>
      <div className="kpi">
        <span className="kpi-label">Deelnemers</span>
        <span className="kpi-value">{summary.participants}</span>
        <span className="kpi-hint">Volwassenen en kinderen</span>
      </div>
      <div className="kpi">
        <span className="kpi-label">Lengte optocht</span>
        <span className="kpi-value">{meters(Math.round(summary.lineupLengthMeters))}</span>
        <span className="kpi-hint">Goedgekeurde groepen, inclusief tussenruimte</span>
      </div>
    </section>
  );
}

/**
 * Optochtinschrijvingen. Fase 11: elke inschrijving wordt door de Optochtcommissie beoordeeld; pas na goedkeuring is ze
 * definitief. Fase 12a: filters en snelle weergaven, totalen per categorie, startnummers publiceren.
 */
export function ParadeRegistrationsPage() {
  const api = useApi();
  const me = useMe();
  const canAssign = (me.data?.permissions ?? []).includes('parade.assign-start-number');
  const [filter, setFilter] = useState<RegistrationFilter>({});
  const [page, setPage] = useState(1);
  const [publishing, setPublishing] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const registrations = useParadeRegistrations(filter, page);
  const summary = useParadeLineupSummary();
  const categories = useParadeCategories();
  const set = (patch: RegistrationFilter) => {
    setFilter((f) => ({ ...f, ...patch }));
    setPage(1);
  };
  const toPublish = summary.data ? summary.data.withStartNumber - summary.data.published : 0;
  const publish = useApiMutation(
    async () => (await api.POST('/api/v1/admin/parade-registrations/publish-start-numbers')).data,
    [['parade-registrations'], ['parade-registration']],
  );

  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>Optochtinschrijvingen</h1>
          <p className="page-subtitle">
            Neem nieuwe inschrijvingen in behandeling en keur ze goed of af; pas na goedkeuring is een inschrijving
            definitief. Ken daarna startnummers toe en publiceer ze: elke groep krijgt dan een melding en een e-mail.
          </p>
        </div>
        {canAssign ? (
          <div className="actions">
            <button type="button" className="button" disabled={toPublish <= 0} onClick={() => setPublishing(true)}>
              Startnummers publiceren{toPublish > 0 ? ` (${toPublish})` : ''}
            </button>
          </div>
        ) : null}
      </div>
      <SuccessMessage message={message} />
      {summary.data ? <LineupKpis summary={summary.data} /> : null}
      {summary.data && summary.data.categories.length ? (
        <details className="card">
          <summary>Per categorie</summary>
          <table className="table">
            <caption className="visually-hidden">Totalen per categorie</caption>
            <thead>
              <tr>
                <th scope="col">Categorie</th>
                <th scope="col">Inschrijvingen</th>
                <th scope="col">Deelnemers</th>
                <th scope="col">Lengte</th>
              </tr>
            </thead>
            <tbody>
              {summary.data.categories.map((c) => (
                <tr key={c.name}>
                  <th scope="row">{c.name}</th>
                  <td>{c.registrations}</td>
                  <td>{c.participants}</td>
                  <td>{meters(c.lengthMeters)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </details>
      ) : null}
      <div className="toolbar" role="group" aria-label="Snelle weergaven">
        {views.map((v) => {
          const active = sameFilter(filter, v.filter);
          return (
            <button
              key={v.label}
              type="button"
              className={active ? 'button small' : 'button secondary small'}
              aria-pressed={active}
              onClick={() => {
                setFilter(v.filter);
                setPage(1);
              }}
            >
              {v.label}
            </button>
          );
        })}
      </div>
      <div className="toolbar">
        <div className="field">
          <label htmlFor="inschrijving-status">Status</label>
          <select
            id="inschrijving-status"
            value={filter.status ?? ''}
            onChange={(e) => set({ status: e.target.value || undefined })}
          >
            <option value="">Alle</option>
            {[
              'Submitted',
              'UnderReview',
              'AdditionalInformationRequired',
              'Approved',
              'StartNumberAssigned',
              'Rejected',
              'Withdrawn',
            ].map((s) => (
              <option key={s} value={s}>
                {registrationStatusLabels[s]}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label htmlFor="inschrijving-categorie">Categorie</label>
          <select
            id="inschrijving-categorie"
            value={filter.categoryId ?? ''}
            onChange={(e) => set({ categoryId: e.target.value ? Number(e.target.value) : undefined })}
          >
            <option value="">Alle</option>
            {(categories.data ?? []).map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label htmlFor="inschrijving-doelgroep">Doelgroep</label>
          <select
            id="inschrijving-doelgroep"
            value={filter.ageGroup ?? ''}
            onChange={(e) => set({ ageGroup: e.target.value || undefined })}
          >
            <option value="">Alle</option>
            <option value="Adult">Volwassenen</option>
            <option value="Youth">Jeugd</option>
          </select>
        </div>
        <div className="field">
          <label htmlFor="inschrijving-voertuig">Voertuig</label>
          <select
            id="inschrijving-voertuig"
            value={filter.hasVehicle === undefined ? '' : String(filter.hasVehicle)}
            onChange={(e) => set({ hasVehicle: e.target.value === '' ? undefined : e.target.value === 'true' })}
          >
            <option value="">Alle</option>
            <option value="true">Met voertuig</option>
            <option value="false">Zonder voertuig</option>
          </select>
        </div>
        <div className="field">
          <label htmlFor="inschrijving-ontbrekend">Ontbrekend of afwijkend</label>
          <select
            id="inschrijving-ontbrekend"
            value={filter.missing ?? ''}
            onChange={(e) => set({ missing: e.target.value || undefined })}
          >
            <option value="">Niet filteren</option>
            {Object.entries(missingLabels).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </div>
        <div className="field grow-field">
          <label htmlFor="inschrijving-zoeken">Zoeken</label>
          <input
            id="inschrijving-zoeken"
            type="search"
            placeholder="Groep, contact, onderwerp, e-mail of nummer"
            value={filter.search ?? ''}
            onChange={(e) => set({ search: e.target.value || undefined })}
          />
        </div>
      </div>
      <ProblemAlert error={registrations.error} />
      <DataTable
        caption="Optochtinschrijvingen"
        columns={columns}
        data={registrations.data?.items ?? []}
        emptyText="Geen inschrijvingen gevonden."
        footer={
          registrations.data ? (
            <Pagination
              page={registrations.data.page}
              pageSize={registrations.data.pageSize}
              totalCount={registrations.data.totalCount}
              onPage={setPage}
              noun="inschrijvingen"
            />
          ) : null
        }
      />
      <Dialog open={publishing} title="Startnummers publiceren" onClose={() => setPublishing(false)}>
        <p>
          {toPublish} {toPublish === 1 ? 'groep krijgt' : 'groepen krijgen'} de status &quot;Startnummer toegekend&quot;
          en een melding en e-mail met hun startnummer.
          {summary.data && summary.data.approved - summary.data.withStartNumber > 0
            ? ` ${summary.data.approved - summary.data.withStartNumber} goedgekeurde ${summary.data.approved - summary.data.withStartNumber === 1 ? 'groep heeft' : 'groepen hebben'} nog geen startnummer en ${summary.data.approved - summary.data.withStartNumber === 1 ? 'wordt' : 'worden'} overgeslagen.`
            : ''}
        </p>
        <ProblemAlert error={publish.error} />
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => setPublishing(false)}>
            Annuleren
          </button>
          <button
            type="button"
            className="button"
            disabled={publish.isPending}
            onClick={() =>
              publish.mutate(undefined, {
                onSuccess: (result) => {
                  setPublishing(false);
                  setMessage(
                    `${(result as PublishResult | undefined)?.published ?? 0} startnummers gepubliceerd. De groepen zijn ingelicht.`,
                  );
                },
              })
            }
          >
            Publiceren
          </button>
        </div>
      </Dialog>
    </>
  );
}

function stripEmpty(filter: RegistrationFilter): RegistrationFilter {
  return Object.fromEntries(
    Object.entries(filter).filter(([, v]) => v !== undefined && v !== ''),
  ) as RegistrationFilter;
}

function sameFilter(a: RegistrationFilter, b: RegistrationFilter) {
  return JSON.stringify(stripEmpty(a)) === JSON.stringify(stripEmpty(b));
}

const actionLabels: Record<ReviewAction, string> = {
  StartReview: 'In behandeling nemen',
  Approve: 'Goedkeuren',
  RequestInformation: 'Aanvulling vragen',
  Reject: 'Afwijzen',
  Reopen: 'Heropenen',
};
const needsReason: ReviewAction[] = ['Reject', 'RequestInformation', 'Reopen'];

/** Detail met alle gegevens, documenten, historie en de beoordelingsknoppen (met <c>parade.manage</c>). */
export function ParadeRegistrationDetailPage() {
  const { id } = useParams({ from: '/optocht/inschrijvingen/$id' });
  const api = useApi();
  const me = useMe();
  const canManage = (me.data?.permissions ?? []).includes('parade.manage');
  const registration = useParadeRegistration(id);
  const [pending, setPending] = useState<ReviewAction | null>(null);
  const [reason, setReason] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const act = useApiMutation(
    (v: { action: ReviewAction; reason: string | null }) =>
      api.POST('/api/v1/admin/parade-registrations/{id}/review', { params: { path: { id } }, body: v }),
    [['parade-registration', id], ['parade-registrations']],
  );

  function run(action: ReviewAction) {
    if (needsReason.includes(action)) {
      setReason('');
      setPending(action);
    } else {
      act.mutate(
        { action, reason: null },
        { onSuccess: () => setMessage(`${actionLabels[action]}: gelukt. De groep is ingelicht.`) },
      );
    }
  }

  function submitReason(event: FormEvent) {
    event.preventDefault();
    if (pending) {
      act.mutate(
        { action: pending, reason },
        {
          onSuccess: () => {
            setMessage(`${actionLabels[pending]}: gelukt. De groep is ingelicht.`);
            setPending(null);
          },
        },
      );
    }
  }

  async function download(documentId: string) {
    const { data } = await api.GET('/api/v1/admin/parade-registrations/{id}/documents/{documentId}/download', {
      params: { path: { id, documentId } },
    });
    if (data?.url) {
      window.open(data.url, '_blank', 'noopener');
    }
  }

  if (!registration.data) {
    return <>{registration.error ? <ProblemAlert error={registration.error} /> : <p>Laden…</p>}</>;
  }
  const r = registration.data;
  const last = r.statusHistory[r.statusHistory.length - 1];
  const supplemented =
    r.status === 'UnderReview' &&
    last?.fromStatus === 'AdditionalInformationRequired' &&
    Boolean(last.reason?.startsWith('Aanvulling ingediend'));
  const address = (a: typeof r.buildAddress) =>
    [
      a.street && `${a.street} ${a.houseNumber ?? ''}${a.addition ?? ''}`.trim(),
      [a.postalCode, a.city].filter(Boolean).join(' '),
    ]
      .filter(Boolean)
      .join(', ');
  return (
    <>
      <Link to="/optocht/inschrijvingen" className="back-link">
        <Icon name="terug" size={16} /> Optochtinschrijvingen
      </Link>
      <div className="page-header">
        <div className="page-title">
          <h1>
            {r.registrationNumber ? `${r.registrationNumber}. ` : ''}
            {r.groupName} <StatusBadge status={r.status} />
          </h1>
          <p className="page-subtitle">
            {registrationSourceLabels[r.source] ?? r.source} · ingediend {formatDateTime(r.submittedAt)}
            {r.managers.length ? ` · beheerd door ${r.managers.join(', ')}` : ''}
          </p>
        </div>
        {canManage && r.allowedActions.length ? (
          <div className="actions">
            {r.allowedActions.map((a) => (
              <button
                key={a}
                type="button"
                className={a === 'Approve' ? 'button' : a === 'Reject' ? 'button danger' : 'button secondary'}
                disabled={act.isPending}
                onClick={() => run(a)}
              >
                {actionLabels[a]}
              </button>
            ))}
          </div>
        ) : null}
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={act.error} />
      {supplemented ? (
        <div className="alert alert-warning" role="status">
          De groep heeft de gevraagde aanvulling ingediend. Beoordeel de inschrijving opnieuw.
        </div>
      ) : null}
      {r.warnings.length ? (
        <div className="alert alert-warning" role="status">
          {r.warnings.join(' ')}
        </div>
      ) : null}
      {r.additionalInformation ? (
        <section className="alert alert-warning" aria-labelledby="extra-info">
          <h2 id="extra-info" className="alert-title">
            Extra informatie van de groep
          </h2>
          <p className="pre-line">{r.additionalInformation}</p>
        </section>
      ) : null}
      <LineupCard registration={r} />
      <div className="columns">
        <section className="card" aria-labelledby="gegevens">
          <h2 id="gegevens">Gegevens</h2>
          <dl className="stats">
            <div>
              <dt>Categorie</dt>
              <dd>{r.categoryName ?? '–'}</dd>
            </div>
            <div>
              <dt>Deelnemers</dt>
              <dd>
                {r.adultCount} volw. · {r.childrenCount} kind.
              </dd>
            </div>
            <div>
              <dt>Geschatte lengte</dt>
              <dd>{r.estimatedLengthMeters ? `${r.estimatedLengthMeters} m` : '–'}</dd>
            </div>
            <div>
              <dt>Onderwerp</dt>
              <dd>{r.subject ?? '–'}</dd>
            </div>
          </dl>
          {r.subjectDescription ? <p>{r.subjectDescription}</p> : null}
          <h3>Contact</h3>
          <p>
            {r.contactName} · {r.contactPhone} · <a href={`mailto:${r.contactEmail}`}>{r.contactEmail}</a>
          </p>
          <h3>Bouwadres</h3>
          <p>{address(r.buildAddress)}</p>
          <h3>Stalling voor de jury</h3>
          <p>{r.juryInspectionSameAsBuildAddress ? 'Zelfde als het bouwadres' : address(r.juryAddress)}</p>
          <h3>Documenten</h3>
          {r.documents.length === 0 ? <p className="muted">Geen documenten.</p> : null}
          <ul className="list">
            {r.documents.map((d) => (
              <li key={d.id} className="list-row">
                <span className="grow">{d.fileName}</span>
                <button type="button" className="button secondary small" onClick={() => void download(d.id)}>
                  Downloaden <span className="visually-hidden">{d.fileName}</span>
                </button>
              </li>
            ))}
          </ul>
        </section>
        <section className="card" aria-labelledby="historie">
          <h2 id="historie">Historie</h2>
          <ul className="list">
            {[...r.statusHistory].reverse().map((h, i) => (
              <li key={i}>
                <strong>{registrationStatusLabels[h.toStatus] ?? h.toStatus}</strong> · {formatDateTime(h.occurredAt)}
                {h.actorName ? ` · ${h.actorName}` : ''}
                {h.reason ? <div className="muted">{h.reason}</div> : null}
              </li>
            ))}
          </ul>
          {r.changes.length ? (
            <>
              <h3>Wijzigingen</h3>
              <ul className="list">
                {r.changes.map((c, i) => (
                  <li key={i} className="small-text">
                    {c.field}: {c.oldValue ?? '–'} → {c.newValue ?? '–'}{' '}
                    <span className="muted">({formatDateTime(c.changedAt)})</span>
                  </li>
                ))}
              </ul>
            </>
          ) : null}
        </section>
      </div>
      <Dialog
        open={pending !== null}
        title={pending ? (actionLabels[pending] ?? pending) : ''}
        onClose={() => setPending(null)}
      >
        <form onSubmit={submitReason}>
          <div className="field">
            <label htmlFor="reden">Reden (de groep krijgt deze tekst te zien)</label>
            <textarea
              id="reden"
              rows={4}
              required
              maxLength={1000}
              value={reason}
              onChange={(e) => setReason(e.target.value)}
            />
          </div>
          <ProblemAlert error={act.error} />
          <div className="actions">
            <button type="button" className="button secondary" onClick={() => setPending(null)}>
              Annuleren
            </button>
            <button
              type="submit"
              className={pending === 'Reject' ? 'button danger' : 'button'}
              disabled={act.isPending || !reason.trim()}
            >
              {pending ? actionLabels[pending] : ''}
            </button>
          </div>
        </form>
      </Dialog>
    </>
  );
}

/**
 * Startnummer en gemeten lengte (fase 12a). Startnummer met <c>parade.assign-start-number</c>, alleen bij een
 * goedgekeurde inschrijving; is het nummer bezet, dan kan de commissie wisselen. Gemeten lengte met <c>parade.manage</c>.
 */
function LineupCard({ registration: r }: { registration: AdminRegistration }) {
  const api = useApi();
  const me = useMe();
  const permissions = me.data?.permissions ?? [];
  const canAssign =
    permissions.includes('parade.assign-start-number') &&
    (r.status === 'Approved' || r.status === 'StartNumberAssigned');
  const canMeasure = permissions.includes('parade.manage');
  const [startNumber, setStartNumber] = useState(r.startNumber?.toString() ?? '');
  const [length, setLength] = useState(r.measuredLengthMeters?.toString().replace('.', ',') ?? '');
  const [message, setMessage] = useState<string | null>(null);
  const invalidate = [['parade-registration', r.id], ['parade-registrations']];
  const saveNumber = useApiMutation(
    (v: { swap: boolean }) =>
      api.PUT('/api/v1/admin/parade-registrations/{id}/start-number', {
        params: { path: { id: r.id } },
        body: { startNumber: startNumber.trim() ? Number(startNumber) : null, swap: v.swap },
      }),
    invalidate,
  );
  const saveLength = useApiMutation(
    () =>
      api.PUT('/api/v1/admin/parade-registrations/{id}/measured-length', {
        params: { path: { id: r.id } },
        body: { measuredLengthMeters: length.trim() ? Number(length.replace(',', '.')) : null },
      }),
    invalidate,
  );
  const taken = saveNumber.error instanceof ApiError && saveNumber.error.problem.code === 'START_NUMBER_TAKEN';

  function submitNumber(event: FormEvent, swap = false) {
    event.preventDefault();
    setMessage(null);
    saveNumber.mutate(
      { swap },
      {
        onSuccess: () =>
          setMessage(
            swap
              ? 'Startnummers gewisseld.'
              : r.status === 'StartNumberAssigned'
                ? 'Startnummer opgeslagen; de groep is ingelicht.'
                : 'Startnummer opgeslagen.',
          ),
      },
    );
  }

  return (
    <section className="card" aria-labelledby="startnummer-lengte">
      <h2 id="startnummer-lengte">Startnummer en lengte</h2>
      <SuccessMessage message={message} />
      <div className="form-grid">
        <form onSubmit={(e) => submitNumber(e)}>
          <div className="field">
            <label htmlFor="startnummer">Startnummer</label>
            {canAssign ? (
              <input
                id="startnummer"
                inputMode="numeric"
                pattern="[0-9]*"
                maxLength={4}
                value={startNumber}
                onChange={(e) => setStartNumber(e.target.value.replace(/\D/g, ''))}
              />
            ) : (
              <p id="startnummer">{r.startNumber ?? 'Nog niet toegekend'}</p>
            )}
            {!canAssign && !r.startNumber ? (
              <p className="muted small-text">Een startnummer kan pas na goedkeuring.</p>
            ) : null}
          </div>
          {canAssign ? (
            <div className="actions">
              <button type="submit" className="button secondary small" disabled={saveNumber.isPending}>
                Startnummer opslaan
              </button>
            </div>
          ) : null}
          {taken ? (
            <div role="alert" className="alert alert-warning">
              {describeProblem(saveNumber.error)}
              <div className="actions">
                <button type="button" className="button small" onClick={(e) => submitNumber(e, true)}>
                  Wisselen
                </button>
              </div>
            </div>
          ) : (
            <ProblemAlert error={saveNumber.error} />
          )}
        </form>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            setMessage(null);
            saveLength.mutate(undefined, { onSuccess: () => setMessage('Gemeten lengte opgeslagen.') });
          }}
        >
          <div className="field">
            <label htmlFor="gemeten-lengte">Gemeten lengte (meter)</label>
            {canMeasure ? (
              <input
                id="gemeten-lengte"
                inputMode="decimal"
                maxLength={5}
                value={length}
                onChange={(e) => setLength(e.target.value.replace(/[^0-9,.]/g, ''))}
                aria-describedby="geschatte-lengte"
              />
            ) : (
              <p id="gemeten-lengte">{meters(r.measuredLengthMeters)}</p>
            )}
            <p id="geschatte-lengte" className="muted small-text">
              Geschat door de groep: {meters(r.estimatedLengthMeters)}
            </p>
          </div>
          {canMeasure ? (
            <div className="actions">
              <button type="submit" className="button secondary small" disabled={saveLength.isPending}>
                Lengte opslaan
              </button>
            </div>
          ) : null}
          <ProblemAlert error={saveLength.error} />
        </form>
      </div>
    </section>
  );
}
