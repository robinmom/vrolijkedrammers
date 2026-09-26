import { Link } from '@tanstack/react-router';
import { useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import {
  useApiMutation,
  useMe,
  useMembers,
  useMemberSummary,
  type MemberFilters,
  type MemberPurgeResult,
  type MemberSummary,
  type MemberSyncState,
  type MembershipStatus,
} from '../api/hooks';
import { DataTable, columnHelper } from '../components/DataTable';
import { Dialog } from '../components/Dialog';
import { Field } from '../components/Field';
import { Icon } from '../components/Icon';
import { Pagination } from '../components/Pagination';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDateTime, membershipStatusLabels, syncStateLabels } from '../format';

const statusTone: Record<string, string> = { Active: 'ok', Inactive: '', Suspended: 'warn', Deceased: '' };
const syncTone: Record<string, string> = { InSync: '', Missing: 'warn', Conflict: 'error' };

const helper = columnHelper<MemberSummary>();
const columns = [
  helper.accessor('memberNumber', { header: 'Lidnummer', cell: (info) => <span className="muted">{info.getValue()}</span> }),
  helper.accessor('fullName', {
    header: 'Naam',
    cell: (info) => (
      <>
        <Link to="/leden/$id" params={{ id: info.row.original.id }} className="cell-title">
          {info.getValue()}
        </Link>
        {info.row.original.email ? <span className="cell-sub">{info.row.original.email}</span> : null}
      </>
    ),
  }),
  helper.accessor('city', { header: 'Plaats', cell: (info) => info.getValue() ?? '—' }),
  helper.accessor('status', {
    header: 'Status',
    cell: (info) => <span className={`badge ${statusTone[info.getValue()] ?? ''}`}>{membershipStatusLabels[info.getValue()] ?? info.getValue()}</span>,
  }),
  helper.accessor('syncState', {
    header: 'Sync',
    cell: (info) => <span className={`badge ${syncTone[info.getValue()] ?? ''}`}>{syncStateLabels[info.getValue()] ?? info.getValue()}</span>,
  }),
  helper.accessor('hasAccount', { header: 'App-account', cell: (info) => <span className="muted">{info.getValue() ? 'Ja' : 'Nee'}</span> }),
];

const EMPTY: MemberSummary[] = [];
const NO_FILTERS: MemberFilters = { search: '', status: '', syncState: '' };

