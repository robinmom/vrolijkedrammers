import { Link, useParams } from '@tanstack/react-router';
import { Fragment, useEffect, useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, useApplication, useApplications, type ApplicationStatus, type ApplicationSummary } from '../api/hooks';
import { DataTable, columnHelper } from '../components/DataTable';
import { ConfirmDialog, Dialog } from '../components/Dialog';
import { Field } from '../components/Field';
import { Icon } from '../components/Icon';
import { Pagination } from '../components/Pagination';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import {
  applicationSourceLabels,
  applicationStatusLabels,
  applicationStatusTone,
  formatDate,
  formatDateTime,
  provisioningStepLabels,
} from '../format';

const helper = columnHelper<ApplicationSummary>();
const columns = [
  helper.accessor('submittedAt', { header: 'Ingediend', cell: (info) => formatDateTime(info.getValue()) }),
  helper.accessor('fullName', {
    header: 'Naam',
    cell: (info) => (
      <Link to="/aanmeldingen/$id" params={{ id: info.row.original.id }}>
        {info.getValue()}
      </Link>
    ),
  }),
  helper.accessor('age', {
    header: 'Leeftijd',
    cell: (info) => (
      <>
        {info.getValue()} jaar {info.row.original.minor ? <span className="badge info">via ouder</span> : null}{' '}
        {info.row.original.membershipType === 'Dansgarde' ? <span className="badge">Dansgarde</span> : null}
        {info.row.original.split ? <span className="badge">Lid splitsen</span> : null}
      </>
    ),
  }),
  helper.accessor('city', { header: 'Woonplaats' }),
  helper.accessor('source', { header: 'Via', cell: (info) => applicationSourceLabels[info.getValue()] ?? info.getValue() }),
  helper.accessor('status', {
    header: 'Status',
    cell: (info) => (
      <span className={`badge ${applicationStatusTone[info.getValue()] ?? 'neutral'}`}>{applicationStatusLabels[info.getValue()] ?? info.getValue()}</span>
    ),
  }),
];

/**
 * Aanmeldingen (fase 9b, ADR-014): nieuwe leden via de app of de webpagina, na bevestiging van hun e-mailadres. Het
 * bestuur beoordeelt altijd handmatig; na goedkeuring maakt het systeem het lid aan in e-Boekhouden en het account.
 */
export function ApplicationsPage() {
  const [status, setStatus] = useState<ApplicationStatus | ''>('Submitted');
  const [page, setPage] = useState(1);
  const applications = useApplications(status, page);

  return (
    <>
      <div className="page-header">
        <div>
          <h1 className="page-title">Aanmeldingen</h1>
          <p className="page-subtitle">
            Nieuwe leden die zich via de app of de webpagina <code>/lid-worden</code> hebben aangemeld en hun e-mailadres hebben bevestigd.
            Na goedkeuring komt het lid automatisch in e-Boekhouden en krijgt het (of bij een kind onder de 16 de ouder) een account.
          </p>
        </div>
      </div>
      <ProblemAlert error={applications.error} />
      <DataTable
        caption="Aanmeldingen"
        columns={columns}
        data={applications.data?.items ?? []}
        emptyText={status === 'Submitted' ? 'Geen nieuwe aanmeldingen.' : 'Geen aanmeldingen.'}
        header={
          <div className="toolbar">
            <div className="field">
              <label htmlFor="aanmelding-status">Status</label>
              <select
                id="aanmelding-status"
                value={status}
                onChange={(e) => {
                  setStatus(e.target.value as ApplicationStatus | '');
                  setPage(1);
                }}
              >
                {(['Submitted', 'InReview', 'ProvisioningFailed', 'Activated', 'Rejected'] as const).map((value) => (
                  <option key={value} value={value}>
                    {applicationStatusLabels[value]}
                  </option>
                ))}
                <option value="">Alle</option>
              </select>
            </div>
          </div>
        }
        footer={
          applications.data ? (
            <Pagination page={applications.data.page} pageSize={applications.data.pageSize} totalCount={applications.data.totalCount} onPage={setPage} noun="aanmeldingen" />
          ) : null
        }
      />
    </>
  );
}

