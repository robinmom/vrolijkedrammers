import { useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation, type MemberDetail } from '../api/hooks';
import { formatDate } from '../format';
import { ConfirmDialog } from './Dialog';
import { Field } from './Field';
import { ProblemAlert, SuccessMessage } from './ProblemAlert';

interface DataForm {
  fullName: string;
  salutation: string;
  gender: string;
  addressLine: string;
  postalCode: string;
  city: string;
  country: string;
  email: string;
  phone: string;
  mobilePhone: string;
  birthDate: string;
  joinYear: string;
  memberCategory: string;
  paradeGroupName: string;
}

function toForm(m: MemberDetail): DataForm {
  return {
    fullName: m.fullName,
    salutation: m.salutation ?? '',
    gender: m.gender ?? '',
    addressLine: m.addressLine ?? '',
    postalCode: m.postalCode ?? '',
    city: m.city ?? '',
    country: m.country ?? '',
    email: m.email ?? '',
    phone: m.phone ?? '',
    mobilePhone: m.mobilePhone ?? '',
    birthDate: m.birthDate ?? '',
    joinYear: m.joinYear?.toString() ?? '',
    memberCategory: m.memberCategory ?? '',
    paradeGroupName: m.paradeGroupName ?? '',
  };
}

const orNull = (value: string) => (value.trim() === '' ? null : value.trim());

const genderLabels: Record<string, string> = { m: 'Man', v: 'Vrouw', a: 'Anders' };

/**
 * Gegevens van een lid (fase 24): de velden die uit e-Boekhouden komen, nu ook te bewerken. Een aangepast veld is
 * "handmatig": de sync overschrijft het niet meer (het portal wint) en meldt het als e-Boekhouden afwijkt. Met
 * "Teruggeven aan e-Boekhouden" neemt de volgende sync alles weer over.
 */
