import { useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { formatAmount, useContributionRates } from '../api/contributions';
import { useApiMutation } from '../api/hooks';
import { formatDate } from '../format';
import { ConfirmDialog } from './Dialog';
import { Field } from './Field';
import { ProblemAlert, SuccessMessage } from './ProblemAlert';

const emptyRate = {
  validFrom: '',
  onePerson: '',
  twoPersons: '',
  onePersonSenior: '',
  twoPersonsSenior: '',
  dansgarde: '',
};

/** Tarieven per ingangsdatum (fase 23a; sinds fase 25 op de pagina Lidmaatschappen). */
export function RatesCard() {
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
    ['onePerson', 'Lid'],
    ['twoPersons', 'Combinatie'],
    ['onePersonSenior', 'Lid 65+'],
    ['twoPersonsSenior', 'Combinatie 65+'],
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
