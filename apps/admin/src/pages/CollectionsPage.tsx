import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from '@tanstack/react-router';
import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { formatAmount } from '../api/contributions';
import { useApiMutation, type Schemas } from '../api/hooks';
import { ConfirmDialog } from '../components/Dialog';
import { Field } from '../components/Field';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDate, formatDateTime } from '../format';

type Preview = Schemas['CollectionPreview'];

function CreditorCard() {
  const api = useApi();
  const creditor = useQuery({
    queryKey: ['collections', 'creditor'],
    queryFn: async () => (await api.GET('/api/v1/admin/collections/creditor')).data!,
  });
  const [form, setForm] = useState({ name: '', iban: '', creditorId: '' });
  const [message, setMessage] = useState<string | null>(null);
  useEffect(() => {
    if (creditor.data) {
      setForm({
        name: creditor.data.name ?? '',
        iban: creditor.data.iban ?? '',
        creditorId: creditor.data.creditorId ?? '',
      });
    }
  }, [creditor.data]);
  const save = useApiMutation(() => api.PUT('/api/v1/admin/collections/creditor', { body: form }), [['collections']]);

  function submit(event: FormEvent) {
    event.preventDefault();
    setMessage(null);
    save.mutate(undefined, { onSuccess: () => setMessage('Gegevens van de vereniging opgeslagen.') });
  }

  return (
    <section className="card" aria-labelledby="incassant">
      <h2 id="incassant">Vereniging als incassant</h2>
      <p className="card-hint">
        Staat in elk incassobestand. Het incassant-ID staat in het incassocontract met de bank (begint met NL en bevat
        ZZZ).
      </p>
      <ProblemAlert error={creditor.error ?? save.error} />
      <SuccessMessage message={message} />
      <form onSubmit={submit}>
        <div className="grid-3">
          <Field
            label="Naam"
            required
            maxLength={70}
            value={form.name}
            onChange={(e) => setForm({ ...form, name: e.target.value })}
          />
          <Field
            label="IBAN van de vereniging"
            required
            maxLength={40}
            value={form.iban}
            onChange={(e) => setForm({ ...form, iban: e.target.value })}
          />
          <Field
            label="Incassant-ID"
            required
            maxLength={35}
            value={form.creditorId}
            onChange={(e) => setForm({ ...form, creditorId: e.target.value })}
          />
        </div>
        <div className="actions">
          <button type="submit" className="button secondary" disabled={save.isPending}>
            Opslaan
          </button>
        </div>
      </form>
    </section>
  );
}

