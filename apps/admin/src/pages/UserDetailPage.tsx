import { Link, useParams } from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import { useApi } from '../api/ApiContext';
import {
  useApiMutation,
  useMe,
  useRoles,
  useTestAccess,
  useUser,
  useUserDevices,
  useUserRoles,
  type RoleAssignment,
} from '../api/hooks';
import { ConfirmDialog, Dialog } from '../components/Dialog';
import { Checkbox, Field } from '../components/Field';
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
  const me = useMe();
  const canBlock = (me.data?.permissions ?? []).includes('member.block');
  const canPrivacy = (me.data?.permissions ?? []).includes('member.privacy');
  const [erasing, setErasing] = useState(false);
  const [eraseConfirmation, setEraseConfirmation] = useState('');
  const exportData = useApiMutation(async () => {
    const { data } = await api.POST('/api/v1/admin/users/{id}/privacy-export', { params: { path: { id } } });
    if (data?.downloadUrl) {
      window.open(data.downloadUrl, '_blank', 'noopener');
    }
  }, [['privacy-requests']]);
  const erase = useApiMutation(
    () => api.POST('/api/v1/admin/users/{id}/erase', { params: { path: { id } }, body: { confirmation: eraseConfirmation } }),
    [['user', id], ['users'], ['privacy-requests']],
  );
  const devices = useUserDevices(id, canBlock);
  const revokeDevice = useApiMutation(
    (deviceId: string) => api.POST('/api/v1/admin/devices/{id}/revoke', { params: { path: { id: deviceId } } }),
    [['user-devices', id]],
  );

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
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Rollen van deze gebruiker">
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
          </div>
          <ProblemAlert error={saveRoles.error} />
          <button type="submit" className="button" disabled={saveRoles.isPending}>
            Rollen opslaan
          </button>
        </form>
      </section>
      {canBlock ? (
        <section className="card" aria-labelledby="apparaten">
          <h2 id="apparaten">Apparaten</h2>
          <p className="muted">Waar deze gebruiker in de app is ingelogd. Intrekken logt dat apparaat direct uit.</p>
          <ProblemAlert error={devices.error ?? revokeDevice.error} />
          {devices.data && devices.data.length === 0 ? <p className="muted">Nog niet ingelogd in de app.</p> : null}
          {devices.data && devices.data.length > 0 ? (
            <div className="table-scroll" tabIndex={0} role="region" aria-label="Apparaten van deze gebruiker">
              <table className="table compact">
                <caption className="visually-hidden">Apparaten van deze gebruiker</caption>
                <thead>
                  <tr>
                    <th scope="col">Apparaat</th>
                    <th scope="col">Laatst gebruikt</th>
                    <th scope="col">Status</th>
                    <th scope="col">
                      <span className="visually-hidden">Acties</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {devices.data.map((d) => (
                    <tr key={d.id}>
                      <td>
                        {d.name}
                        <div className="muted small-text">
                          {d.platform === 'Ios' ? 'iOS' : 'Android'}
                          {d.appVersion ? ` · app ${d.appVersion}` : ''}
                        </div>
                      </td>
                      <td>{formatDateTime(d.lastSeenAt)}</td>
                      <td>
                        <span className={`badge ${d.status === 'Active' ? 'ok' : 'neutral'}`}>{d.status === 'Active' ? 'Actief' : 'Ingetrokken'}</span>
                      </td>
                      <td>
                        {d.status === 'Active' ? (
                          <button
                            type="button"
                            className="button secondary small"
                            aria-label={`${d.name} intrekken`}
                            disabled={revokeDevice.isPending}
                            onClick={() => revokeDevice.mutate(d.id, { onSuccess: () => setMessage(`${d.name} is ingetrokken.`) })}
                          >
                            Intrekken
                          </button>
                        ) : null}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : null}
        </section>
      ) : null}
      {canPrivacy && user.data.accountStatus !== 'Deleted' ? (
        <section className="card" aria-labelledby="privacy">
          <h2 id="privacy">Privacy (AVG)</h2>
          <p className="muted">
            Voor een verzoek van dit lid (per brief of e-mail). Wissen verwijdert het account, de inlog en alle gegevens die alleen de app
            bewaart; de auditlog blijft. De ledenadministratie in e-Boekhouden past het secretariaat zelf aan.
          </p>
          <ProblemAlert error={exportData.error ?? erase.error} />
          <div className="actions">
            <button type="button" className="button secondary" disabled={exportData.isPending} onClick={() => exportData.mutate(undefined)}>
              Gegevens exporteren
            </button>
            <button type="button" className="button danger" onClick={() => setErasing(true)}>
              Alle app-gegevens wissen
            </button>
          </div>
        </section>
      ) : null}
      <Dialog open={erasing} title="Alle app-gegevens wissen" onClose={() => setErasing(false)}>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            erase.mutate(undefined, {
              onSuccess: () => {
                setErasing(false);
                setMessage('De app-gegevens zijn gewist en het account is verwijderd. Vergeet e-Boekhouden niet.');
              },
            });
          }}
        >
          <p>
            Dit kan niet ongedaan worden gemaakt: het account van {user.data.displayName}, de inlog, aanmeldingen, accountverzoeken,
            aanmeldhistorie en apparaten worden verwijderd.
          </p>
          <Field label="Typ WISSEN ter bevestiging" value={eraseConfirmation} onChange={(e) => setEraseConfirmation(e.target.value)} />
          <ProblemAlert error={erase.error} />
          <div className="actions">
            <button type="button" className="button secondary" onClick={() => setErasing(false)}>
              Annuleren
            </button>
            <button type="submit" className="button danger" disabled={eraseConfirmation !== 'WISSEN' || erase.isPending}>
              Wissen
            </button>
          </div>
        </form>
      </Dialog>
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
      {user.data.accountStatus !== 'Deleted' ? <TestAccessCard userId={id} name={user.data.displayName} /> : null}
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

/**
 * Toegang tot de testomgeving (Dev/Acc, B-02): groep Testers + environmentAccess in Entra, in plaats van set-tester.sh.
 * Alleen zichtbaar waar dat kan; in Prod is er geen toewijzingsplicht.
 */
function TestAccessCard({ userId, name }: { userId: string; name: string }) {
  const api = useApi();
  const me = useMe();
  const canManage = (me.data?.permissions ?? []).includes('role.manage');
  const status = useTestAccess(userId, canManage);
  const set = useApiMutation(
    (granted: boolean) => api.PUT('/api/v1/admin/users/{id}/test-access', { params: { path: { id: userId } }, body: { granted } }),
    [['test-access', userId]],
  );
  if (!canManage || (status.data && !status.data.available)) {
    return null;
  }
  const s = status.data;
  const environment = s?.environment?.toUpperCase() ?? '';
  return (
    <section className="card" aria-labelledby="testomgeving">
      <h2 id="testomgeving">Toegang tot testomgeving</h2>
      <ProblemAlert error={status.error ?? set.error} />
      {!s ? (
        status.isLoading ? <p>Laden…</p> : null
      ) : !s.hasSignIn ? (
        <p className="muted">
          {name} heeft nog geen inlog. Laat eerst een inlog aanmaken in de app of het portal (e-mail + code); de foutmelding die dan
          volgt is normaal. Daarna kun je hier toegang geven.
        </p>
      ) : (
        <p>
          {s.hasAccessHere
            ? `${name} heeft toegang tot ${environment} (${s.environments}).`
            : `${name} heeft geen toegang tot ${environment}. Toegang geven zet de inlog in de groep Testers met ${s.grant}; daarna opnieuw inloggen.`}
        </p>
      )}
      {s?.available ? (
        <div className="actions">
          {s.hasAccessHere || s.inTestersGroup ? (
            <button type="button" className="button secondary" disabled={set.isPending} onClick={() => set.mutate(false)}>
              Toegang intrekken
            </button>
          ) : null}
          {!s.hasAccessHere ? (
            <button type="button" className="button" disabled={set.isPending} onClick={() => set.mutate(true)}>
              Toegang geven
            </button>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}
