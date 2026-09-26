import { Link, useParams } from '@tanstack/react-router';
import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, useMe, useMember, useMemberHistory, type MemberDetail, type MembershipStatus } from '../api/hooks';
import { ConfirmDialog } from '../components/Dialog';
import { Field } from '../components/Field';
import { Icon } from '../components/Icon';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import {
  accountStatusLabels,
  formatDate,
  formatDateTime,
  groupFunctionLabels,
  membershipStatusLabels,
  provisioningStepLabels,
  syncStateLabels,
} from '../format';

interface LocalForm {
  localStatusOverride: MembershipStatus | '';
  membershipValidFrom: string;
  membershipValidTo: string;
  firstName: string;
  namePrefix: string;
  lastName: string;
  birthDate: string;
  joinYear: string;
}

function toForm(m: MemberDetail): LocalForm {
  return {
    localStatusOverride: m.localStatusOverride ?? '',
    membershipValidFrom: m.membershipValidFrom ?? '',
    membershipValidTo: m.membershipValidTo ?? '',
    firstName: m.firstName ?? '',
    namePrefix: m.namePrefix ?? '',
    lastName: m.lastName ?? '',
    birthDate: m.birthDate ?? '',
    joinYear: m.joinYear?.toString() ?? '',
  };
}

const orNull = (value: string) => (value.trim() === '' ? null : value.trim());