/** Eén aanmelding: gegevens (IBAN gemaskeerd), beoordelen, goedkeuren of afwijzen, en de voortgang daarna. */
export function ApplicationDetailPage() {
  const { id } = useParams({ from: '/aanmeldingen/$id' });
  const api = useApi();
  const application = useApplication(id);
  const [message, setMessage] = useState<string | null>(null);
  const [confirmApprove, setConfirmApprove] = useState(false);
  const [rejecting, setRejecting] = useState(false);
  const [reason, setReason] = useState('');
  const [notes, setNotes] = useState('');
  const keys = [['application', id], ['applications']];

  useEffect(() => {
    setNotes(application.data?.internalNotes ?? '');
  }, [application.data?.internalNotes]);

  const startReview = useApiMutation(() => api.POST('/api/v1/admin/membership-applications/{id}/start-review', { params: { path: { id } } }), keys);
  const approve = useApiMutation(() => api.POST('/api/v1/admin/membership-applications/{id}/approve', { params: { path: { id } } }), keys);
  const reject = useApiMutation(
    () => api.POST('/api/v1/admin/membership-applications/{id}/reject', { params: { path: { id } }, body: { reason } }),
    keys,
  );
  const saveNotes = useApiMutation(
    () => api.PUT('/api/v1/admin/membership-applications/{id}/notes', { params: { path: { id } }, body: { notes: notes || null } }),
    keys,
  );
  const retry = useApiMutation(() => api.POST('/api/v1/admin/membership-applications/{id}/retry', { params: { path: { id } } }), keys);

  if (!application.data) {
    return <>{application.error ? <ProblemAlert error={application.error} /> : <p>Laden…</p>}</>;
  }

  const a = application.data;
  const open = a.status === 'Submitted' || a.status === 'InReview';
  const rows: [string, string | null | undefined][] = [
    [
      'Soort lidmaatschap',
      a.splitFrom
        ? `Lid splitsen: tweede lid van ${a.splitFrom.fullName} (lidnummer ${a.splitFrom.memberNumber}); combinatie, het hoofdlid betaalt`
        : a.membershipType === 'Dansgarde'
          ? 'Dansgarde (groep "Dansgarde" in e-Boekhouden)'
          : 'Lid',
    ],
    ['Geboortedatum', `${formatDate(a.birthDate)} (${a.age} jaar)`],
    ['Geslacht', a.gender === 'm' ? 'Man' : a.gender === 'v' ? 'Vrouw' : '—'],
    ['Adres', `${a.addressLine}, ${a.postalCode} ${a.city}`],
    [a.minor ? 'E-mailadres (ouder)' : 'E-mailadres', a.email],
    ['Telefoon', a.minor ? a.guardianPhone : a.phone],
  ];
  if (a.minor) {
    rows.push(['Ouder/verzorger', a.guardianName]);
  }

  return (
    <>
      <Link to="/aanmeldingen" className="back-link">
        ← Aanmeldingen
      </Link>
      <div className="page-header">
        <div>
          <h1 className="page-title">
            {a.fullName}{' '}
            <span className={`badge ${applicationStatusTone[a.status] ?? 'neutral'}`}>{applicationStatusLabels[a.status] ?? a.status}</span>
          </h1>
          <p className="page-subtitle">
            Via {applicationSourceLabels[a.source] ?? a.source}, ingediend {formatDateTime(a.submittedAt)}
            {a.handledBy ? ` · behandelaar ${a.handledBy}` : ''}
          </p>
        </div>
        {open ? (
          <div className="actions">
            {a.status === 'Submitted' ? (
              <button type="button" className="button secondary" disabled={startReview.isPending} onClick={() => startReview.mutate(undefined)}>
                In behandeling nemen
              </button>
            ) : null}
            <button type="button" className="button secondary" onClick={() => setRejecting(true)}>
              Afwijzen
            </button>
            <button type="button" className="button" onClick={() => setConfirmApprove(true)}>
              Goedkeuren
            </button>
          </div>
        ) : null}
      </div>
      <SuccessMessage message={message} />
      {a.emailInUseBy ? (
        <div className="alert alert-warning banner" role="status">
          <Icon name="waarschuwing" size={22} />
          <div className="banner-text">
            <strong>E-mailadres al in gebruik</strong>
            <p>
              {a.email} hoort al bij het app-account van {a.emailInUseBy}. Eén account hoort bij één lid; goedkeuren kan pas met een
              eigen e-mailadres. Vraag de aanvrager om opnieuw aan te melden met een ander adres, of wijs de aanmelding af.
            </p>
          </div>
        </div>
      ) : null}
      <ProblemAlert error={startReview.error ?? approve.error ?? reject.error ?? retry.error ?? saveNotes.error} />

      <div className="columns">
        <div className="stack">
          <section className="card" aria-labelledby="gegevens">
            <h2 id="gegevens">Gegevens{a.minor ? ' (lid onder de 16, via de ouder)' : ''}</h2>
            <dl className="details">
              {rows.map(([label, value]) => (
                <Fragment key={label}>
                  <dt>{label}</dt>
                  <dd>{value || '—'}</dd>
                </Fragment>
              ))}
            </dl>
          </section>
          <section className="card" aria-labelledby="contributie">
            <h2 id="contributie">Contributie en toestemming</h2>
            <dl className="details">
                <dt>IBAN</dt>
                <dd>
                  {a.splitFrom ? (
                    <span className="muted">Niet nodig: het hoofdlid betaalt de combinatie</span>
                  ) : (
                    (a.ibanMasked ?? <span className="muted">Gewist (doorgegeven aan e-Boekhouden of afgewezen)</span>)
                  )}
                </dd>
                <dt>Rekeninghouder</dt>
                <dd>{a.accountHolder ?? '—'}</dd>
                <dt>Machtiging</dt>
                <dd>
                  Doorlopend, kenmerk <code>{a.mandateReference}</code>, gegeven {formatDateTime(a.mandateConsentAt)}
                </dd>
                <dt>Privacy</dt>
                <dd>Akkoord {formatDateTime(a.consentPrivacyAt)}</dd>
                <dt>Foto&apos;s</dt>
                <dd>{a.consentPhoto ? 'Mogen worden getoond' : 'Geen toestemming'}</dd>
            </dl>
          </section>
        </div>

        <div className="stack">
          {a.status === 'Rejected' ? (
            <section className="card" aria-labelledby="afgewezen">
              <h2 id="afgewezen">Afgewezen</h2>
              <p>{a.rejectionReason}</p>
              <p className="muted">{formatDateTime(a.decisionAt)}</p>
            </section>
          ) : null}
          {a.provisioning || a.status === 'Approved' ? (
            <section className="card" aria-labelledby="verwerking">
              <h2 id="verwerking">Verwerking</h2>
              <p>
                {a.status === 'Activated' ? (
                  <>
                    Lid geworden
                    {a.provisioning?.memberNumber ? ` met lidnummer ${a.provisioning.memberNumber}` : ''}.{' '}
                    {a.resultingMemberId ? (
                      <Link to="/leden/$id" params={{ id: a.resultingMemberId }}>
                        Naar het lid →
                      </Link>
                    ) : null}
                  </>
                ) : (
                  (provisioningStepLabels[a.provisioning?.step ?? 'Pending'] ?? a.provisioning?.step)
                )}
              </p>
              {a.provisioning?.memberNumber?.startsWith('SIM') ? (
                <p className="muted small-text">Gesimuleerd lidnummer: in deze omgeving schrijft de app niet naar e-Boekhouden.</p>
              ) : null}
              {a.status === 'ProvisioningFailed' ? (
                <>
                  <p className="muted small-text">{a.provisioning?.lastError}</p>
                  <button type="button" className="button secondary" disabled={retry.isPending} onClick={() => retry.mutate(undefined)}>
                    Opnieuw proberen
                  </button>
                </>
              ) : null}
            </section>
          ) : null}
          <section className="card" aria-labelledby="notities">
            <h2 id="notities">Interne notities</h2>
            <form
              onSubmit={(e) => {
                e.preventDefault();
                saveNotes.mutate(undefined, { onSuccess: () => setMessage('Notities opgeslagen.') });
              }}
            >
              <div className="field">
                <label htmlFor="notities-tekst">Alleen zichtbaar voor het bestuur</label>
                <textarea id="notities-tekst" rows={4} maxLength={2000} value={notes} onChange={(e) => setNotes(e.target.value)} />
              </div>
              <button type="submit" className="button secondary" disabled={saveNotes.isPending}>
                Notities opslaan
              </button>
            </form>
          </section>
        </div>
      </div>

      <ConfirmDialog
        open={confirmApprove}
        title="Aanmelding goedkeuren"
        message={`${a.fullName} wordt lid: het systeem maakt het lid aan in e-Boekhouden (met de machtiging), zet het klaar in de app en stuurt ${a.minor ? 'de ouder' : 'het lid'} een welkomstmail. Daarna worden de bankgegevens hier gewist.`}
        confirmLabel="Goedkeuren"
        busy={approve.isPending}
        onCancel={() => setConfirmApprove(false)}
        onConfirm={() =>
          approve.mutate(undefined, {
            onSettled: () => setConfirmApprove(false),
            onSuccess: () => setMessage('Goedgekeurd; het lid wordt nu aangemaakt.'),
          })
        }
      />
      <Dialog open={rejecting} title="Aanmelding afwijzen" onClose={() => setRejecting(false)}>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            reject.mutate(undefined, {
              onSuccess: () => {
                setRejecting(false);
                setMessage('De aanmelding is afgewezen; de bankgegevens zijn gewist.');
              },
            });
          }}
        >
          <p>De aanvrager krijgt hiervan (nog) geen automatisch bericht; neem zo nodig zelf contact op.</p>
          <Field label="Reden" required minLength={3} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
          <div className="actions">
            <button type="button" className="button secondary" onClick={() => setRejecting(false)}>
              Annuleren
            </button>
            <button type="submit" className="button danger" disabled={reject.isPending}>
              Afwijzen
            </button>
          </div>
        </form>
      </Dialog>
    </>
  );
}