/** Het voorbeeld van een incasso; ook voor de adverteerders (fase 27c), dan met nummer, bedrijf en soort. */
export function PreviewTable({ preview, advertisers = false }: { preview: Preview; advertisers?: boolean }) {
  const who = advertisers ? 'Adverteerders' : 'Leden';
  const link = (id: string, name: string) =>
    advertisers ? (
      <Link to="/adverteerders/$id" params={{ id }}>
        {name}
      </Link>
    ) : (
      <Link to="/leden/$id" params={{ id }}>
        {name}
      </Link>
    );
  return (
    <>
      <section className="kpis" aria-label="Voorbeeld van de incasso">
        <div className="kpi">
          <span className="kpi-label">Incasso&apos;s</span>
          <span className="kpi-value">{preview.count}</span>
        </div>
        <div className="kpi">
          <span className="kpi-label">Totaal</span>
          <span className="kpi-value">{formatAmount(preview.total)}</span>
        </div>
        <div className="kpi">
          <span className="kpi-label">Overgeslagen</span>
          <span className="kpi-value">{preview.skipped.length}</span>
        </div>
      </section>
      {preview.warnings.map((w) => (
        <p key={w} className="alert alert-warning" role="status">
          {w}
        </p>
      ))}
      <div className="table-scroll" tabIndex={0} role="region" aria-label={`${who} in de incasso`}>
        <table className="table compact">
          <caption className="visually-hidden">{who} in de incasso</caption>
          <thead>
            <tr>
              <th scope="col">{advertisers ? 'Nr.' : 'Lidnummer'}</th>
              <th scope="col">{advertisers ? 'Bedrijf' : 'Naam'}</th>
              <th scope="col">{advertisers ? 'Soort' : 'Lidmaatschap'}</th>
              <th scope="col">Bedrag</th>
              <th scope="col">IBAN</th>
              <th scope="col">Machtiging</th>
              <th scope="col">Type</th>
            </tr>
          </thead>
          <tbody>
            {preview.lines.map((l) => (
              <tr key={l.memberId}>
                <td>{l.memberNumber}</td>
                <td>{link(l.memberId, l.fullName)}</td>
                <td>{l.kind}</td>
                <td>{formatAmount(l.amount)}</td>
                <td>{l.ibanMasked}</td>
                <td>
                  {l.mandateReference}
                  {l.warning ? <div className="muted small-text">{l.warning}</div> : null}
                </td>
                <td>{l.sequenceType === 'Frst' ? 'Eerste' : 'Herhaald'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {preview.skipped.length > 0 ? (
        <>
          <h3>Overgeslagen</h3>
          <div className="table-scroll" tabIndex={0} role="region" aria-label={`Overgeslagen ${who.toLowerCase()}`}>
            <table className="table compact">
              <caption className="visually-hidden">Overgeslagen {who.toLowerCase()}</caption>
              <thead>
                <tr>
                  <th scope="col">{advertisers ? 'Nr.' : 'Lidnummer'}</th>
                  <th scope="col">{advertisers ? 'Bedrijf' : 'Naam'}</th>
                  <th scope="col">Bedrag</th>
                  <th scope="col">Reden</th>
                </tr>
              </thead>
              <tbody>
                {preview.skipped.map((s) => (
                  <tr key={s.memberId}>
                    <td>{s.memberNumber}</td>
                    <td>{link(s.memberId, s.fullName)}</td>
                    <td>{formatAmount(s.amount)}</td>
                    <td>{s.reason}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      ) : null}
    </>
  );
}

/**
 * SEPA-incasso (fase 23c): kies een incassodatum, bekijk wie meedoet (en wie niet, met reden), maak de run en download
 * het pain.008-bestand. Lever het aan in Rabo Internetbankieren; pas na ondertekenen wordt er geïncasseerd.
 */
export function CollectionsPage() {
  const api = useApi();
  const queryClient = useQueryClient();
  const [date, setDate] = useState('');
  const [description, setDescription] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [confirm, setConfirm] = useState(false);
  const [removing, setRemoving] = useState<string | null>(null);
  const [downloadError, setDownloadError] = useState<unknown>(null);

  const preview = useQuery({
    queryKey: ['collections', 'preview', date],
    enabled: date.length === 10,
    queryFn: async () => (await api.GET('/api/v1/admin/collections/preview', { params: { query: { date } } })).data!,
  });
  const runs = useQuery({
    queryKey: ['collections', 'runs'],
    queryFn: async () => (await api.GET('/api/v1/admin/collections')).data!,
  });
  const create = useApiMutation(
    () => api.POST('/api/v1/admin/collections', { body: { date, description: description.trim() || null } }),
    [['collections']],
  );
  const remove = useApiMutation(
    (id: string) => api.DELETE('/api/v1/admin/collections/{id}', { params: { path: { id } } }),
    [['collections']],
  );

  async function download(id: string, collectionDate: string) {
    setDownloadError(null);
    try {
      const { data } = await api.GET('/api/v1/admin/collections/{id}/file', {
        params: { path: { id } },
        parseAs: 'blob',
      });
      if (data) {
        const url = URL.createObjectURL(data);
        const link = document.createElement('a');
        link.href = url;
        link.download = `incasso-${collectionDate.replaceAll('-', '')}.xml`;
        link.click();
        URL.revokeObjectURL(url);
        await queryClient.invalidateQueries({ queryKey: ['collections', 'runs'] });
      }
    } catch (error) {
      setDownloadError(error);
    }
  }

  return (
    <>
      <div className="page-header">
        <h1>Incasso</h1>
      </div>
      <ProblemAlert error={runs.error ?? create.error ?? remove.error ?? downloadError} />
      <SuccessMessage message={message} />

      <CreditorCard />

      <section className="card" aria-labelledby="nieuwe-run">
        <h2 id="nieuwe-run">Nieuwe incasso</h2>
        <p className="card-hint">
          Het bedrag per lid volgt uit <Link to="/contributie">Contributie</Link> op de incassodatum (ook 65+). Leden
          zonder IBAN of machtiging worden overgeslagen.
        </p>
        <div className="grid-3">
          <Field label="Incassodatum" type="date" required value={date} onChange={(e) => setDate(e.target.value)} />
          <Field
            label="Omschrijving (op het afschrift)"
            hint="Leeg = Contributie [jaar] CV De Vrolijke Drammers"
            maxLength={100}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
        </div>
        <ProblemAlert error={preview.error} />
        {preview.data ? <PreviewTable preview={preview.data} /> : null}
        {preview.data ? (
          <div className="actions">
            <button
              type="button"
              className="button"
              disabled={!preview.data.creditorComplete || preview.data.count === 0 || create.isPending}
              onClick={() => setConfirm(true)}
            >
              Incassorun maken ({preview.data.count})
            </button>
          </div>
        ) : null}
      </section>

      <section className="card" aria-labelledby="runs">
        <h2 id="runs">Incassoruns</h2>
        <p className="card-hint">
          Lever het bestand aan in Rabo Internetbankieren (Betalen → Incasso&apos;s → bestand aanleveren). De bank
          controleert het bestand; er wordt pas geïncasseerd nadat je het ondertekent.
        </p>
        {runs.data && runs.data.length === 0 ? <p className="muted">Nog geen incassoruns.</p> : null}
        {runs.data && runs.data.length > 0 ? (
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Incassoruns">
            <table className="table compact">
              <caption className="visually-hidden">Incassoruns</caption>
              <thead>
                <tr>
                  <th scope="col">Incassodatum</th>
                  <th scope="col">Omschrijving</th>
                  <th scope="col">Aantal</th>
                  <th scope="col">Totaal</th>
                  <th scope="col">Gedownload</th>
                  <th scope="col">
                    <span className="visually-hidden">Acties</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {runs.data.map((r) => (
                  <tr key={r.id}>
                    <td>{formatDate(r.collectionDate)}</td>
                    <td>{r.description}</td>
                    <td>{r.lineCount}</td>
                    <td>{formatAmount(r.total)}</td>
                    <td>{r.exportedAt ? formatDateTime(r.exportedAt) : 'Nog niet'}</td>
                    <td>
                      <div className="actions">
                        <button
                          type="button"
                          className="button secondary small"
                          aria-label={`Bestand downloaden: incasso ${formatDate(r.collectionDate)}`}
                          onClick={() => void download(r.id, r.collectionDate)}
                        >
                          Bestand downloaden
                        </button>
                        <button
                          type="button"
                          className="button ghost small"
                          aria-label={`Incasso ${formatDate(r.collectionDate)} verwijderen`}
                          onClick={() => setRemoving(r.id)}
                        >
                          Verwijderen
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </section>

      <ConfirmDialog
        open={confirm}
        title="Incassorun maken?"
        message={`Er wordt een run gemaakt met ${preview.data?.count ?? 0} incasso's (${formatAmount(preview.data?.total ?? 0)}) op ${formatDate(date)}. Daarna download je het bestand voor de bank.`}
        confirmLabel="Run maken"
        busy={create.isPending}
        onConfirm={() =>
          create.mutate(undefined, {
            onSuccess: () => {
              setConfirm(false);
              setMessage('De incassorun is gemaakt. Download het bestand hieronder.');
            },
          })
        }
        onCancel={() => setConfirm(false)}
      />
      <ConfirmDialog
        open={removing !== null}
        title="Incassorun verwijderen?"
        message="Verwijder alleen een run die niet bij de bank is aangeleverd, of die je daar hebt geannuleerd."
        confirmLabel="Verwijderen"
        busy={remove.isPending}
        onConfirm={() => remove.mutate(removing!, { onSuccess: () => setRemoving(null) })}
        onCancel={() => setRemoving(null)}
      />
    </>
  );
}
