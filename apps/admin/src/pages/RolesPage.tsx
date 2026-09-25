import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, usePermissions, useRoles, type Role } from '../api/hooks';
import { ConfirmDialog, Dialog } from '../components/Dialog';
import { Checkbox, Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';

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
        {selected ? <RoleEditor key={selected.code} role={selected} onMessage={setMessage} /> : null}
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
