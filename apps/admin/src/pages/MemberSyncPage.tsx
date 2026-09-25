import { Link, useParams } from '@tanstack/react-router';
import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import {
  useApiMutation,
  useMe,
  useMemberMapping,
  useSyncConflicts,
  useSyncJob,
  useSyncJobItems,
  useSyncJobs,
  type MemberFieldMapping,
  type SyncConflict,
  type SyncItemAction,
  type SyncJob,
  type SyncJobItem,
} from '../api/hooks';
import { DataTable, columnHelper } from '../components/DataTable';
import { Dialog } from '../components/Dialog';
import { Field } from '../components/Field';
import { Pagination } from '../components/Pagination';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import {
  formatChangedFields,
  formatDateTime,
  syncConflictTypeLabels,
  syncItemActionLabels,
  syncJobStatusLabels,
} from '../format';

function StatusBadge({ status }: { status: SyncJob['status'] }) {
  const kind = status === 'Succeeded' ? 'ok' : status === 'Queued' || status === 'Running' ? '' : 'warn';
  return <span className={`badge ${kind}`}>{syncJobStatusLabels[status] ?? status}</span>;
}

const jobHelper = columnHelper<SyncJob>();
const jobColumns = [
  jobHelper.accessor('requestedAt', {
    header: 'Gestart',
    cell: (info) => (
      <Link to="/ledensync/$id" params={{ id: info.row.original.id }}>
        {formatDateTime(info.getValue())}
      </Link>
    ),
  }),
  jobHelper.accessor('dryRun', { header: 'Soort', cell: (info) => (info.getValue() ? 'Dry-run' : 'Echte run') }),
  jobHelper.accessor('trigger', {
    header: 'Door',
    cell: (info) => (info.getValue() === 'Scheduled' ? 'Nachtelijk' : 'Handmatig'),
  }),
  jobHelper.accessor('status', { header: 'Status', cell: (info) => <StatusBadge status={info.getValue()} /> }),
  jobHelper.display({
    id: 'counts',
    header: 'Resultaat',
    cell: (info) => summary(info.row.original),
  }),
];

function summary(j: SyncJob): string {
  if (j.status === 'Failed') {
    return j.errorMessage ?? 'Mislukt';
  }
  if (j.status === 'Queued' || j.status === 'Running') {
    return j.totalInSource ? `${j.totalInSource} leden in e-Boekhouden…` : '…';
  }
  const parts = [
    `${j.created} nieuw`,
    `${j.updated} gewijzigd`,
    `${j.unchanged} ongewijzigd`,
    j.missing ? `${j.missing} ontbreken` : '',
    j.deactivated ? `${j.deactivated} inactief` : '',
    j.reactivated ? `${j.reactivated} teruggekeerd` : '',
    j.warnings ? `${j.warnings} waarschuwingen` : '',
    j.errors ? `${j.errors} fouten` : '',
    j.conflicts ? `${j.conflicts} conflicten` : '',
  ];
  return parts.filter(Boolean).join(' · ');
}

const EMPTY_JOBS: SyncJob[] = [];

/** Ledensync met e-Boekhouden (fase 8): runs starten en volgen, conflicten, mapping van de vrije velden. */
export function MemberSyncPage() {
  const api = useApi();
  const me = useMe();
  const [page, setPage] = useState(1);
  const [message, setMessage] = useState<string | null>(null);
  const [confirmReal, setConfirmReal] = useState(false);
  const jobs = useSyncJobs(page);
  const busy = jobs.data?.items.some((j) => j.status === 'Queued' || j.status === 'Running') ?? false;
  const canConfigure = (me.data?.permissions ?? []).includes('config.manage');
  const start = useApiMutation(
    (dryRun: boolean) => api.POST('/api/v1/admin/members/import', { params: { query: { dryRun } } }),
    [['sync-jobs']],
  );

  function run(dryRun: boolean) {
    setMessage(null);
    start.mutate(dryRun, {
      onSuccess: () => {
        setConfirmReal(false);
        setMessage(dryRun ? 'Dry-run gestart. Het rapport verschijnt hieronder.' : 'Synchronisatie gestart.');
      },
    });
  }

  return (
    <>
      <p>
        <Link to="/leden">← Leden</Link>
      </p>
      <div className="page-header">
        <h1>Synchronisatie met e-Boekhouden</h1>
        <div className="actions">
          <button
            type="button"
            className="button secondary"
            disabled={busy || start.isPending}
            onClick={() => run(true)}
          >
            Dry-run starten
          </button>
          <button
            type="button"
            className="button"
            disabled={busy || start.isPending}
            onClick={() => setConfirmReal(true)}
          >
            Synchroniseren
          </button>
        </div>
      </div>
      <p className="muted">
        De sync leest de leden uit e-Boekhouden en schrijft daar niets terug. Een dry-run laat zien wat er zou
        veranderen, zonder iets op te slaan. Doe na een wijziging van de mapping altijd eerst een dry-run.
      </p>
      <SuccessMessage message={message} />
      <ProblemAlert error={start.error ?? jobs.error} />

      <OpenConflicts />

      <section className="card" aria-labelledby="runs">
        <h2 id="runs">Runs</h2>
        <DataTable caption="Syncruns" columns={jobColumns} data={jobs.data?.items ?? EMPTY_JOBS} />
        {jobs.data ? (
          <Pagination
            page={jobs.data.page}
            pageSize={jobs.data.pageSize}
            totalCount={jobs.data.totalCount}
            onPage={setPage}
          />
        ) : null}
      </section>

      {canConfigure ? <MappingForm /> : null}

      <Dialog open={confirmReal} title="Synchroniseren" onClose={() => setConfirmReal(false)}>
        <p>
          De leden in de app worden bijgewerkt volgens e-Boekhouden. Leden die daar ontbreken, worden eerst gemarkeerd
          en pas bij de volgende run inactief. Ontbreekt meer dan 10 %, dan wordt niemand gedeactiveerd.
        </p>
        <ProblemAlert error={start.error} />
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => setConfirmReal(false)}>
            Annuleren
          </button>
          <button type="button" className="button" disabled={start.isPending} onClick={() => run(false)}>
            Nu synchroniseren
          </button>
        </div>
      </Dialog>
    </>
  );
}

