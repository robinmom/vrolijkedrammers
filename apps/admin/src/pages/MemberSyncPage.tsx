import { Link, useParams } from '@tanstack/react-router';
import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import {
  useApiMutation,
  useFeatureFlags,
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
import { Icon } from '../components/Icon';
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
  const kind = status === 'Succeeded' ? 'ok' : status === 'Queued' || status === 'Running' ? 'info' : status === 'Failed' ? 'error' : 'warn';
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

  const running = jobs.data?.items.find((j) => j.status === 'Queued' || j.status === 'Running');
  const lastDone = jobs.data?.items.find((j) => j.status !== 'Queued' && j.status !== 'Running');

  return (
    <>
      <Link to="/leden" className="back-link">
        <Icon name="terug" size={16} /> Leden
      </Link>
      <div className="page-header">
        <div className="page-title">
          <h1>Synchronisatie met e-Boekhouden</h1>
          <p className="page-subtitle">
            Leest de leden uit e-Boekhouden en schrijft daar nooit iets terug. Een dry-run laat zien wat er zou veranderen,
            zonder iets op te slaan; doe die altijd eerst na een wijziging van de mapping.
          </p>
        </div>
        <div className="actions">
          <button type="button" className="button secondary" disabled={busy || start.isPending} onClick={() => run(true)}>
            Dry-run starten
          </button>
          <button type="button" className="button" disabled={busy || start.isPending} onClick={() => setConfirmReal(true)}>
            Synchroniseren
          </button>
        </div>
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={start.error ?? jobs.error} />

      <SyncStatus lastDone={lastDone} canConfigure={canConfigure} />
      {running ? <RunningJob job={running} /> : null}

      <OpenConflicts />

      <DataTable
        caption="Syncruns"
        columns={jobColumns}
        data={jobs.data?.items ?? EMPTY_JOBS}
        columnPicker={false}
        header={<h2 id="runs">Runs</h2>}
        footer={
          jobs.data && jobs.data.totalCount > jobs.data.pageSize ? (
            <Pagination page={jobs.data.page} pageSize={jobs.data.pageSize} totalCount={jobs.data.totalCount} onPage={setPage} noun="runs" />
          ) : null
        }
      />

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

/** Drie statuskaarten (Figma "Ledensync"): laatste run, nachtelijke sync, koppeling. */
function SyncStatus({ lastDone, canConfigure }: { lastDone: SyncJob | undefined; canConfigure: boolean }) {
  const flags = useFeatureFlags(canConfigure);
  const nightly = flags.data?.find((f) => f.key === 'members-sync');
  const tokenProblem = lastDone?.status === 'Failed' && /token|geconfigureerd|Key Vault|Aanmelden/i.test(lastDone.errorMessage ?? '');
  return (
    <div className="grid-3 status-cards">
      <section className="card stat-card" aria-label="Laatste run">
        <p className="kpi-label">Laatste run</p>
        {lastDone ? (
          <>
            <p>
              <StatusBadge status={lastDone.status} />
            </p>
            <p className="kpi-hint">
              {formatDateTime(lastDone.completedAt ?? lastDone.requestedAt)} · {lastDone.dryRun ? 'dry-run' : 'echte run'} ·{' '}
              {summary(lastDone)}
            </p>
          </>
        ) : (
          <p className="kpi-hint">Nog geen run gedaan.</p>
        )}
      </section>
      <section className="card stat-card" aria-label="Nachtelijke sync">
        <p className="kpi-label">Nachtelijke sync</p>
        {canConfigure ? (
          <>
            <p>
              <span className={`badge ${nightly?.enabled ? 'info' : ''}`}>{nightly?.enabled ? 'Aan' : 'Uit'}</span>
            </p>
            <p className="kpi-hint">
              Elke nacht om 03:00 zolang de feature flag <code>members-sync</code> aan staat (Configuratie).
            </p>
          </>
        ) : (
          <p className="kpi-hint">Elke nacht om 03:00 als de beheerder die aanzet.</p>
        )}
      </section>
      <section className="card stat-card" aria-label="Koppeling">
        <p className="kpi-label">Koppeling met e-Boekhouden</p>
        <p>
          {tokenProblem ? (
            <span className="badge error">Niet verbonden</span>
          ) : lastDone && lastDone.status !== 'Failed' ? (
            <span className="badge ok">Verbonden</span>
          ) : (
            <span className="badge">Nog niet getest</span>
          )}
        </p>
        <p className="kpi-hint">{tokenProblem ? lastDone?.errorMessage : 'Token in Key Vault · alleen lezen'}</p>
      </section>
    </div>
  );
}

/** Voortgang van een run die in de wachtrij staat of loopt; ververst vanzelf (useSyncJobs). */
function RunningJob({ job }: { job: SyncJob }) {
  const processed = job.created + job.updated + job.unchanged + job.reactivated + job.errors + job.conflicts;
  const percent = job.totalInSource ? Math.min(100, Math.round((processed / job.totalInSource) * 100)) : 0;
  return (
    <section className="card" aria-labelledby="lopende-run">
      <div className="card-header">
        <h2 id="lopende-run">{job.dryRun ? 'Dry-run bezig' : 'Synchronisatie bezig'}</h2>
        <StatusBadge status={job.status} />
        <span className="kpi-label">{job.totalInSource ? `${processed} van ${job.totalInSource} leden` : 'Leden ophalen…'}</span>
      </div>
      <div
        className="progress"
        role="progressbar"
        aria-label="Voortgang"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={percent}
      >
        <span style={{ width: `${percent}%` }} />
      </div>
      <p className="kpi-hint">
        Gestart om {formatDateTime(job.requestedAt)}
        {job.dryRun ? ' · er wordt niets opgeslagen (dry-run)' : ''} · de pagina ververst vanzelf
      </p>
    </section>
  );
}

function OpenConflicts() {
  const conflicts = useSyncConflicts();
  const [resolving, setResolving] = useState<SyncConflict | null>(null);
  if (!conflicts.data?.length) {
    return null;
  }
  return (
    <section className="card accent-gold" aria-labelledby="conflicten">
      <h2 id="conflicten">Openstaande conflicten ({conflicts.data.length})</h2>
      <ul className="list">
        {conflicts.data.map((c) => (
          <li key={c.id} className="list-row">
            <div className="grow">
              <p>
                <span className="badge warn">{syncConflictTypeLabels[c.type] ?? c.type}</span>{' '}
                {c.memberNumber ? (
                  c.memberId ? (
                    <Link to="/leden/$id" params={{ id: c.memberId }}>
                      Lidnummer {c.memberNumber}
                    </Link>
                  ) : (
                    <>Lidnummer {c.memberNumber}</>
                  )
                ) : null}
              </p>
              <p className="muted">{c.details}</p>
            </div>
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
        <div className="grid-4">
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