export function MemberDataCard({ member: m, canEdit }: { member: MemberDetail; canEdit: boolean }) {
  const api = useApi();
  const [form, setForm] = useState<DataForm | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [confirmRelease, setConfirmRelease] = useState(false);
  const local = new Set(m.localFields ?? []);

  const save = useApiMutation(
    (f: DataForm) =>
      api.PUT('/api/v1/admin/members/{id}/data', {
        params: { path: { id: m.id } },
        body: {
          fullName: f.fullName.trim(),
          salutation: orNull(f.salutation),
          gender: orNull(f.gender),
          addressLine: orNull(f.addressLine),
          postalCode: orNull(f.postalCode),
          city: orNull(f.city),
          country: orNull(f.country),
          email: orNull(f.email),
          phone: orNull(f.phone),
          mobilePhone: orNull(f.mobilePhone),
          birthDate: orNull(f.birthDate),
          joinYear: f.joinYear ? Number(f.joinYear) : null,
          memberCategory: orNull(f.memberCategory),
          paradeGroupName: orNull(f.paradeGroupName),
        },
      }),
    [['member', m.id], ['members']],
  );
  const release = useApiMutation(
    () => api.DELETE('/api/v1/admin/members/{id}/local-fields', { params: { path: { id: m.id } } }),
    [['member', m.id]],
  );

  function submit(event: FormEvent) {
    event.preventDefault();
    setMessage(null);
    save.mutate(form!, {
      onSuccess: () => {
        setForm(null);
        setMessage('Gegevens opgeslagen. Aangepaste velden overschrijft de sync niet meer.');
      },
    });
  }

  const Manual = ({ field }: { field: string }) =>
    local.has(field) ? (
      <span className="badge" title="In het portal aangepast; de sync overschrijft dit niet">
        handmatig
      </span>
    ) : null;

  const address = [m.addressLine, [m.postalCode, m.city].filter(Boolean).join(' '), m.country]
    .filter(Boolean)
    .join(', ');
  const set = (change: Partial<DataForm>) => setForm({ ...form!, ...change });

  return (
    <section className="card" aria-labelledby="ledengegevens">
      <div className="card-header">
        <h2 id="ledengegevens">Gegevens van het lid</h2>
        {canEdit && !form ? (
          <button
            type="button"
            className="button secondary small"
            onClick={() => {
              setMessage(null);
              setForm(toForm(m));
            }}
          >
            Bewerken
          </button>
        ) : null}
      </div>
      <p className="card-hint">
        Komt uit e-Boekhouden. Wat je hier aanpast, is &quot;handmatig&quot;: de sync overschrijft het niet meer.
      </p>
      <ProblemAlert error={release.error} />
      <SuccessMessage message={message} />
      {form ? (
        <form onSubmit={submit}>
          <div className="grid-3">
            <Field
              label="Naam"
              required
              maxLength={100}
              value={form.fullName}
              onChange={(e) => set({ fullName: e.target.value })}
            />
            <Field
              label="Aanhef"
              maxLength={50}
              value={form.salutation}
              onChange={(e) => set({ salutation: e.target.value })}
            />
            <div className="field">
              <label htmlFor="lid-geslacht">Geslacht</label>
              <select id="lid-geslacht" value={form.gender} onChange={(e) => set({ gender: e.target.value })}>
                <option value="">Onbekend</option>
                <option value="m">Man</option>
                <option value="v">Vrouw</option>
                <option value="a">Anders</option>
              </select>
            </div>
          </div>
          <Field
            label="Straat en huisnummer"
            maxLength={150}
            value={form.addressLine}
            onChange={(e) => set({ addressLine: e.target.value })}
          />
          <div className="grid-3">
            <Field
              label="Postcode"
              maxLength={50}
              value={form.postalCode}
              onChange={(e) => set({ postalCode: e.target.value })}
            />
            <Field label="Plaats" maxLength={50} value={form.city} onChange={(e) => set({ city: e.target.value })} />
            <Field
              label="Land"
              maxLength={50}
              value={form.country}
              onChange={(e) => set({ country: e.target.value })}
            />
          </div>
          <div className="grid-3">
            <Field
              label="E-mailadres"
              type="email"
              maxLength={150}
              value={form.email}
              onChange={(e) => set({ email: e.target.value })}
            />
            <Field
              label="Telefoon"
              type="tel"
              maxLength={50}
              value={form.phone}
              onChange={(e) => set({ phone: e.target.value })}
            />
            <Field
              label="Mobiel"
              type="tel"
              maxLength={50}
              value={form.mobilePhone}
              onChange={(e) => set({ mobilePhone: e.target.value })}
            />
          </div>
          <div className="grid-3">
            <Field
              label="Geboortedatum"
              type="date"
              value={form.birthDate}
              onChange={(e) => set({ birthDate: e.target.value })}
            />
            <Field
              label="Inschrijfjaar"
              type="number"
              min={1900}
              max={2100}
              value={form.joinYear}
              onChange={(e) => set({ joinYear: e.target.value })}
            />
            <Field
              label="Categorie"
              maxLength={50}
              value={form.memberCategory}
              onChange={(e) => set({ memberCategory: e.target.value })}
            />
          </div>
          <Field
            label="Groep (optocht)"
            maxLength={100}
            value={form.paradeGroupName}
            onChange={(e) => set({ paradeGroupName: e.target.value })}
          />
          {m.account ? (
            <p className="card-hint">Het inlogadres van het app-account verandert niet mee met het e-mailadres.</p>
          ) : null}
          <ProblemAlert error={save.error} />
          <div className="actions">
            <button type="button" className="button secondary" onClick={() => setForm(null)}>
              Annuleren
            </button>
            <button type="submit" className="button" disabled={save.isPending}>
              Gegevens opslaan
            </button>
          </div>
        </form>
      ) : (
        <dl className="details">
          <dt>Naam</dt>
          <dd>
            {m.fullName} <Manual field="name" />
          </dd>
          <dt>Adres</dt>
          <dd>
            {address || '—'} <Manual field="address" />
            <Manual field="postalCode" />
            <Manual field="city" />
          </dd>
          <dt>E-mailadres</dt>
          <dd>
            {m.email ?? '—'} <Manual field="email" />
          </dd>
          <dt>Telefoon</dt>
          <dd>
            {[m.phone, m.mobilePhone].filter(Boolean).join(' · ') || '—'} <Manual field="phone" />
            <Manual field="mobilePhone" />
          </dd>
          <dt>Geslacht</dt>
          <dd>
            {m.gender ? (genderLabels[m.gender] ?? m.gender) : '—'} <Manual field="gender" />
          </dd>
          <dt>Geboortedatum</dt>
          <dd>
            {formatDate(m.birthDate)} <Manual field="birthDate" />
          </dd>
          <dt>Inschrijfjaar</dt>
          <dd>
            {m.joinYear ?? '—'} <Manual field="joinYear" />
          </dd>
          {m.ebStatusRaw ? (
            <>
              <dt>Status in e-Boekhouden</dt>
              <dd>
                {m.ebStatusRaw}
                {m.persons > 1 ? ` (telt als ${m.persons} personen)` : ''}
              </dd>
            </>
          ) : null}
          <dt>Categorie</dt>
          <dd>
            {m.memberCategory ?? '—'} <Manual field="category" />
          </dd>
          <dt>Groep</dt>
          <dd>
            {m.paradeGroupName ?? '—'} <Manual field="paradeGroupName" />
          </dd>
        </dl>
      )}
      {canEdit && local.size > 0 && !form ? (
        <div className="actions">
          <button type="button" className="button ghost small" onClick={() => setConfirmRelease(true)}>
            Teruggeven aan e-Boekhouden
          </button>
        </div>
      ) : null}
      <ConfirmDialog
        open={confirmRelease}
        title="Teruggeven aan e-Boekhouden?"
        message="De handmatig aangepaste velden worden bij de volgende sync weer overschreven met de gegevens uit e-Boekhouden."
        confirmLabel="Teruggeven"
        busy={release.isPending}
        onConfirm={() =>
          release.mutate(undefined, {
            onSuccess: () => {
              setConfirmRelease(false);
              setMessage('De volgende sync neemt de gegevens weer over uit e-Boekhouden.');
            },
          })
        }
        onCancel={() => setConfirmRelease(false)}
      />
    </section>
  );
}
