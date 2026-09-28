import { Link, useParams } from '@tanstack/react-router';
import { useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import {
  useApiMutation,
  useMe,
  useParadeRegistration,
  useParadeRegistrations,
  type ReviewAction,
  type ReviewSummary,
} from '../api/hooks';
import { DataTable, columnHelper } from '../components/DataTable';
import { Dialog } from '../components/Dialog';
import { Icon } from '../components/Icon';
import { Pagination } from '../components/Pagination';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDateTime, registrationSourceLabels, registrationStatusLabels } from '../format';

const statusTone: Record<string, string> = {
  Submitted: 'warn',
  UnderReview: 'info',
  AdditionalInformationRequired: 'warn',
  Approved: 'ok',
  Rejected: 'error',
  Withdrawn: '',
};

function StatusBadge({ status }: { status: string }) {
  return <span className={`badge ${statusTone[status] ?? ''}`}>{registrationStatusLabels[status] ?? status}</span>;
}

const helper = columnHelper<ReviewSummary>();
const columns = [
  helper.accessor('registrationNumber', { header: 'Nr.', cell: (info) => info.getValue() ?? '–' }),
  helper.accessor('groupName', {
    header: 'Groep',
    cell: (info) => (
      <Link to="/optocht/inschrijvingen/$id" params={{ id: info.row.original.id }}>
        {info.getValue() ?? 'Zonder naam'}
      </Link>
    ),
  }),
  helper.accessor('categoryName', { header: 'Categorie', cell: (info) => info.getValue() ?? '–' }),
  helper.display({
    id: 'deelnemers',
    header: 'Deelnemers',
    cell: (info) => `${info.row.original.adultCount} volw. · ${info.row.original.childrenCount} kind.`,
  }),
  helper.accessor('estimatedLengthMeters', {
    header: 'Lengte',
    cell: (info) => (info.getValue() ? `${info.getValue()} m` : '–'),
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

/**
 * Optochtinschrijvingen (fase 11): elke inschrijving wordt door de Optochtcommissie beoordeeld; pas na goedkeuring is ze
 * definitief. Samenstellen en startnummers volgen in fase 12.
 */
export function ParadeRegistrationsPage() {
  const [status, setStatus] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const registrations = useParadeRegistrations(status, search, page);
  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>Optochtinschrijvingen</h1>
          <p className="page-subtitle">
            Neem nieuwe inschrijvingen in behandeling en keur ze goed of af. Pas na goedkeuring is een inschrijving
            definitief; de groep krijgt bij elke stap een melding en een e-mail.
          </p>
        </div>
      </div>
      <div className="filters">
        <div className="field">
          <label htmlFor="inschrijving-status">Status</label>
          <select
            id="inschrijving-status"
            value={status}
            onChange={(e) => {
              setStatus(e.target.value);
              setPage(1);
            }}
          >
            <option value="">Alle</option>
            {['Submitted', 'UnderReview', 'AdditionalInformationRequired', 'Approved', 'Rejected', 'Withdrawn'].map(
              (s) => (
                <option key={s} value={s}>
                  {registrationStatusLabels[s]}
                </option>
              ),
            )}
          </select>
        </div>
        <div className="field">
          <label htmlFor="inschrijving-zoeken">Zoeken</label>
          <input
            id="inschrijving-zoeken"
            type="search"
            placeholder="Groep, contactpersoon, onderwerp of nummer"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
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
    </>
  );
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
          {r.additionalInformation ? (
            <>
              <h3>Extra informatie</h3>
              <p>{r.additionalInformation}</p>
            </>
          ) : null}
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