/** Ledenlijst uit e-Boekhouden (Figma "Leden"): kengetallen, zoeken en filteren, exporteren, gevarenzone (alleen Dev/Acc). */
export function MembersPage() {
  const api = useApi();
  const me = useMe();
  const summary = useMemberSummary();
  const [form, setForm] = useState<MemberFilters>(NO_FILTERS);
  const [filters, setFilters] = useState<MemberFilters>(NO_FILTERS);
  const [page, setPage] = useState(1);
  const [message, setMessage] = useState<string | null>(null);
  const [exportError, setExportError] = useState<unknown>(null);
  const [purging, setPurging] = useState(false);
  const members = useMembers(filters, page);
  const permissions = me.data?.permissions ?? [];
  const s = summary.data;

  async function exportExcel() {
    setExportError(null);
    try {
      const { data } = await api.GET('/api/v1/admin/members/export', {
        params: {
          query: { search: filters.search || undefined, status: filters.status || undefined, syncState: filters.syncState || undefined },
        },
        parseAs: 'blob',
      });
      if (data) {
        const url = URL.createObjectURL(data);
        const link = document.createElement('a');
        link.href = url;
        link.download = 'leden.xlsx';
        link.click();
        URL.revokeObjectURL(url);
      }
    } catch (error) {
      setExportError(error);
    }
  }

  function search(event: FormEvent) {
    event.preventDefault();
    setPage(1);
    setFilters(form);
  }

  const withAccountShare = s && s.active > 0 ? Math.round((s.activeWithAccount / s.active) * 100) : 0;

  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>Leden</h1>
          <p className="page-subtitle">
            Uit e-Boekhouden{s?.lastSyncAt ? ` · laatste synchronisatie ${formatDateTime(s.lastSyncAt)}` : ''}. Naam, adres en
            contactgegevens wijzig je in e-Boekhouden.
          </p>
        </div>
        <div className="actions">
          {permissions.includes('member.export') ? (
            <button type="button" className="button secondary" onClick={() => void exportExcel()}>
              Exporteren (Excel)
            </button>
          ) : null}
          {permissions.includes('import.run') ? (
            <Link to="/ledensync" className="button">
              Synchronisatie
            </Link>
          ) : null}
        </div>
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={exportError ?? members.error ?? summary.error} />

      {s ? (
        <section className="kpis" aria-label="Kengetallen">
          <div className="kpi">
            <span className="kpi-label">Actieve leden</span>
            <span className="kpi-value">{s.active}</span>
            <span className="kpi-hint">{s.suspended ? `${s.suspended} geschorst` : 'Volgens e-Boekhouden'}</span>
          </div>
          <div className="kpi">
            <span className="kpi-label">Inactief</span>
            <span className="kpi-value">{s.inactive + s.deceased}</span>
            <span className="kpi-hint">Opgezegd of gestopt</span>
          </div>
          <div className="kpi">
            <span className="kpi-label">Ontbreekt in e-Boekhouden</span>
            <span className="kpi-value">{s.missingInEBoekhouden}</span>
            <span className="kpi-hint">{s.missingInEBoekhouden ? 'Controleren' : 'Alles in orde'}</span>
          </div>
          <div className="kpi">
            <span className="kpi-label">Met app-account</span>
            <span className="kpi-value">{s.activeWithAccount}</span>
            <span className="kpi-hint">{withAccountShare} % van de actieve leden</span>
          </div>
        </section>
      ) : null}

      <DataTable
        caption="Leden"
        columns={columns}
        data={members.data?.items ?? EMPTY}
        columnPicker={false}
        header={
          <form role="search" className="toolbar" onSubmit={search}>
            <div className="field grow-field">
              <label htmlFor="zoeken">Zoeken op naam, lidnummer, e-mail of plaats</label>
              <input id="zoeken" value={form.search} onChange={(e) => setForm({ ...form, search: e.target.value })} />
            </div>
            <div className="field">
              <label htmlFor="status-filter">Status</label>
              <select id="status-filter" value={form.status} onChange={(e) => setForm({ ...form, status: e.target.value as MembershipStatus | '' })}>
                <option value="">Alle</option>
                {Object.entries(membershipStatusLabels).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </div>
            <div className="field">
              <label htmlFor="sync-filter">Sync</label>
              <select id="sync-filter" value={form.syncState} onChange={(e) => setForm({ ...form, syncState: e.target.value as MemberSyncState | '' })}>
                <option value="">Alle</option>
                {Object.entries(syncStateLabels).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </div>
            <button type="submit" className="button secondary">
              Zoeken
            </button>
          </form>
        }
        footer={
          members.data ? (
            <Pagination page={members.data.page} pageSize={members.data.pageSize} totalCount={members.data.totalCount} onPage={setPage} noun="leden" />
          ) : null
        }
      />

      {permissions.includes('member.purge') ? (
        <section className="danger-zone" aria-labelledby="gevarenzone">
          <span className="icon-bubble red">
            <Icon name="waarschuwing" size={22} />
          </span>
          <div className="grow">
            <h2 id="gevarenzone">
              Gevarenzone <span className="badge">Alleen test- en acceptatieomgeving</span>
            </h2>
            <p>
              Verwijdert alle leden, syncruns en conflicten uit de app en zet de nachtelijke sync uit. In e-Boekhouden verandert
              niets; met een synchronisatie zijn de leden er zo weer.
            </p>
          </div>
          <button type="button" className="button danger" onClick={() => setPurging(true)}>
            Alle leden verwijderen
          </button>
        </section>
      ) : null}

      <PurgeDialog
        open={purging}
        onClose={() => setPurging(false)}
        onDone={(text) => {
          setPurging(false);
          setMessage(text);
        }}
      />
    </>
  );
}

const CONFIRMATION = 'LEDEN VERWIJDEREN';

/** Verwijdert alle leden uit de omgeving; vraagt een getypte bevestiging (alleen Dev/Acc, de API dwingt dat af). */
function PurgeDialog({
  open,
  onClose,
  onDone,
}: {
  open: boolean;
  onClose: () => void;
  onDone: (message: string) => void;
}) {
  const api = useApi();
  const [typed, setTyped] = useState('');
  const purge = useApiMutation(
    async () => (await api.POST('/api/v1/admin/members/purge', { body: { confirmation: typed } })).data,
    [['members'], ['sync-jobs'], ['sync-conflicts']],
  );

  function submit(event: FormEvent) {
    event.preventDefault();
    purge.mutate(undefined, {
      onSuccess: (data) => {
        const result = data as MemberPurgeResult | undefined;
        setTyped('');
        onDone(
          `${result?.members ?? 0} leden en ${result?.syncJobs ?? 0} syncruns verwijderd; ${result?.unlinkedAccounts ?? 0} app-account(s) ontkoppeld. De nachtelijke sync staat uit.`,
        );
      },
    });
  }

  return (
    <Dialog open={open} title="Alle leden verwijderen" onClose={onClose}>
      <form onSubmit={submit}>
        <p>
          Dit verwijdert <strong>alle leden</strong>, syncruns en conflicten uit deze omgeving en zet de nachtelijke
          sync uit. App-accounts blijven bestaan, maar zijn daarna niet meer aan een lid gekoppeld. In e-Boekhouden
          verandert niets.
        </p>
        <p className="muted">Deze functie werkt alleen in de test- en acceptatieomgeving.</p>
        <Field
          label={`Typ ${CONFIRMATION} ter bevestiging`}
          value={typed}
          autoComplete="off"
          onChange={(e) => setTyped(e.target.value)}
        />
        <ProblemAlert error={purge.error} />
        <div className="actions">
          <button type="button" className="button secondary" onClick={onClose}>
            Annuleren
          </button>
          <button type="submit" className="button danger" disabled={purge.isPending || typed !== CONFIRMATION}>
            Alles verwijderen
          </button>
        </div>
      </form>
    </Dialog>
  );
}
