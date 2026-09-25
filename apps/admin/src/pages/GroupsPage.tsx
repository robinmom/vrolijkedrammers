import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import {
  useApiMutation,
  useGroup,
  useGroups,
  useMe,
  useMembers,
  type GroupFunction,
  type GroupSummary,
  type GroupType,
} from '../api/hooks';
import { DataTable, columnHelper } from '../components/DataTable';
import { ConfirmDialog, Dialog } from '../components/Dialog';
import { Checkbox, Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDate, groupFunctionLabels, groupTypeLabels } from '../format';

const helper = columnHelper<GroupSummary>();
const columns = [
  helper.accessor('name', {
    header: 'Groep',
    cell: (info) => (
      <Link to="/groepen/$id" params={{ id: info.row.original.id }}>
        {info.getValue()}
      </Link>
    ),
  }),
  helper.accessor('type', { header: 'Soort', cell: (info) => groupTypeLabels[info.getValue()] ?? info.getValue() }),
  helper.accessor('memberCount', { header: 'Leden' }),
  helper.accessor('active', { header: 'Actief', cell: (info) => (info.getValue() ? 'Ja' : 'Nee') }),
];

const EMPTY: GroupSummary[] = [];

/** Groepen (fase 8c): doelgroepen die geen rol zijn, zoals Jeugdcommissie of Dansgarde. */
export function GroupsPage() {
  const me = useMe();
  const groups = useGroups();
  const [creating, setCreating] = useState(false);
  const canEdit = (me.data?.permissions ?? []).includes('member.update');
  return (
    <>
      <div className="page-header">
        <h1>Groepen</h1>
        {canEdit ? (
          <div className="actions">
            <button type="button" className="button" onClick={() => setCreating(true)}>
              Groep toevoegen
            </button>
          </div>
        ) : null}
      </div>
      <p className="muted">
        Een groep is een doelgroep voor agenda, nieuws en foto&apos;s (bijvoorbeeld &quot;Jeugdcommissie&quot;). Een groep geeft geen
        rechten; daarvoor zijn de rollen.
      </p>
      <ProblemAlert error={groups.error} />
      <DataTable caption="Groepen" columns={columns} data={groups.data ?? EMPTY} />
      <GroupDialog open={creating} onClose={() => setCreating(false)} />
    </>
  );
}

interface GroupForm {
  name: string;
  description: string;
  type: GroupType;
  active: boolean;
}

function GroupDialog({ open, onClose, id, initial }: { open: boolean; onClose: () => void; id?: string; initial?: GroupForm }) {
  const api = useApi();
  const navigate = useNavigate();
  const [form, setForm] = useState<GroupForm>(initial ?? { name: '', description: '', type: 'Committee', active: true });
  useEffect(() => {
    if (open && initial) {
      setForm(initial);
    }
  }, [open, initial]);
  const body = { name: form.name, description: form.description || null, type: form.type, carnivalYearId: null, active: form.active };
  const save = useApiMutation(
    async () =>
      id
        ? (await api.PUT('/api/v1/admin/groups/{id}', { params: { path: { id } }, body })).data
        : (await api.POST('/api/v1/admin/groups', { body })).data,
    [['groups'], ['audience-groups'], ...(id ? [['group', id]] : [])],
  );

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate(undefined, {
      onSuccess: (data) => {
        onClose();
        const created = (data as { id?: string } | undefined)?.id;
        if (!id && created) {
          void navigate({ to: '/groepen/$id', params: { id: created } });
        }
      },
    });
  }

  return (
    <Dialog open={open} title={id ? 'Groep wijzigen' : 'Groep toevoegen'} onClose={onClose}>
      <form onSubmit={submit}>
        <Field label="Naam" required minLength={2} maxLength={100} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
        <Field label="Omschrijving" value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} />
        <div className="field">
          <label htmlFor="group-type">Soort</label>
          <select id="group-type" value={form.type} onChange={(e) => setForm({ ...form, type: e.target.value as GroupType })}>
            {Object.entries(groupTypeLabels).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </div>
        <Checkbox label="Actief (als doelgroep te kiezen)" checked={form.active} onChange={(e) => setForm({ ...form, active: e.target.checked })} />
        <ProblemAlert error={save.error} />
        <div className="actions">
          <button type="button" className="button secondary" onClick={onClose}>
            Annuleren
          </button>
          <button type="submit" className="button" disabled={save.isPending}>
            Opslaan
          </button>
        </div>
      </form>
    </Dialog>
  );
}