/** Lid-detail (fase 8): e-Boekhouden-velden alleen-lezen met bron, lokale velden bewerkbaar. */
export function MemberDetailPage() {
  const { id } = useParams({ from: '/leden/$id' });
  const api = useApi();
  const me = useMe();
  const member = useMember(id);
  const [form, setForm] = useState<LocalForm | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [confirmInactive, setConfirmInactive] = useState(false);
  const canEdit = (me.data?.permissions ?? []).includes('member.update');
  const canApprove = (me.data?.permissions ?? []).includes('member.approve');

  useEffect(() => {
    if (member.data) {
      setForm(toForm(member.data));
    }
  }, [member.data]);

  const save = useApiMutation(
    () =>
      api.PATCH('/api/v1/admin/members/{id}', {
        params: { path: { id } },
        body: {
          localStatusOverride: form?.localStatusOverride || null,
          membershipValidFrom: orNull(form?.membershipValidFrom ?? ''),
          membershipValidTo: orNull(form?.membershipValidTo ?? ''),
          firstName: orNull(form?.firstName ?? ''),
          namePrefix: orNull(form?.namePrefix ?? ''),
          lastName: orNull(form?.lastName ?? ''),
          birthDate: orNull(form?.birthDate ?? ''),
          joinYear: form?.joinYear ? Number(form.joinYear) : null,
        },
      }),
    [['member', id], ['members']],
  );
  const provision = useApiMutation(
    () => api.POST('/api/v1/admin/members/{id}/provision-account', { params: { path: { id } } }),
    [['member', id], ['account-provisioning']],
  );
  const markInactive = useApiMutation(
    () => api.POST('/api/v1/admin/members/{id}/confirm-inactive', { params: { path: { id } } }),
    [['member', id], ['members']],
  );

  if (!member.data || !form) {
    return <>{member.error ? <ProblemAlert error={member.error} /> : <p>Laden…</p>}</>;
  }

  const m = member.data;
  const sources = m.fieldSources;
  const set = (change: Partial<LocalForm>) => setForm({ ...form, ...change });

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate(undefined, { onSuccess: () => setMessage('Opgeslagen.') });
  }

  const statusTone: Record<string, string> = { Active: 'ok', Suspended: 'warn' };
  const address = [m.addressLine, [m.postalCode, m.city].filter(Boolean).join(' '), m.country].filter(Boolean).join(', ');

  return (
    <>
      <Link to="/leden" className="back-link">
        <Icon name="terug" size={16} /> Leden
      </Link>
      <div className="page-header">
        <div className="page-title">
          <h1>
            {m.fullName}{' '}
            <span className={`badge ${statusTone[m.effectiveStatus] ?? ''}`}>{membershipStatusLabels[m.effectiveStatus] ?? m.effectiveStatus}</span>
            {m.syncState !== 'InSync' ? (
              <span className={`badge ${m.syncState === 'Missing' ? 'warn' : 'error'}`}>{syncStateLabels[m.syncState] ?? m.syncState}</span>
            ) : null}
          </h1>
          <p className="page-subtitle">
            Lidnummer {m.memberNumber}
            {m.joinYear ? ` · lid sinds ${m.joinYear}` : ''}
            {m.ebLastSeenAt ? ` · laatst gezien in e-Boekhouden ${formatDateTime(m.ebLastSeenAt)}` : ''}
          </p>
        </div>
      </div>
      <SuccessMessage message={message} />

      {m.syncState === 'Missing' ? (
        <div className="alert alert-warning banner" role="status">
          <Icon name="waarschuwing" size={22} />
          <div className="banner-text">
            <strong>Dit lid staat niet meer in e-Boekhouden</strong>
            <p>
              Sinds {formatDateTime(m.ebMissingSince)}. Bij de volgende synchronisatie wordt het lid inactief; is het lid echt
              gestopt, dan kun je dat nu al bevestigen. Rollen en account blijven bewaard.
            </p>
          </div>
          {canEdit ? (
            <button type="button" className="button secondary" onClick={() => setConfirmInactive(true)}>
              Nu op inactief zetten
            </button>
          ) : null}
        </div>
      ) : null}

      <div className="columns">
        <div>
          <section className="card" aria-labelledby="eboekhouden">
            <div className="card-header">
              <h2 id="eboekhouden">Gegevens uit e-Boekhouden</h2>
              <span className="badge info">Bron: e-Boekhouden</span>
            </div>
            <p className="card-hint">Alleen-lezen. Wijzigen gaat via het secretariaat in e-Boekhouden; de sync neemt het daarna over.</p>
            <dl className="details">
              <dt>Naam</dt>
              <dd>{m.fullName}</dd>
              <dt>Adres</dt>
              <dd>{address || '—'}</dd>
              <dt>E-mailadres</dt>
              <dd>{m.email ?? '—'}</dd>
              <dt>Telefoon</dt>
              <dd>{[m.phone, m.mobilePhone].filter(Boolean).join(' · ') || '—'}</dd>
              {sources.birthDateFromEBoekhouden ? (
                <>
                  <dt>Geboortedatum</dt>
                  <dd>{formatDate(m.birthDate)}</dd>
                </>
              ) : null}
              {sources.joinYearFromEBoekhouden ? (
                <>
                  <dt>Inschrijfjaar</dt>
                  <dd>{m.joinYear ?? '—'}</dd>
                </>
              ) : null}
              {sources.statusFromEBoekhouden ? (
                <>
                  <dt>Status in e-Boekhouden</dt>
                  <dd>{m.ebStatusRaw ?? '—'}</dd>
                </>
              ) : null}
              {sources.categoryFromEBoekhouden ? (
                <>
                  <dt>Categorie</dt>
                  <dd>{m.memberCategory ?? '—'}</dd>
                </>
              ) : null}
            </dl>
          </section>

          <section className="card" aria-labelledby="lokaal">
            <h2 id="lokaal">Gegevens van de app</h2>
            <p className="card-hint">
              Deze gegevens beheer je hier; de sync raakt ze nooit aan.
              {m.nameCorrectedManually ? ' De naam is handmatig gecorrigeerd.' : ''}
            </p>
            <form onSubmit={submit}>
              <fieldset disabled={!canEdit} className="plain-fieldset">
                <legend className="visually-hidden">Naam (voor de app)</legend>
                <div className="grid-3">
                  <Field label="Voornaam" value={form.firstName} onChange={(e) => set({ firstName: e.target.value })} />
                  <Field label="Tussenvoegsel" value={form.namePrefix} onChange={(e) => set({ namePrefix: e.target.value })} />
                  <Field label="Achternaam" value={form.lastName} onChange={(e) => set({ lastName: e.target.value })} />
                </div>
              </fieldset>
              <fieldset disabled={!canEdit} className="plain-fieldset">
                <legend className="visually-hidden">Lidmaatschap</legend>
                <div className="grid-3">
                  <div className="field">
                    <label htmlFor="override">Status-override</label>
                    <select
                      id="override"
                      aria-describedby="override-hint"
                      value={form.localStatusOverride}
                      onChange={(e) => set({ localStatusOverride: e.target.value as MembershipStatus | '' })}
                    >
                      <option value="">Volgt e-Boekhouden ({membershipStatusLabels[m.syncedStatus] ?? m.syncedStatus})</option>
                      <option value="Suspended">Geschorst</option>
                      <option value="Inactive">Inactief</option>
                      <option value="Deceased">Overleden</option>
                    </select>
                    <small id="override-hint" className="muted">
                      Een override wint altijd van de status uit e-Boekhouden.
                    </small>
                  </div>
                  <Field label="Geldig vanaf" type="date" value={form.membershipValidFrom} onChange={(e) => set({ membershipValidFrom: e.target.value })} />
                  <Field
                    label="Geldig tot"
                    type="date"
                    hint="Voor een tijdelijk lidmaatschap"
                    value={form.membershipValidTo}
                    onChange={(e) => set({ membershipValidTo: e.target.value })}
                  />
                </div>
                {!sources.birthDateFromEBoekhouden || !sources.joinYearFromEBoekhouden ? (
                  <div className="grid-3">
                    {!sources.birthDateFromEBoekhouden ? (
                      <Field label="Geboortedatum" type="date" value={form.birthDate} onChange={(e) => set({ birthDate: e.target.value })} />
                    ) : null}
                    {!sources.joinYearFromEBoekhouden ? (
                      <Field
                        label="Inschrijfjaar"
                        type="number"
                        min={1900}
                        max={2100}
                        value={form.joinYear}
                        onChange={(e) => set({ joinYear: e.target.value })}
                      />
                    ) : null}
                  </div>
                ) : null}
              </fieldset>
              <ProblemAlert error={save.error ?? markInactive.error} />
              {canEdit ? (
                <div className="actions">
                  <button type="submit" className="button" disabled={save.isPending}>
                    Gegevens van de app opslaan
                  </button>
                </div>
              ) : null}
            </form>
          </section>
        </div>

        <div>
          <section className="card" aria-labelledby="account">
            <h2 id="account">App-account</h2>
            {m.account ? (
              <>
                <dl className="details compact-details">
                  <dt>E-mailadres</dt>
                  <dd>{m.account.email}</dd>
                  <dt>Status</dt>
                  <dd>
                    <span className={`badge ${m.account.accountStatus === 'Active' ? 'ok' : 'warn'}`}>
                      {accountStatusLabels[m.account.accountStatus] ?? m.account.accountStatus}
                    </span>
                  </dd>
                  <dt>Laatste login</dt>
                  <dd>{formatDateTime(m.account.lastLoginAt)}</dd>
                </dl>
                <Link to="/gebruikers/$id" params={{ id: m.account.userId }} className="button ghost">
                  Naar gebruiker en rollen →
                </Link>
              </>
            ) : (
              <>
                <p className="muted">Dit lid heeft (nog) geen app-account.</p>
                {m.provisioning ? (
                  <p>
                    <span className={`badge ${m.provisioning.lastError ? 'error' : 'info'}`}>
                      {m.provisioning.lastError ? 'Aanmaken mislukt' : (provisioningStepLabels[m.provisioning.step] ?? m.provisioning.step)}
                    </span>{' '}
                    {m.provisioning.lastError ? (
                      <Link to="/accountverzoeken">Bekijk en probeer opnieuw</Link>
                    ) : null}
                  </p>
                ) : null}
                <ProblemAlert error={provision.error} />
                {canApprove ? (
                  <button
                    type="button"
                    className="button"
                    disabled={!m.email || m.effectiveStatus !== 'Active' || provision.isPending || Boolean(m.provisioning && !m.provisioning.lastError)}
                    title={m.email ? undefined : 'Vul eerst een e-mailadres in e-Boekhouden in'}
                    onClick={() =>
                      provision.mutate(undefined, { onSuccess: () => setMessage(`Het account wordt aangemaakt; ${m.fullName} krijgt een welkomstmail op ${m.email}.`) })
                    }
                  >
                    App-account aanmaken
                  </button>
                ) : null}
              </>
            )}
          </section>

          <section className="card" aria-labelledby="groepen">
            <div className="card-header">
              <h2 id="groepen">Groepen</h2>
              <Link to="/groepen" className="button ghost small">
                Groepen beheren
              </Link>
            </div>
            {m.groups.length === 0 ? (
              <p className="muted">Nog in geen enkele groep.</p>
            ) : (
              <ul className="list">
                {m.groups.map((g) => (
                  <li key={g.groupId} className="list-row">
                    <Link to="/groepen/$id" params={{ id: g.groupId }} className="grow">
                      {g.name}
                    </Link>
                    <span className="badge">{groupFunctionLabels[g.function] ?? g.function}</span>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <MemberHistory memberId={id} />
        </div>
      </div>

      <ConfirmDialog
        open={confirmInactive}
        title="Lid op inactief zetten"
        message={`${m.fullName} staat niet meer in e-Boekhouden. Nu op inactief zetten? Rollen en account blijven bewaard.`}
        confirmLabel="Op inactief zetten"
        busy={markInactive.isPending}
        onCancel={() => setConfirmInactive(false)}
        onConfirm={() =>
          markInactive.mutate(undefined, {
            onSuccess: () => {
              setConfirmInactive(false);
              setMessage('Het lid is op inactief gezet.');
            },
          })
        }
      />
    </>
  );
}

const historyLabels: Record<string, string> = {
  'member.updated': 'Gegevens van de app gewijzigd',
  'member.confirmed-inactive': 'Op inactief gezet',
};

/** Laatste wijzigingen aan dit lid uit de auditlog (alleen met audit.read). */
function MemberHistory({ memberId }: { memberId: string }) {
  const me = useMe();
  const canRead = (me.data?.permissions ?? []).includes('audit.read');
  const history = useMemberHistory(memberId, canRead);
  if (!canRead) {
    return null;
  }
  return (
    <section className="card" aria-labelledby="historie">
      <h2 id="historie">Historie</h2>
      {history.data?.items.length ? (
        <ul className="list">
          {history.data.items.map((entry) => (
            <li key={entry.id}>
              <p>{historyLabels[entry.action] ?? entry.action}</p>
              <p className="muted small-text">
                {formatDateTime(entry.occurredAt)}
                {entry.actorName ? ` · ${entry.actorName}` : ''}
              </p>
            </li>
          ))}
        </ul>
      ) : (
        <p className="muted">Nog geen wijzigingen in de app.</p>
      )}
    </section>
  );
}
