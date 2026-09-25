import { Link, useParams } from '@tanstack/react-router';
import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, useMe, useMember, type MemberDetail, type MembershipStatus } from '../api/hooks';
import { ConfirmDialog } from '../components/Dialog';
import { Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { accountStatusLabels, formatDate, formatDateTime, membershipStatusLabels, syncStateLabels } from '../format';

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

  return (
    <>
      <p>
        <Link to="/leden">← Leden</Link>
      </p>
      <h1>{m.fullName}</h1>
      <p className="muted">
        Lidnummer {m.memberNumber} · {membershipStatusLabels[m.effectiveStatus] ?? m.effectiveStatus} ·{' '}
        {syncStateLabels[m.syncState] ?? m.syncState}
        {m.ebLastSeenAt ? ` · laatst gezien in e-Boekhouden ${formatDateTime(m.ebLastSeenAt)}` : ''}
      </p>
      <SuccessMessage message={message} />

      {m.syncState === 'Missing' ? (
        <div className="alert alert-warning" role="status">
          <p>
            Dit lid staat sinds {formatDateTime(m.ebMissingSince)} niet meer in e-Boekhouden. Bij de volgende sync wordt
            het lid inactief; je kunt dat hier ook direct bevestigen.
          </p>
          {canEdit ? (
            <button type="button" className="button secondary" onClick={() => setConfirmInactive(true)}>
              Nu op inactief zetten
            </button>
          ) : null}
        </div>
      ) : null}

      <section className="card" aria-labelledby="eboekhouden">
        <h2 id="eboekhouden">Gegevens uit e-Boekhouden</h2>
        <p className="muted">Wijzigen via het secretariaat in e-Boekhouden; de sync neemt het daarna over.</p>
        <dl className="details">
          <dt>Naam</dt>
          <dd>{m.fullName}</dd>
          <dt>Adres</dt>
          <dd>
            {[m.addressLine, [m.postalCode, m.city].filter(Boolean).join(' '), m.country].filter(Boolean).join(', ') ||
              '—'}
          </dd>
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
        <form onSubmit={submit}>
          <fieldset disabled={!canEdit}>
            <legend>Naam (voor de app)</legend>
            <p className="muted">
              e-Boekhouden kent één naamveld; de app splitst het automatisch. Corrigeer hier als dat niet goed ging
              {m.nameCorrectedManually ? ' (handmatig gecorrigeerd)' : ''}.
            </p>
            <div className="grid-3">
              <Field label="Voornaam" value={form.firstName} onChange={(e) => set({ firstName: e.target.value })} />
              <Field
                label="Tussenvoegsel"
                value={form.namePrefix}
                onChange={(e) => set({ namePrefix: e.target.value })}
              />
              <Field label="Achternaam" value={form.lastName} onChange={(e) => set({ lastName: e.target.value })} />
            </div>
          </fieldset>
          <fieldset disabled={!canEdit}>
            <legend>Lidmaatschap</legend>
            <div className="field">
              <label htmlFor="override">Status-override</label>
              <select
                id="override"
                aria-describedby="override-hint"
                value={form.localStatusOverride}
                onChange={(e) => set({ localStatusOverride: e.target.value as MembershipStatus | '' })}
              >
                <option value="">
                  Geen (volgt e-Boekhouden: {membershipStatusLabels[m.syncedStatus] ?? m.syncedStatus})
                </option>
                <option value="Suspended">Geschorst</option>
                <option value="Inactive">Inactief</option>
                <option value="Deceased">Overleden</option>
              </select>
              <small id="override-hint" className="muted">
                Een override wint altijd van de status uit e-Boekhouden.
              </small>
            </div>
            <div className="grid-3">
              <Field
                label="Geldig vanaf"
                type="date"
                value={form.membershipValidFrom}
                onChange={(e) => set({ membershipValidFrom: e.target.value })}
              />
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
                  <Field
                    label="Geboortedatum"
                    type="date"
                    value={form.birthDate}
                    onChange={(e) => set({ birthDate: e.target.value })}
                  />
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

      <section className="card" aria-labelledby="account">
        <h2 id="account">App-account</h2>
        {m.account ? (
          <p>
            <Link to="/gebruikers/$id" params={{ id: m.account.userId }}>
              {m.account.email}
            </Link>{' '}
            · {accountStatusLabels[m.account.accountStatus] ?? m.account.accountStatus} · laatste login{' '}
            {formatDateTime(m.account.lastLoginAt)}
          </p>
        ) : (
          <p className="muted">Dit lid heeft (nog) geen app-account. Accounts voor leden volgen in fase 9.</p>
        )}
      </section>

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
