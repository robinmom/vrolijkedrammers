import { Link } from '@tanstack/react-router';
import { useMemo, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, useRoles, useUsers, type UserSummary } from '../api/hooks';
import { DataTable, columnHelper } from '../components/DataTable';
import { Dialog } from '../components/Dialog';
import { Checkbox, Field } from '../components/Field';
import { Pagination } from '../components/Pagination';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { accountStatusLabels, formatDateTime } from '../format';

const helper = columnHelper<UserSummary>();
const columns = [
  helper.accessor('displayName', {
    header: 'Naam',
    cell: (info) => (
      <Link to="/gebruikers/$id" params={{ id: info.row.original.id }}>
        {info.getValue()}
      </Link>
    ),
  }),
  helper.accessor('email', { header: 'E-mailadres' }),
  helper.accessor('accountStatus', { header: 'Status', cell: (info) => accountStatusLabels[info.getValue()] ?? info.getValue() }),
  helper.accessor('lastLoginAt', { header: 'Laatste login', cell: (info) => formatDateTime(info.getValue()) }),
];

export function UsersPage() {
  const api = useApi();
  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const users = useUsers(query, page);
  const [exportError, setExportError] = useState<unknown>(null);

  async function exportExcel() {
    setExportError(null);
    try {
      const { data } = await api.GET('/api/v1/admin/users/export', {
        params: { query: { search: query || undefined } },
        parseAs: 'blob',
      });
      if (data) {
        const url = URL.createObjectURL(data);
        const link = document.createElement('a');
        link.href = url;
        link.download = 'gebruikers.xlsx';
        link.click();
        URL.revokeObjectURL(url);
      }
    } catch (error) {
      setExportError(error);
    }
  }

  return (
    <>
      <div className="page-header">
        <h1>Gebruikers</h1>
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => void exportExcel()}>
            Exporteren (Excel)
          </button>
          <button type="button" className="button" onClick={() => setCreating(true)}>
            Beheerder toevoegen
          </button>
        </div>
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={exportError ?? users.error} />
      <form
        role="search"
        className="toolbar"
        onSubmit={(e) => {
          e.preventDefault();
          setPage(1);
          setQuery(search);
        }}
      >
        <Field label="Zoeken op naam of e-mail" value={search} onChange={(e) => setSearch(e.target.value)} />
        <button type="submit" className="button secondary">
          Zoeken
        </button>
      </form>
      <DataTable caption="Gebruikers" columns={columns} data={users.data?.items ?? EMPTY} />
      {users.data ? (
        <Pagination page={users.data.page} pageSize={users.data.pageSize} totalCount={users.data.totalCount} onPage={setPage} />
      ) : null}
      <CreateAdminDialog
        open={creating}
        onClose={() => setCreating(false)}
        onCreated={(email) => {
          setCreating(false);
          setMessage(`${email} is toegevoegd.`);
        }}
      />
    </>
  );
}

const EMPTY: UserSummary[] = [];

function CreateAdminDialog({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (email: string) => void }) {
  const api = useApi();
  const roles = useRoles();
  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [selected, setSelected] = useState<string[]>([]);
  const adminRoles = useMemo(() => (roles.data ?? []).filter((r) => !['lid', 'ouder'].includes(r.code)), [roles.data]);
  const create = useApiMutation(
    () => api.POST('/api/v1/admin/users', { body: { email, displayName, roles: selected } }),
    [['users']],
  );

  function submit(event: FormEvent) {
    event.preventDefault();
    create.mutate(undefined, { onSuccess: () => onCreated(email) });
  }

  return (
    <Dialog open={open} title="Beheerder toevoegen" onClose={onClose}>
      <form onSubmit={submit}>
        <p className="muted">
          Bestaat er al een account met dit e-mailadres, dan wordt dat gekoppeld. Anders wordt er een account aangemaakt; de
          beheerder logt in met een eenmalige code per e-mail.
        </p>
        <Field label="E-mailadres" type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
        <Field label="Naam" required value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
        <fieldset>
          <legend>Rollen</legend>
          {adminRoles.map((role) => (
            <Checkbox
              key={role.code}
              label={role.name}
              checked={selected.includes(role.code)}
              onChange={(e) => setSelected((s) => (e.target.checked ? [...s, role.code] : s.filter((c) => c !== role.code)))}
            />
          ))}
        </fieldset>
        <ProblemAlert error={create.error} />
        <div className="actions">
          <button type="button" className="button secondary" onClick={onClose}>
            Annuleren
          </button>
          <button type="submit" className="button" disabled={create.isPending || selected.length === 0}>
            Toevoegen
          </button>
        </div>
      </form>
    </Dialog>
  );
}
