import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, usePermissions, useRoleMembers, useRoles, useUsers, type Role, type RoleMember } from '../api/hooks';
import { ConfirmDialog, Dialog } from '../components/Dialog';
import { Checkbox, Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';

/** Queries die opnieuw moeten laden na een rolmutatie op een gebruiker. */
const memberInvalidate: string[][] = [['role-members'], ['roles'], ['users'], ['me']];

export function RolesPage() {
  const roles = useRoles();
  const [selectedCode, setSelectedCode] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const selected = roles.data?.find((r) => r.code === selectedCode) ?? roles.data?.[0];

  return (
    <>
      <div className="page-header">
        <h1>Rollen en rechten</h1>
        <button type="button" className="button" onClick={() => setCreating(true)}>
          Rol toevoegen
        </button>
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={roles.error} />
      <div className="split">
        <nav aria-label="Rollen" className="card">
          <ul className="plain selectable">
            {(roles.data ?? []).map((role) => (
              <li key={role.code}>
                <button
                  type="button"
                  aria-current={role.code === selected?.code ? 'true' : undefined}
                  onClick={() => setSelectedCode(role.code)}
                >
                  {role.name}
                  {role.isSystem ? <span className="badge">systeem</span> : null}
                </button>
              </li>
            ))}
          </ul>
        </nav>
        {selected ? (
          <div className="stack">
            <RoleEditor key={selected.code} role={selected} onMessage={setMessage} />
            <RoleMembers key={`${selected.code}-leden`} role={selected} onMessage={setMessage} />
          </div>
        ) : null}
      </div>
      <CreateRoleDialog
        open={creating}
        onClose={() => setCreating(false)}
        onCreated={(code) => {
          setCreating(false);
          setSelectedCode(code);
          setMessage('Rol toegevoegd.');
        }}
      />
    </>
  );
}

function RoleEditor({ role, onMessage }: { role: Role; onMessage: (message: string) => void }) {
  const api = useApi();
  const permissions = usePermissions();
  const [checked, setChecked] = useState<string[]>(role.permissions);
  const [confirmDelete, setConfirmDelete] = useState(false);
  useEffect(() => setChecked(role.permissions), [role.permissions]);

  const byCategory = useMemo(() => {
    const groups = new Map<string, { code: string; description: string }[]>();
    for (const p of permissions.data ?? []) {
      groups.set(p.category, [...(groups.get(p.category) ?? []), p]);
    }
    return [...groups.entries()];
  }, [permissions.data]);

  const save = useApiMutation(
    () => api.PUT('/api/v1/admin/roles/{id}/permissions', { params: { path: { id: role.id } }, body: { permissions: checked } }),
    [['roles'], ['me']],
  );
  const remove = useApiMutation(() => api.DELETE('/api/v1/admin/roles/{id}', { params: { path: { id: role.id } } }), [['roles']]);

  return (
    <section className="card grow" aria-labelledby="rol-titel">
      <h2 id="rol-titel">{role.name}</h2>
      {role.description ? <p className="muted">{role.description}</p> : null}
      <form
        onSubmit={(e) => {
          e.preventDefault();
          save.mutate(undefined, { onSuccess: () => onMessage(`Rechten van ${role.name} opgeslagen.`) });
        }}
      >
        {byCategory.map(([category, items]) => (
          <fieldset key={category}>
            <legend>{category}</legend>
            {items.map((p) => (
              <Checkbox
                key={p.code}
                label={
                  <>
                    {p.description} <code>{p.code}</code>
                  </>
                }
                checked={checked.includes(p.code)}
                onChange={(e) => setChecked((c) => (e.target.checked ? [...c, p.code] : c.filter((x) => x !== p.code)))}
              />
            ))}
          </fieldset>
        ))}
        <ProblemAlert error={save.error ?? remove.error} />
        <div className="actions">
          {!role.isSystem ? (
            <button type="button" className="button danger" onClick={() => setConfirmDelete(true)}>
              Rol verwijderen
            </button>
          ) : null}
          <button type="submit" className="button" disabled={save.isPending}>
            Rechten opslaan
          </button>
        </div>
      </form>
      <ConfirmDialog
        open={confirmDelete}
        title={`Rol ${role.name} verwijderen?`}
        message="Gebruikers met deze rol verliezen de bijbehorende rechten."
        confirmLabel="Verwijderen"
        busy={remove.isPending}
        onCancel={() => setConfirmDelete(false)}
        onConfirm={() => remove.mutate(undefined, { onSettled: () => setConfirmDelete(false), onSuccess: () => onMessage('Rol verwijderd.') })}
      />
    </section>
  );
}

/**
 * Gebruikers van een rol: huidige leden met "Verwijderen", en een zoekveld om iemand toe te voegen. Toevoegen en
 * verwijderen raken de andere rollen van de gebruiker niet; de laatste rolbeheerder kan niet worden weggehaald (409).
 */
function RoleMembers({ role, onMessage }: { role: Role; onMessage: (message: string) => void }) {
  const api = useApi();
  const members = useRoleMembers(role.id);
  const [confirmRemove, setConfirmRemove] = useState<RoleMember | null>(null);

  const remove = useApiMutation(
    (userId: string) =>
      api.DELETE('/api/v1/admin/roles/{id}/members/{userId}', { params: { path: { id: role.id, userId } } }),
    memberInvalidate,
  );

  const list = members.data ?? [];

  return (
    <section className="card" aria-labelledby="rol-leden">
      <div className="card-header">
        <h2 id="rol-leden">Gebruikers met deze rol</h2>
        <span className="badge">{list.length}</span>
      </div>
      <ProblemAlert error={members.error ?? remove.error} />
      {list.length === 0 ? <p className="muted">Nog niemand heeft deze rol.</p> : null}
      <ul className="list">
        {list.map((member) => (
          <li key={member.userId} className="list-row">
            <div className="grow">
              <strong>{member.displayName}</strong>
              <div className="muted small-text">
                {member.email}
                {member.isTemporary ? ' · tijdelijk' : ''}
              </div>
            </div>
            <button
              type="button"
              className="button ghost small"
              disabled={remove.isPending}
              onClick={() => setConfirmRemove(member)}
            >
              Verwijderen
            </button>
          </li>
        ))}
      </ul>
      <AddRoleMember role={role} members={list} onMessage={onMessage} />
      <ConfirmDialog
        open={confirmRemove !== null}
        title={confirmRemove ? `${confirmRemove.displayName} uit ${role.name} halen?` : ''}
        message={
          confirmRemove
            ? `${confirmRemove.displayName} verliest de rechten van ${role.name}. De andere rollen blijven staan.`
            : ''
        }
        confirmLabel="Verwijderen"
        busy={remove.isPending}
        onCancel={() => setConfirmRemove(null)}
        onConfirm={() => {
          const member = confirmRemove;
          if (!member) {
            return;
          }
          remove.mutate(member.userId, {
            onSettled: () => setConfirmRemove(null),
            onSuccess: () => onMessage(`${member.displayName} is uit ${role.name} gehaald.`),
          });
        }}
      />
    </section>
  );
}

/** Zoekveld om een bestaande gebruiker te vinden en aan de rol toe te voegen (hergebruikt de gebruikerszoekopdracht). */
function AddRoleMember({
  role,
  members,
  onMessage,
}: {
  role: Role;
  members: RoleMember[];
  onMessage: (message: string) => void;
}) {
  const api = useApi();
  const [search, setSearch] = useState('');
  const users = useUsers(search, 1);
  const add = useApiMutation(
    (userId: string) =>
      api.POST('/api/v1/admin/roles/{id}/members', { params: { path: { id: role.id } }, body: { userId } }),
    memberInvalidate,
  );

  const query = search.trim();
  const current = new Set(members.map((m) => m.userId));
  const results = (users.data?.items ?? []).filter((u) => !current.has(u.id));

  return (
    <div className="nested-add">
      <Field
        label="Gebruiker toevoegen"
        hint="Zoek op naam of e-mailadres"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
      />
      <ProblemAlert error={add.error} />
      {query.length >= 2 ? (
        <>
          <ul className="list">
            {results.map((user) => (
              <li key={user.id} className="list-row">
                <div className="grow">
                  <strong>{user.displayName}</strong>
                  <div className="muted small-text">{user.email}</div>
                </div>
                <button
                  type="button"
                  className="button small"
                  disabled={add.isPending}
                  onClick={() =>
                    add.mutate(user.id, { onSuccess: () => onMessage(`${user.displayName} heeft nu de rol ${role.name}.`) })
                  }
                >
                  Toevoegen
                </button>
              </li>
            ))}
          </ul>
          {results.length === 0 ? (
            <p className="muted">{users.isLoading ? 'Zoeken…' : 'Geen gebruiker gevonden die deze rol nog niet heeft.'}</p>
          ) : null}
        </>
      ) : null}
    </div>
  );
}

function CreateRoleDialog({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (code: string) => void }) {
  const api = useApi();
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const create = useApiMutation(
    () => api.POST('/api/v1/admin/roles', { body: { code, name, description: null, permissions: [] } }),
    [['roles']],
  );
  function submit(event: FormEvent) {
    event.preventDefault();
    create.mutate(undefined, { onSuccess: () => onCreated(code) });
  }
  return (
    <Dialog open={open} title="Rol toevoegen" onClose={onClose}>
      <form onSubmit={submit}>
        <Field label="Naam" required value={name} onChange={(e) => setName(e.target.value)} />
        <Field
          label="Code"
          hint="Kleine letters, cijfers en streepjes, bijv. penningmeester"
          required
          pattern="^[a-z][a-z0-9-]{1,48}$"
          value={code}
          onChange={(e) => setCode(e.target.value)}
        />
        <ProblemAlert error={create.error} />
        <div className="actions">
          <button type="button" className="button secondary" onClick={onClose}>
            Annuleren
          </button>
          <button type="submit" className="button" disabled={create.isPending}>
            Toevoegen
          </button>
        </div>
      </form>
    </Dialog>
  );
}
