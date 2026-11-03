import { Link } from '@tanstack/react-router';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import {
  KIND_LABELS,
  PAYMENT_LABELS,
  euro,
  useAdvertisersAwaitingReview,
  useDeletedAdvertisers,
  type AdvertiserReviewItem,
} from '../api/advertisers';
import { useApiMutation } from '../api/hooks';
import { ConfirmDialog, Dialog } from '../components/Dialog';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { formatDateTime } from '../format';

/** De queries die na een beoordeling opnieuw geladen moeten worden. */
const keys = [['advertisers']];

/**
 * Adverteerders → Nieuwe adverteerders (fase 27s): de aanmeldingen die collectanten in de app doen. Zolang ze hier
 * staan tellen ze niet mee in de campagne, de export, de facturen of de incasso.
 */
export function NewAdvertisersPage() {
  const api = useApi();
  const pending = useAdvertisersAwaitingReview();
  const [message, setMessage] = useState<string | null>(null);
  const [question, setQuestion] = useState<AdvertiserReviewItem | null>(null);
  const [reject, setReject] = useState<AdvertiserReviewItem | null>(null);
  const [text, setText] = useState('');

  const approve = useApiMutation(
    (id: string) => api.POST('/api/v1/admin/advertisers/{id}/approve', { params: { path: { id } } }),
    keys,
  );
  const ask = useApiMutation(
    (v: { id: string; question: string }) =>
      api.POST('/api/v1/admin/advertisers/{id}/request-info', {
        params: { path: { id: v.id } },
        body: { question: v.question },
      }),
    keys,
  );
  const refuse = useApiMutation(
    (v: { id: string; reason: string }) =>
      api.POST('/api/v1/admin/advertisers/{id}/reject', {
        params: { path: { id: v.id } },
        body: { reason: v.reason },
      }),
    keys,
  );

  const rows = pending.data ?? [];
  return (
    <>
      <h1>Nieuwe adverteerders</h1>
      <p>
        Aanmeldingen die collectanten in de app hebben gedaan. Zolang een aanmelding hier staat, telt hij niet mee in
        de campagne, de export, de facturen of de incasso. Kijk de gegevens na, keur goed, of wijs af met een reden.
      </p>
      <p className="muted">
        Klopt er iets niet? Open de adverteerder, pas de gegevens aan en keur daarna goed. Mis je gegevens die de
        collectant moet aanleveren, zet de aanmelding dan op &quot;meer info nodig&quot; met je vraag erbij en neem
        contact met hem op; de vraag blijft hier staan.
      </p>

      <SuccessMessage message={message} />
      <ProblemAlert error={pending.error ?? approve.error ?? ask.error ?? refuse.error} />

      {rows.length === 0 ? (
        <section className="card">
          <p>Geen nieuwe aanmeldingen.</p>
        </section>
      ) : null}

      {rows.map((row) => (
        <section className="card" key={row.id} aria-label={row.companyName}>
          <div className="page-header">
            <div>
              <h2>
                <Link to="/adverteerders/$id" params={{ id: row.id }}>
                  {row.number} {row.companyName}
                </Link>
                {row.review === 'InfoNeeded' ? <span className="badge warn"> meer info nodig</span> : null}
              </h2>
              <p className="muted">
                Aangemeld door {row.collectorName ?? 'onbekend'} op {formatDateTime(row.addedAt)}
              </p>
            </div>
          </div>
          <dl className="details">
            <dt>Contactpersoon</dt>
            <dd>{row.contactName ?? '—'}</dd>
            <dt>Telefoon</dt>
            <dd>{row.phone ?? '—'}</dd>
            <dt>E-mailadres</dt>
            <dd>{row.email ?? '—'}</dd>
            <dt>Plaats</dt>
            <dd>{row.city ?? '—'}</dd>
            <dt>Soort</dt>
            <dd>{KIND_LABELS[row.kind]}</dd>
            <dt>Betaling</dt>
            <dd>{PAYMENT_LABELS[row.payment]}</dd>
            <dt>Bijdrage</dt>
            <dd>{row.amount === null || row.amount === undefined ? '—' : euro(row.amount)}</dd>
          </dl>
          {row.note ? <p>Opmerking van de collectant: {row.note}</p> : null}
          {row.question ? <p className="badge warn">Jouw vraag: {row.question}</p> : null}
          <div className="actions">
            <button
              type="button"
              className="button"
              disabled={approve.isPending}
              onClick={() =>
                approve.mutate(row.id, {
                  onSuccess: () => setMessage(`${row.companyName} is goedgekeurd en doet mee in de campagne.`),
                })
              }
            >
              Goedkeuren
            </button>
            <button
              type="button"
              className="button secondary"
              onClick={() => {
                setText(row.question ?? '');
                setQuestion(row);
              }}
            >
              Meer info nodig
            </button>
            <button
              type="button"
              className="button danger"
              onClick={() => {
                setText('');
                setReject(row);
              }}
            >
              Afwijzen
            </button>
          </div>
        </section>
      ))}

      <Dialog open={question !== null} title="Welke informatie mis je?" onClose={() => setQuestion(null)}>
        <div className="field">
          <label htmlFor="vraag">Vraag aan de collectant</label>
          <textarea
            id="vraag"
            rows={3}
            maxLength={500}
            value={text}
            onChange={(e) => setText(e.target.value)}
          />
          <small className="muted">
            De vraag blijft bij de aanmelding staan. De collectant krijgt er geen melding van, dus neem zelf contact
            met hem op.
          </small>
        </div>
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => setQuestion(null)}>
            Annuleren
          </button>
          <button
            type="button"
            className="button"
            disabled={ask.isPending || text.trim() === ''}
            onClick={() =>
              question &&
              ask.mutate(
                { id: question.id, question: text.trim() },
                {
                  onSuccess: () => {
                    setMessage(`Vraag bij ${question.companyName} gezet.`);
                    setQuestion(null);
                  },
                },
              )
            }
          >
            Opslaan
          </button>
        </div>
      </Dialog>

      <Dialog open={reject !== null} title="Aanmelding afwijzen" onClose={() => setReject(null)}>
        <p>
          {reject?.companyName} gaat naar de verwijderde adverteerders. Daar kun je hem terugzetten als je je
          bedenkt.
        </p>
        <div className="field">
          <label htmlFor="afwijsreden">Reden</label>
          <textarea
            id="afwijsreden"
            rows={3}
            maxLength={500}
            value={text}
            onChange={(e) => setText(e.target.value)}
          />
        </div>
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => setReject(null)}>
            Annuleren
          </button>
          <button
            type="button"
            className="button danger"
            disabled={refuse.isPending || text.trim() === ''}
            onClick={() =>
              reject &&
              refuse.mutate(
                { id: reject.id, reason: text.trim() },
                {
                  onSuccess: () => {
                    setMessage(`${reject.companyName} is afgewezen.`);
                    setReject(null);
                  },
                },
              )
            }
          >
            Afwijzen
          </button>
        </div>
      </Dialog>
    </>
  );
}

