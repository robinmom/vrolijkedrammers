import { Link } from '@tanstack/react-router';
import { useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import {
  useApiMutation,
  useMe,
  useMembers,
  type MemberFilters,
  type MemberPurgeResult,
  type MemberSummary,
  type MemberSyncState,
  type MembershipStatus,
} from '../api/hooks';
import { DataTable, columnHelper } from '../components/DataTable';
import { Dialog } from '../components/Dialog';
import { Field } from '../components/Field';
import { Pagination } from '../components/Pagination';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { membershipStatusLabels, syncStateLabels } from '../format';

const helper = columnHelper<MemberSummary>();
const columns = [
  helper.accessor('memberNumber', { header: 'Lidnummer' }),
  helper.accessor('fullName', {
    header: 'Naam',
    cell: (info) => (
      <Link to="/leden/$id" params={{ id: info.row.original.id }}>
        {info.getValue()}
      </Link>
    ),
  }),
  helper.accessor('city', { header: 'Plaats', cell: (info) => info.getValue() ?? '—' }),
  helper.accessor('status', {
    header: 'Status',
    cell: (info) => membershipStatusLabels[info.getValue()] ?? info.getValue(),
  }),
  helper.accessor('syncState', {
    header: 'Sync',
    cell: (info) =>
      info.getValue() === 'InSync' ? (
        <span className="badge ok">{syncStateLabels.InSync}</span>
      ) : (
        <span className="badge warn">{syncStateLabels[info.getValue()] ?? info.getValue()}</span>
      ),
  }),
  helper.accessor('hasAccount', { header: 'App-account', cell: (info) => (info.getValue() ? 'Ja' : 'Nee') }),
];

const EMPTY: MemberSummary[] = [];
const NO_FILTERS: MemberFilters = { search: '', status: '', syncState: '' };

/** Ledenlijst uit e-Boekhouden (fase 8): zoeken, filteren, exporteren en (alleen Dev/Acc) alles verwijderen. */
export function MembersPage() {
  const api = useApi();
  const me = useMe();
  const [form, setForm] = useState<MemberFilters>(NO_FILTERS);
  const [filters, setFilters] = useState<MemberFilters>(NO_FILTERS);
  const [page, setPage] = useState(1);
  const [message, setMessage] = useState<string | null>(null);
  const [exportError, setExportError] = useState<unknown>(null);
  const [purging, setPurging] = useState(false);
  const members = useMembers(filters, page);
  const permissions = me.data?.permissions ?? [];

  async function exportExcel() {
    setExportError(null);
    try {
      const { data } = await api.GET('/api/v1/admin/members/export', {
        params: {
          query: {
            search: filters.search || undefined,
            status: filters.status || undefined,
            syncState: filters.syncState || undefined,
          },
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

  return (
    <>
      <div className="page-header">
        <h1>Leden</h1>
        <div className="actions">
          {permissions.includes('import.run') ? (
            <Link to="/ledensync" className="button secondary">
              Synchronisatie
            </Link>
          ) : null}
          {permissions.includes('member.export') ? (
            <button type="button" className="button secondary" onClick={() => void exportExcel()}>
              Exporteren (Excel)
            </button>
          ) : null}
          {permissions.includes('member.purge') ? (
            <button type="button" className="button danger" onClick={() => setPurging(true)}>
              Alle leden verwijderen
            </button>
          ) : null}
        </div>
      </div>
      <p className="muted">
        De leden komen uit e-Boekhouden. Naam, adres en contactgegevens wijzig je daar; hier beheer je alleen de
        gegevens van de app.
      </p>
      <SuccessMessage message={message} />
      <ProblemAlert error={exportError ?? members.error} />
      <form role="search" className="toolbar" onSubmit={search}>
        <Field
          label="Zoeken op naam, lidnummer, e-mail of plaats"
          value={form.search}
          onChange={(e) => setForm({ ...form, search: e.target.value })}
        />
        <div className="field">
          <label htmlFor="status-filter">Status</label>
          <select
            id="status-filter"
            value={form.status}
            onChange={(e) => setForm({ ...form, status: e.target.value as MembershipStatus | '' })}
          >
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
          <select
            id="sync-filter"
            value={form.syncState}
            onChange={(e) => setForm({ ...form, syncState: e.target.value as MemberSyncState | '' })}
          >
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
      <DataTable caption="Leden" columns={columns} data={members.data?.items ?? EMPTY} />
      {members.data ? (
        <Pagination
          page={members.data.page}
          pageSize={members.data.pageSize}
          totalCount={members.data.totalCount}
          onPage={setPage}
        />
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