/** Een groep met haar leden; leden toevoegen via zoeken, met functie en optionele geldigheid. */
export function GroupDetailPage() {
  const { id } = useParams({ from: '/groepen/$id' });
  const api = useApi();
  const navigate = useNavigate();
  const me = useMe();
  const group = useGroup(id);
  const [editing, setEditing] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const canEdit = (me.data?.permissions ?? []).includes('member.update');
  const remove = useApiMutation(
    (memberId: string) => api.DELETE('/api/v1/admin/groups/{id}/members/{memberId}', { params: { path: { id, memberId } } }),
    [['group', id], ['groups']],
  );
  const deleteGroup = useApiMutation(() => api.DELETE('/api/v1/admin/groups/{id}', { params: { path: { id } } }), [['groups'], ['audience-groups']]);

  if (!group.data) {
    return <>{group.error ? <ProblemAlert error={group.error} /> : <p>Laden…</p>}</>;
  }
  const g = group.data;

  return (
    <>
      <p>
        <Link to="/groepen">← Groepen</Link>
      </p>
      <div className="page-header">
        <h1>{g.name}</h1>
        {canEdit ? (
          <div className="actions">
            <button type="button" className="button secondary" onClick={() => setEditing(true)}>
              Wijzigen
            </button>
            <button type="button" className="button danger" onClick={() => setDeleting(true)}>
              Groep verwijderen
            </button>
          </div>
        ) : null}
      </div>
      <p className="muted">
        {groupTypeLabels[g.type] ?? g.type} · {g.active ? 'actief' : 'niet actief'}
        {g.description ? ` · ${g.description}` : ''}
      </p>
      <SuccessMessage message={message} />
      <ProblemAlert error={remove.error ?? deleteGroup.error} />

      <section className="card" aria-labelledby="groepsleden">
        <h2 id="groepsleden">Leden ({g.members.length})</h2>
        {g.members.length === 0 ? <p className="muted">Nog geen leden in deze groep.</p> : null}
        <ul className="list">
          {g.members.map((m) => (
            <li key={m.memberId}>
              <Link to="/leden/$id" params={{ id: m.memberId }}>
                {m.fullName}
              </Link>{' '}
              ({m.memberNumber}) · {groupFunctionLabels[m.function] ?? m.function}
              {m.validFrom || m.validTo ? ` · ${formatDate(m.validFrom)} t/m ${formatDate(m.validTo)}` : ''}{' '}
              {canEdit ? (
                <button
                  type="button"
                  className="button secondary small"
                  aria-label={`${m.fullName} uit de groep halen`}
                  onClick={() => remove.mutate(m.memberId, { onSuccess: () => setMessage(`${m.fullName} is uit de groep gehaald.`) })}
                >
                  Uit groep
                </button>
              ) : null}
            </li>
          ))}
        </ul>
        {canEdit ? <AddMember groupId={id} existing={g.members.map((m) => m.memberId)} onAdded={setMessage} /> : null}
      </section>

      <GroupDialog
        open={editing}
        onClose={() => setEditing(false)}
        id={id}
        initial={{ name: g.name, description: g.description ?? '', type: g.type, active: g.active }}
      />
      <ConfirmDialog
        open={deleting}
        title="Groep verwijderen"
        message={`"${g.name}" verwijderen? Content met deze groep als enige doelgroep is daarna voor niemand meer via deze groep zichtbaar.`}
        confirmLabel="Verwijderen"
        busy={deleteGroup.isPending}
        onCancel={() => setDeleting(false)}
        onConfirm={() => deleteGroup.mutate(undefined, { onSuccess: () => void navigate({ to: '/groepen' }) })}
      />
    </>
  );
}

function AddMember({ groupId, existing, onAdded }: { groupId: string; existing: string[]; onAdded: (message: string) => void }) {
  const api = useApi();
  const [search, setSearch] = useState('');
  const [role, setRole] = useState<GroupFunction>('Member');
  const [validTo, setValidTo] = useState('');
  const results = useMembers({ search, status: '', syncState: '' }, 1);
  const matches = search.trim().length >= 2 ? (results.data?.items ?? []).filter((m) => !existing.includes(m.id)).slice(0, 8) : [];
  const add = useApiMutation(
    (memberId: string) =>
      api.PUT('/api/v1/admin/groups/{id}/members/{memberId}', {
        params: { path: { id: groupId, memberId } },
        body: { function: role, validFrom: null, validTo: validTo || null },
      }),
    [['group', groupId], ['groups']],
  );

  return (
    <fieldset>
      <legend>Lid toevoegen</legend>
      <div className="grid-3">
        <Field label="Zoek op naam of lidnummer" value={search} onChange={(e) => setSearch(e.target.value)} />
        <div className="field">
          <label htmlFor="group-function">Functie</label>
          <select id="group-function" value={role} onChange={(e) => setRole(e.target.value as GroupFunction)}>
            {Object.entries(groupFunctionLabels).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </div>
        <Field label="Tot en met (optioneel)" type="date" value={validTo} onChange={(e) => setValidTo(e.target.value)} />
      </div>
      <ProblemAlert error={add.error} />
      {matches.length > 0 ? (
        <ul className="list" aria-label="Zoekresultaten">
          {matches.map((m) => (
            <li key={m.id}>
              {m.fullName} ({m.memberNumber}){' '}
              <button
                type="button"
                className="button secondary small"
                onClick={() =>
                  add.mutate(m.id, {
                    onSuccess: () => {
                      setSearch('');
                      onAdded(`${m.fullName} is toegevoegd.`);
                    },
                  })
                }
              >
                Toevoegen
              </button>
            </li>
          ))}
        </ul>
      ) : null}
    </fieldset>
  );
}
