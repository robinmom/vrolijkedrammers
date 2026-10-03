import { Link } from '@tanstack/react-router';
import { useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import {
  formatAmount,
  kindLabel,
  useContributionRates,
  useContributions,
  type ContributionLine,
} from '../api/contributions';
import { useApiMutation } from '../api/hooks';
import { ConfirmDialog } from '../components/Dialog';
import { Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDate } from '../format';

const statusLabels: Record<ContributionLine['status'], string> = {
  Due: 'Betaalt',
  Exempt: 'Vrijgesteld',
  PaidByPartner: 'Via partner',
  Unknown: 'Onbekend',
};

type Filter = 'all' | 'Due' | 'Exempt' | 'PaidByPartner' | 'attention';

function today() {
  return new Date().toISOString().slice(0, 10);
}

const emptyRate = {
  validFrom: '',
  onePerson: '',
  twoPersons: '',
  onePersonSenior: '',
  twoPersonsSenior: '',
  dansgarde: '',
};

function RatesCard() {
  const api = useApi();
  const rates = useContributionRates();
  const [form, setForm] = useState(emptyRate);
  const [message, setMessage] = useState<string | null>(null);
  const [removing, setRemoving] = useState<number | null>(null);

  const save = useApiMutation(
    () =>
      api.PUT('/api/v1/admin/contributions/rates', {
        body: {
          validFrom: form.validFrom,
          onePerson: Number(form.onePerson.replace(',', '.')),
          twoPersons: Number(form.twoPersons.replace(',', '.')),
          onePersonSenior: Number(form.onePersonSenior.replace(',', '.')),
          twoPersonsSenior: Number(form.twoPersonsSenior.replace(',', '.')),
          dansgarde: Number(form.dansgarde.replace(',', '.')),
        },
      }),
    [['contributions']],
  );
  const remove = useApiMutation(
    (id: number) => api.DELETE('/api/v1/admin/contributions/rates/{id}', { params: { path: { id } } }),
    [['contributions']],
  );

  function submit(event: FormEvent) {
    event.preventDefault();
    setMessage(null);
    save.mutate(undefined, {
      onSuccess: () => {
        setMessage('Tarief opgeslagen.');
        setForm(emptyRate);
      },
    });
  }

  const amountFields: [keyof typeof emptyRate, string][] = [
    ['onePerson', 'Eén persoon'],
    ['twoPersons', 'Twee personen'],
    ['onePersonSenior', 'Eén persoon 65+'],
    ['twoPersonsSenior', 'Twee personen 65+'],
    ['dansgarde', 'Dansgarde'],
  ];

  return (
    <section className="card" aria-labelledby="tarieven">
      <h2 id="tarieven">Tarieven per jaar</h2>
      <p className="card-hint">
        Het tarief met de laatste ingangsdatum vóór of op de incassodatum telt. Met een bestaande ingangsdatum pas je
        dat tarief aan.
      </p>
      <ProblemAlert error={rates.error ?? remove.error} />
      {rates.data ? (
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Tarieven">
          <table className="table compact">
            <caption className="visually-hidden">Tarieven</caption>
            <thead>
              <tr>
                <th scope="col">Vanaf</th>
                {amountFields.map(([, label]) => (
                  <th key={label} scope="col">
                    {label}
                  </th>
                ))}
                <th scope="col">
                  <span className="visually-hidden">Acties</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {rates.data.map((r) => (
                <tr key={r.id}>
                  <td>{formatDate(r.validFrom)}</td>
                  <td>{formatAmount(r.onePerson)}</td>
                  <td>{formatAmount(r.twoPersons)}</td>
                  <td>{formatAmount(r.onePersonSenior)}</td>
                  <td>{formatAmount(r.twoPersonsSenior)}</td>
                  <td>{formatAmount(r.dansgarde)}</td>
                  <td>
                    {rates.data.length > 1 ? (
                      <button
                        type="button"
                        className="button ghost small"
                        aria-label={`Tarief vanaf ${formatDate(r.validFrom)} verwijderen`}
                        onClick={() => setRemoving(r.id ?? null)}
                      >
                        Verwijderen
                      </button>
                    ) : null}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
      <form onSubmit={submit}>
        <h3>Tarief toevoegen of aanpassen</h3>
        <div className="grid-3">
          <Field
            label="Geldig vanaf"
            type="date"
            required
            value={form.validFrom}
            onChange={(e) => setForm({ ...form, validFrom: e.target.value })}
          />
          {amountFields.map(([key, label]) => (
            <Field
              key={key}
              label={`${label} (€)`}
              inputMode="decimal"
              required
              pattern="\d+([.,]\d{1,2})?"
              value={form[key]}
              onChange={(e) => setForm({ ...form, [key]: e.target.value })}
            />
          ))}
        </div>
        <ProblemAlert error={save.error} />
        <SuccessMessage message={message} />
        <div className="actions">
          <button type="submit" className="button secondary" disabled={save.isPending}>
            Tarief opslaan
          </button>
        </div>
      </form>
      <ConfirmDialog
        open={removing !== null}
        title="Tarief verwijderen?"
        message="Dit tarief geldt daarna niet meer; het vorige tarief telt dan door."
        confirmLabel="Verwijderen"
        busy={remove.isPending}
        onConfirm={() => remove.mutate(removing!, { onSuccess: () => setRemoving(null) })}
        onCancel={() => setRemoving(null)}
      />
    </section>
  );
}

/**
 * Contributie (fase 23a): wat elk actief lid betaalt op de peildatum (straks de incassodatum). Senior (65+) volgt uit de
 * leeftijd op die datum; bij twee personen alleen als beiden 65+ zijn. Lidmaatschap en vrijstelling stel je in bij het lid.
 */
export function ContributionsPage() {
  const api = useApi();
  const [date, setDate] = useState(today());
  const [filter, setFilter] = useState<Filter>('all');
  const overview = useContributions(date);
  const [exportError, setExportError] = useState<unknown>(null);

  async function exportExcel() {
    setExportError(null);
    try {
      const { data } = await api.GET('/api/v1/admin/contributions/export', {
        params: { query: { date } },
        parseAs: 'blob',
      });
      if (data) {
        const url = URL.createObjectURL(data);
        const link = document.createElement('a');
        link.href = url;
        link.download = `contributie-${date.replaceAll('-', '')}.xlsx`;
        link.click();
        URL.revokeObjectURL(url);
      }
    } catch (error) {
      setExportError(error);
    }
  }

  const o = overview.data;
  const attention = (l: ContributionLine) => l.status === 'Unknown' || !!l.note;
  const lines = (o?.lines ?? []).filter((l) =>
    filter === 'all' ? true : filter === 'attention' ? attention(l) : l.status === filter,
  );
  const count = (f: Filter) =>
    (o?.lines ?? []).filter((l) => (f === 'attention' ? attention(l) : f === 'all' ? true : l.status === f)).length;

  return (
    <>
      <div className="page-header">
        <h1>Contributie</h1>
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => void exportExcel()} disabled={!o}>
            Exporteren (Excel)
          </button>
        </div>
      </div>
      <ProblemAlert error={exportError ?? overview.error} />
      <section className="card" aria-labelledby="peildatum">
        <h2 id="peildatum" className="visually-hidden">
          Peildatum
        </h2>
        <Field
          label="Peildatum (incassodatum)"
          hint="Bepaalt het tarief en wie 65+ is"
          type="date"
          required
          value={date}
          onChange={(e) => e.target.value && setDate(e.target.value)}
        />
      </section>
      {o ? (
        <>
          <section className="kpis" aria-label="Totalen">
            {o.totals.map((t) => (
              <div key={t.label} className="kpi">
                <span className="kpi-label">{t.label}</span>
                <span className="kpi-value">{formatAmount(t.amount)}</span>
                <span className="kpi-hint">{t.count} leden</span>
              </div>
            ))}
            <div className="kpi">
              <span className="kpi-label">Totaal</span>
              <span className="kpi-value">{formatAmount(o.total)}</span>
              <span className="kpi-hint">{count('Due')} betalers</span>
            </div>
          </section>

          <section className="card" aria-labelledby="per-lid">
            <div className="card-header">
              <h2 id="per-lid">Per lid</h2>
              <select aria-label="Filter" value={filter} onChange={(e) => setFilter(e.target.value as Filter)}>
                <option value="all">Alle actieve leden ({count('all')})</option>
                <option value="Due">Betalen ({count('Due')})</option>
                <option value="Exempt">Vrijgesteld ({count('Exempt')})</option>
                <option value="PaidByPartner">Via partner ({count('PaidByPartner')})</option>
                <option value="attention">Aandacht nodig ({count('attention')})</option>
              </select>
            </div>
            <div className="table-scroll" tabIndex={0} role="region" aria-label="Contributie per lid">
              <table className="table compact">
                <caption className="visually-hidden">Contributie per lid</caption>
                <thead>
                  <tr>
                    <th scope="col">Lidnummer</th>
                    <th scope="col">Naam</th>
                    <th scope="col">Lidmaatschap</th>
                    <th scope="col">Bedrag</th>
                    <th scope="col">Status</th>
                    <th scope="col">Opmerking</th>
                  </tr>
                </thead>
                <tbody>
                  {lines.map((l) => (
                    <tr key={l.memberId}>
                      <td>{l.memberNumber}</td>
                      <td>
                        <Link to="/leden/$id" params={{ id: l.memberId }}>
                          {l.fullName}
                        </Link>
                      </td>
                      <td>
                        {kindLabel(l.kind, l.senior)}
                        {l.kindFromEBoekhouden && l.kind ? <span className="muted"> (e-Boekhouden)</span> : null}
                      </td>
                      <td>{formatAmount(l.amount)}</td>
                      <td>{statusLabels[l.status]}</td>
                      <td>{l.note ?? (l.partnerName ? `Partner: ${l.partnerName}` : '')}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        </>
      ) : overview.error ? null : (
        <p>Laden…</p>
      )}
      <RatesCard />
    </>
  );
}
