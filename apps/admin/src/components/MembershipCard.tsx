import { useQuery } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { kindLabels, useMembershipSettings, type MembershipKind } from '../api/contributions';
import { useApiMutation, type MemberDetail } from '../api/hooks';
import { Field } from './Field';
import { ProblemAlert, SuccessMessage } from './ProblemAlert';

interface Form {
  kind: MembershipKind | '';
  payerMemberId: string | null;
  payerName: string;
  exempt: boolean;
  exemptReason: string;
}

/**
 * Lidmaatschap en contributie van een lid (fase 23a): soort lidmaatschap, bij een partner het lid dat betaalt, en een
 * vrijstelling (bijvoorbeeld Convent). Zonder soort volgt de app het lidmaatschap uit e-Boekhouden.
 */
export function MembershipCard({ member }: { member: MemberDetail }) {
  const api = useApi();
  const settings = useMembershipSettings(member.id, true);
  const [form, setForm] = useState<Form | null>(null);
  const [search, setSearch] = useState('');
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    if (settings.data) {
      setForm({
        kind: settings.data.kind ?? '',
        payerMemberId: settings.data.payerMemberId ?? null,
        payerName: '',
        exempt: settings.data.exempt,
        exemptReason: settings.data.exemptReason ?? '',
      });
    }
  }, [settings.data]);

  const payer = useQuery({
    queryKey: ['member', form?.payerMemberId],
    enabled: !!form?.payerMemberId,
    queryFn: async () =>
      (await api.GET('/api/v1/admin/members/{id}', { params: { path: { id: form!.payerMemberId! } } })).data!,
  });
  const candidates = useQuery({
    queryKey: ['members', 'payer-search', search],
    enabled: form?.kind === 'Partner' && search.trim().length >= 2,
    queryFn: async () =>
      (await api.GET('/api/v1/admin/members', { params: { query: { search, status: 'Active', pageSize: 8 } } })).data!,
  });

  const save = useApiMutation(
    () =>
      api.PUT('/api/v1/admin/contributions/members/{memberId}', {
        params: { path: { memberId: member.id } },
        body: {
          kind: form?.kind || null,
          payerMemberId: form?.kind === 'Partner' ? form.payerMemberId : null,
          exempt: form?.exempt ?? false,
          exemptReason: form?.exempt ? form.exemptReason || null : null,
        },
      }),
    [['contributions'], ['member', member.id]],
  );

  if (!form) {
    return null;
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    setMessage(null);
    save.mutate(undefined, { onSuccess: () => setMessage('Lidmaatschap opgeslagen.') });
  }

  const set = (change: Partial<Form>) => setForm({ ...form!, ...change });
  return (
    <section className="card" aria-labelledby="lidmaatschap">
      <h2 id="lidmaatschap">Lidmaatschap en contributie</h2>
      <p className="card-hint">
        65+ bepaalt de app zelf op de incassodatum. Bij twee personen betaalt één lid; de partner koppel je hier aan dat
        lid.
      </p>
      <ProblemAlert error={settings.error} />
      <form onSubmit={submit}>
        <div className="field">
          <label htmlFor="soort-lidmaatschap">Soort lidmaatschap</label>
          <select
            id="soort-lidmaatschap"
            value={form.kind}
            onChange={(e) => set({ kind: e.target.value as MembershipKind | '', payerMemberId: null })}
          >
            <option value="">Volgt e-Boekhouden{member.ebStatusRaw ? ` (${member.ebStatusRaw})` : ''}</option>
            {(Object.keys(kindLabels) as MembershipKind[]).map((k) => (
              <option key={k} value={k}>
                {kindLabels[k]}
              </option>
            ))}
          </select>
        </div>
        {form.kind === 'Partner' ? (
          <div className="field">
            <span id="betaler-label">Betaald door</span>
            {form.payerMemberId ? (
              <p aria-labelledby="betaler-label">
                {payer.data ? (
                  <Link to="/leden/$id" params={{ id: form.payerMemberId }}>
                    {payer.data.fullName} ({payer.data.memberNumber})
                  </Link>
                ) : (
                  'Laden…'
                )}{' '}
                <button type="button" className="button ghost small" onClick={() => set({ payerMemberId: null })}>
                  Ander lid kiezen
                </button>
              </p>
            ) : (
              <>
                <Field
                  label="Zoek het lid dat betaalt"
                  hint="Naam of lidnummer; het lid moet een tweepersoonslidmaatschap hebben"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                />
                <ul className="list" aria-label="Zoekresultaten">
                  {(candidates.data?.items ?? [])
                    .filter((c) => c.id !== member.id)
                    .map((c) => (
                      <li key={c.id} className="list-row">
                        <span className="grow">
                          {c.fullName} ({c.memberNumber})
                        </span>
                        <button
                          type="button"
                          className="button ghost small"
                          onClick={() => set({ payerMemberId: c.id })}
                          aria-label={`${c.fullName} kiezen als betaler`}
                        >
                          Kiezen
                        </button>
                      </li>
                    ))}
                </ul>
              </>
            )}
          </div>
        ) : null}
        <label className="check">
          <input type="checkbox" checked={form.exempt} onChange={(e) => set({ exempt: e.target.checked })} />{' '}
          Vrijgesteld van contributie
        </label>
        {form.exempt ? (
          <Field
            label="Reden"
            hint="Bijvoorbeeld: Convent (bewezen dienstjaren)"
            maxLength={200}
            value={form.exemptReason}
            onChange={(e) => set({ exemptReason: e.target.value })}
          />
        ) : null}
        <ProblemAlert error={save.error} />
        <SuccessMessage message={message} />
        <div className="actions">
          <button
            type="submit"
            className="button secondary"
            disabled={save.isPending || (form.kind === 'Partner' && !form.payerMemberId)}
          >
            Lidmaatschap opslaan
          </button>
        </div>
      </form>
    </section>
  );
}