/** Adverteerders → Verwijderde adverteerders (fase 27s): terugzetten of definitief wissen. */
export function DeletedAdvertisersPage() {
  const api = useApi();
  const deleted = useDeletedAdvertisers();
  const [message, setMessage] = useState<string | null>(null);
  const [purge, setPurge] = useState<{ id: string; companyName: string } | null>(null);

  const restore = useApiMutation(
    (id: string) => api.POST('/api/v1/admin/advertisers/{id}/restore', { params: { path: { id } } }),
    keys,
  );
  const remove = useApiMutation(
    (id: string) => api.DELETE('/api/v1/admin/advertisers/{id}/permanent', { params: { path: { id } } }),
    keys,
  );

  const rows = deleted.data ?? [];
  return (
    <>
      <h1>Verwijderde adverteerders</h1>
      <p>
        Verwijderde en afgewezen adverteerders. Ze tellen nergens meer mee. Terugzetten kan altijd; definitief wissen
        alleen als er geen facturen aan de adverteerder hangen, want een verstuurde factuur moet in de administratie
        blijven.
      </p>

      <SuccessMessage message={message} />
      <ProblemAlert error={deleted.error ?? restore.error ?? remove.error} />

      <p className="muted">{rows.length} verwijderd</p>
      <div className="table-scroll table-wrapper" tabIndex={0} role="region" aria-label="Verwijderde adverteerders">
        <table className="table">
          <caption className="visually-hidden">Verwijderde adverteerders</caption>
          <thead>
            <tr>
              <th scope="col">Nr.</th>
              <th scope="col">Bedrijf</th>
              <th scope="col">Collectant</th>
              <th scope="col">Reden</th>
              <th scope="col">Verwijderd op</th>
              <th scope="col">Facturen</th>
              <th scope="col">Acties</th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr>
                <td colSpan={7}>Er is niets verwijderd.</td>
              </tr>
            ) : null}
            {rows.map((row) => (
              <tr key={row.id}>
                <td>{row.number}</td>
                <td>
                  {row.companyName}
                  {row.city ? <span className="muted"> · {row.city}</span> : null}
                </td>
                <td>{row.collectorName ?? '—'}</td>
                <td>{row.reason ?? '—'}</td>
                <td>{formatDateTime(row.deletedAt)}</td>
                <td>{row.invoiceCount === 0 ? '—' : row.invoiceCount}</td>
                <td className="actions">
                  <button
                    type="button"
                    className="button secondary small"
                    disabled={restore.isPending}
                    onClick={() =>
                      restore.mutate(row.id, {
                        onSuccess: () => setMessage(`${row.companyName} staat weer in de lijst.`),
                      })
                    }
                  >
                    Terugzetten
                  </button>
                  {row.invoiceCount === 0 ? (
                    <button
                      type="button"
                      className="button danger small"
                      onClick={() => setPurge({ id: row.id, companyName: row.companyName })}
                    >
                      Definitief verwijderen
                    </button>
                  ) : (
                    <span className="muted">
                      Blijft bewaard: {row.invoiceCount === 1 ? '1 factuur' : `${row.invoiceCount} facturen`}
                    </span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <ConfirmDialog
        open={purge !== null}
        title="Definitief verwijderen"
        message={`${purge?.companyName ?? ''} wordt met zijn hele bijdragehistorie gewist. Dit kun je niet ongedaan maken.`}
        confirmLabel="Definitief verwijderen"
        busy={remove.isPending}
        onCancel={() => setPurge(null)}
        onConfirm={() =>
          purge &&
          remove.mutate(purge.id, {
            onSuccess: () => {
              setMessage(`${purge.companyName} is definitief verwijderd.`);
              setPurge(null);
            },
          })
        }
      />
    </>
  );
}