function OpenConflicts() {
  const conflicts = useSyncConflicts();
  const [resolving, setResolving] = useState<SyncConflict | null>(null);
  if (!conflicts.data?.length) {
    return null;
  }
  return (
    <section className="card" aria-labelledby="conflicten">
      <h2 id="conflicten">Openstaande conflicten ({conflicts.data.length})</h2>
      <ul className="list">
        {conflicts.data.map((c) => (
          <li key={c.id}>
            <strong>{syncConflictTypeLabels[c.type] ?? c.type}</strong>
            {c.memberNumber ? (
              <>
                {' '}
                · lidnummer{' '}
                {c.memberId ? (
                  <Link to="/leden/$id" params={{ id: c.memberId }}>
                    {c.memberNumber}
                  </Link>
                ) : (
                  c.memberNumber
                )}
              </>
            ) : null}
            <p>{c.details}</p>
            <button type="button" className="button secondary" onClick={() => setResolving(c)}>
              Afhandelen
            </button>
          </li>
        ))}
      </ul>
      <ResolveDialog conflict={resolving} onClose={() => setResolving(null)} />
    </section>
  );
}

function ResolveDialog({ conflict, onClose }: { conflict: SyncConflict | null; onClose: () => void }) {
  const api = useApi();
  const [note, setNote] = useState('');
  const resolve = useApiMutation(
    (resolution: 'Accepted' | 'Ignored') =>
      api.POST('/api/v1/admin/sync-conflicts/{id}/resolve', {
        params: { path: { id: conflict?.id ?? '' } },
        body: { resolution, note: note || null },
      }),
    [['sync-conflicts']],
  );

  function done(resolution: 'Accepted' | 'Ignored') {
    resolve.mutate(resolution, {
      onSuccess: () => {
        setNote('');
        onClose();
      },
    });
  }

  return (
    <Dialog open={conflict !== null} title="Conflict afhandelen" onClose={onClose}>
      <p>{conflict?.details}</p>
      <p className="muted">
        <strong>Geaccepteerd</strong>: de situatie klopt (bijv. na correctie in e-Boekhouden).{' '}
        <strong>Genegeerd</strong>: geen actie nodig.
      </p>
      <Field label="Notitie (optioneel)" value={note} onChange={(e) => setNote(e.target.value)} />
      <ProblemAlert error={resolve.error} />
      <div className="actions">
        <button type="button" className="button secondary" onClick={onClose}>
          Annuleren
        </button>
        <button type="button" className="button secondary" disabled={resolve.isPending} onClick={() => done('Ignored')}>
          Negeren
        </button>
        <button type="button" className="button" disabled={resolve.isPending} onClick={() => done('Accepted')}>
          Accepteren
        </button>
      </div>
    </Dialog>
  );
}

const FREE_TEXT = Array.from({ length: 10 }, (_, i) => `freeText${i + 1}`);

const mappingFields: { key: 'birthDate' | 'joinYear' | 'status' | 'category'; label: string; hint: string }[] = [
  { key: 'birthDate', label: 'Geboortedatum', hint: 'Formaat JJJJ-MM-DD (DD-MM-JJJJ wordt ook herkend)' },
  { key: 'joinYear', label: 'Inschrijfjaar', hint: 'Jaartal, bijv. 1995' },
  { key: 'status', label: 'Lidmaatschapsstatus', hint: 'Bijv. actief of opgezegd' },
  { key: 'category', label: 'Categorie', hint: 'Bijv. jeugdlid of gezinslid' },
];

