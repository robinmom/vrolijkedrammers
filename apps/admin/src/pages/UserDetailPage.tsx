import { Link, useParams } from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, useRoles, useUser, useUserRoles, type RoleAssignment } from '../api/hooks';
import { ConfirmDialog } from '../components/Dialog';
import { Checkbox } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { accountStatusLabels, formatDateTime } from '../format';

export function UserDetailPage() {
  const { id } = useParams({ from: '/gebruikers/$id' });
  const api = useApi();
  const user = useUser(id);
  const roles = useRoles();
  const userRoles = useUserRoles(id);
  const [assignments, setAssignments] = useState<RoleAssignment[]>([]);
  const [message, setMessage] = useState<string | null>(null);
  const [confirmBlock, setConfirmBlock] = useState(false);

  useEffect(() => {
    if (userRoles.data) {
      setAssignments(userRoles.data.map((r) => ({ roleCode: r.code, validFrom: r.validFrom, validTo: r.validTo })));
    }
  }, [userRoles.data]);

  const saveRoles = useApiMutation(
    () => api.PUT('/api/v1/admin/users/{id}/roles', { params: { path: { id } }, body: { roles: assignments } }),
    [['user-roles', id], ['users'], ['me']],
  );
  const blocked = user.data?.accountStatus === 'Blocked';
  const toggleBlock = useApiMutation(
    () =>
      blocked
        ? api.POST('/api/v1/admin/users/{id}/unblock', { params: { path: { id } } })
        : api.POST('/api/v1/admin/users/{id}/block', { params: { path: { id } } }),
    [['user', id], ['users']],
  );

  function update(code: string, change: Partial<RoleAssignment> | null) {
    setAssignments((current) => {
      const without = current.filter((a) => a.roleCode !== code);
      if (change === null) {
        return without;
      }
      const existing = current.find((a) => a.roleCode === code) ?? { roleCode: code, validFrom: null, validTo: null };
      return [...without, { ...existing, ...change }];
    });
  }

  if (!user.data) {
    return <>{user.error ? <ProblemAlert error={user.error} /> : <p>Laden…</p>}</>;
  }

  return (
    <>
      <p>
        <Link to="/gebruikers">← Gebruikers</Link>
      </p>
      <h1>{user.data.displayName}</h1>
      <p className="muted">
        {user.data.email} · {accountStatusLabels[user.data.accountStatus] ?? user.data.accountStatus} · laatste login{' '}
        {formatDateTime(user.data.lastLoginAt)}
      </p>
      <SuccessMessage message={message} />
      <section className="card" aria-labelledby="rollen">
        <h2 id="rollen">Rollen</h2>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            saveRoles.mutate(undefined, { onSuccess: () => setMessage('Rollen opgeslagen.') });
          }}
        >
          <table className="table compact">
            <caption className="visually-hidden">Rollen van deze gebruiker, met optionele geldigheid</caption>
            <thead>
              <tr>
                <th scope="col">Rol</th>
                <th scope="col">Geldig vanaf</th>
                <th scope="col">Geldig tot</th>
              </tr>
            </thead>
            <tbody>
              {(roles.data ?? []).map((role) => {
                const assignment = assignments.find((a) => a.roleCode === role.code);
                return (
                  <tr key={role.code}>
                    <td>
                      <Checkbox
                        label={role.name}
                        checked={Boolean(assignment)}
                        onChange={(e) => update(role.code, e.target.checked ? {} : null)}
                      />
                    </td>
                    <td>
                      <input
                        type="date"
                        aria-label={`${role.name} geldig vanaf`}
                        disabled={!assignment}
                        value={assignment?.validFrom ?? ''}
                        onChange={(e) => update(role.code, { validFrom: e.target.value || null })}
                      />
                    </td>
                    <td>
                      <input
                        type="date"
                        aria-label={`${role.name} geldig tot`}
                        disabled={!assignment}
                        value={assignment?.validTo ?? ''}
                        onChange={(e) => update(role.code, { validTo: e.target.value || null })}
                      />
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          <ProblemAlert error={saveRoles.error} />
          <button type="submit" className="button" disabled={saveRoles.isPending}>
            Rollen opslaan
          </button>
        </form>
      </section>
      <section className="card" aria-labelledby="toegang">
        <h2 id="toegang">Toegang</h2>
        <p>
          {blocked
            ? 'Dit account is geblokkeerd: inloggen is niet mogelijk.'
            : 'Blokkeren schakelt het account uit en beëindigt alle sessies.'}
        </p>
        <ProblemAlert error={toggleBlock.error} />
        <button type="button" className={blocked ? 'button' : 'button danger'} onClick={() => setConfirmBlock(true)}>
          {blocked ? 'Deblokkeren' : 'Blokkeren'}
        </button>
      </section>
      <ConfirmDialog
        open={confirmBlock}
        title={blocked ? 'Account deblokkeren?' : 'Account blokkeren?'}
        message={
          blocked
            ? `${user.data.displayName} kan daarna weer inloggen.`
            : `${user.data.displayName} kan daarna niet meer inloggen; lopende sessies worden beëindigd.`
        }
        confirmLabel={blocked ? 'Deblokkeren' : 'Blokkeren'}
        busy={toggleBlock.isPending}
        onCancel={() => setConfirmBlock(false)}
        onConfirm={() =>
          toggleBlock.mutate(undefined, {
            onSettled: () => setConfirmBlock(false),
            onSuccess: () => setMessage(blocked ? 'Account gedeblokkeerd.' : 'Account geblokkeerd.'),
          })
        }
      />
    </>
  );
}