/** Welk vrij veld in e-Boekhouden welk gegeven bevat (B-06); alleen met config.manage. */
function MappingForm() {
  const api = useApi();
  const mapping = useMemberMapping(true);
  const [form, setForm] = useState<MemberFieldMapping | null>(null);
  const [inactive, setInactive] = useState('');
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    if (mapping.data) {
      setForm(mapping.data);
      setInactive(mapping.data.inactiveStatusValues.join(', '));
    }
  }, [mapping.data]);

  const save = useApiMutation(
    () =>
      api.PUT('/api/v1/admin/config/member-mapping', {
        body: {
          ...(form as MemberFieldMapping),
          inactiveStatusValues: inactive
            .split(',')
            .map((v) => v.trim())
            .filter(Boolean),
        },
      }),
    [['member-mapping']],
  );

  if (!form) {
    return mapping.error ? <ProblemAlert error={mapping.error} /> : null;
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate(undefined, { onSuccess: () => setMessage('Mapping opgeslagen. Start nu eerst een dry-run.') });
  }

  return (
    <section className="card" aria-labelledby="mapping">
      <h2 id="mapping">Vrije velden in e-Boekhouden</h2>
      <p className="muted">
        e-Boekhouden kent geen velden voor geboortedatum, inschrijfjaar en status; die staan in vrije velden. Kies per
        gegeven het vrije veld, of laat het leeg om het gegeven in de app zelf te beheren.
      </p>
      <form onSubmit={submit}>
        <div className="grid-2">
          {mappingFields.map((f) => (
            <div className="field" key={f.key}>
              <label htmlFor={`map-${f.key}`}>{f.label}</label>
              <select
                id={`map-${f.key}`}
                aria-describedby={`map-${f.key}-hint`}
                value={form[f.key] ?? ''}
                onChange={(e) => setForm({ ...form, [f.key]: e.target.value || null })}
              >
                <option value="">Niet gemapt (beheerd in de app)</option>
                {FREE_TEXT.map((field, i) => (
                  <option key={field} value={field}>
                    Vrij veld {i + 1}
                  </option>
                ))}
              </select>
              <small id={`map-${f.key}-hint`} className="muted">
                {f.hint}
              </small>
            </div>
          ))}
        </div>
        <Field
          label="Statuswaarden die 'niet meer actief' betekenen"
          hint="Kommagescheiden, hoofdletters maken niet uit"
          value={inactive}
          onChange={(e) => setInactive(e.target.value)}
        />
        <SuccessMessage message={message} />
        <ProblemAlert error={save.error} />
        <div className="actions">
          <button type="submit" className="button" disabled={save.isPending}>
            Mapping opslaan
          </button>
        </div>
      </form>
    </section>
  );
}

const itemHelper = columnHelper<SyncJobItem>();
const itemColumns = [
  itemHelper.accessor('memberNumber', {
    header: 'Lidnummer',
    cell: (info) =>
      info.row.original.memberId ? (
        <Link to="/leden/$id" params={{ id: info.row.original.memberId }}>
          {info.getValue()}
        </Link>
      ) : (
        info.getValue()
      ),
  }),
  itemHelper.accessor('action', {
    header: 'Resultaat',
    cell: (info) => syncItemActionLabels[info.getValue()] ?? info.getValue(),
  }),
  itemHelper.accessor('changedFields', { header: 'Gewijzigd', cell: (info) => formatChangedFields(info.getValue()) }),
  itemHelper.accessor('message', { header: 'Toelichting', cell: (info) => info.getValue() ?? '' }),
];

const EMPTY_ITEMS: SyncJobItem[] = [];

/** Rapport van één run, met filter op resultaat. */
export function SyncJobPage() {
  const { id } = useParams({ from: '/ledensync/$id' });
  const [action, setAction] = useState<SyncItemAction | ''>('');
  const [page, setPage] = useState(1);
  const job = useSyncJob(id);
  const items = useSyncJobItems(id, action, page);

  if (!job.data) {
    return <>{job.error ? <ProblemAlert error={job.error} /> : <p>Laden…</p>}</>;
  }

  const j = job.data;
  return (
    <>
      <p>
        <Link to="/ledensync">← Synchronisatie</Link>
      </p>
      <h1>
        {j.dryRun ? 'Dry-run' : 'Synchronisatie'} van {formatDateTime(j.requestedAt)}
      </h1>
      <p>
        <StatusBadge status={j.status} /> {summary(j)}
      </p>
      {j.dryRun && j.status !== 'Queued' && j.status !== 'Running' ? (
        <p className="muted">
          Dit is een dry-run: er is niets opgeslagen. Klopt het rapport, start dan een echte synchronisatie.
        </p>
      ) : null}
      <div className="toolbar">
        <div className="field">
          <label htmlFor="action-filter">Toon</label>
          <select
            id="action-filter"
            value={action}
            onChange={(e) => {
              setPage(1);
              setAction(e.target.value as SyncItemAction | '');
            }}
          >
            <option value="">Alles behalve ongewijzigd</option>
            {Object.entries(syncItemActionLabels).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </div>
      </div>
      <ProblemAlert error={items.error} />
      <DataTable caption="Resultaat per lid" columns={itemColumns} data={items.data?.items ?? EMPTY_ITEMS} />
      {items.data ? (
        <Pagination
          page={items.data.page}
          pageSize={items.data.pageSize}
          totalCount={items.data.totalCount}
          onPage={setPage}
        />
      ) : null}
    </>
  );
}
