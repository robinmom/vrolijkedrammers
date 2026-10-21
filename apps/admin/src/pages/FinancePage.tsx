import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { euro } from '../api/advertisers';
import { useApiMutation } from '../api/hooks';
import { ProblemAlert } from '../components/ProblemAlert';

function formatDate(date: string | null | undefined) {
  if (!date) return '—';
  const [y, m, d] = date.split('-');
  return `${d}-${m}-${y}`;
}

/** Inkomsten (fase 4a): adverteerders-facturen en festival-betalingen samen, met een CSV-export om in SnelStart over te nemen. */
export function IncomePage() {
  const api = useApi();
  const [downloadError, setDownloadError] = useState<unknown>(null);

  const income = useQuery({
    queryKey: ['finance', 'income'],
    queryFn: async () => (await api.GET('/api/v1/admin/finance/income')).data!,
  });

  async function download(year: number) {
    setDownloadError(null);
    try {
      const { data } = await api.GET('/api/v1/admin/finance/income/export', { parseAs: 'blob' });
      if (data) {
        const url = URL.createObjectURL(data as Blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `Inkomsten ${year}.csv`;
        link.click();
        URL.revokeObjectURL(url);
      }
    } catch (error) {
      setDownloadError(error);
    }
  }

  const r = income.data;
  return (
    <>
      <div className="page-header">
        <div>
          <h1>Inkomsten {r ? r.year : ''}</h1>
          <p className="muted">
            Adverteerders-facturen en festival-betalingen van het campagnejaar, met de btw-uitsplitsing en de stand — als
            bron om in SnelStart over te nemen.
          </p>
        </div>
        {r ? (
          <button type="button" className="button secondary" onClick={() => void download(r.year)}>
            Exporteren (CSV)
          </button>
        ) : null}
      </div>
      <ProblemAlert error={income.error ?? downloadError} />

      {r ? (
        <section className="card">
          <dl className="totals">
            <div>
              <dt>Excl. btw</dt>
              <dd>{euro(r.totals.net)}</dd>
            </div>
            <div>
              <dt>Btw</dt>
              <dd>{euro(r.totals.vat)}</dd>
            </div>
            <div>
              <dt>Totaal incl. btw</dt>
              <dd>{euro(r.totals.total)}</dd>
            </div>
            <div>
              <dt>Waarvan betaald</dt>
              <dd>{euro(r.totals.paid)}</dd>
            </div>
          </dl>
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Inkomsten">
            <table className="table compact">
              <thead>
                <tr>
                  <th scope="col">Bron</th>
                  <th scope="col">Nummer</th>
                  <th scope="col">Partij</th>
                  <th scope="col">Datum</th>
                  <th scope="col">Excl. btw</th>
                  <th scope="col">Btw</th>
                  <th scope="col">Totaal</th>
                  <th scope="col">Betaalwijze</th>
                  <th scope="col">Status</th>
                  <th scope="col">Betaald op</th>
                </tr>
              </thead>
              <tbody>
                {r.rows.map((row) => (
                  <tr key={`${row.source}-${row.number}`}>
                    <td>{row.source}</td>
                    <td>{row.number}</td>
                    <td>{row.party}</td>
                    <td>{formatDate(row.date)}</td>
                    <td>{euro(row.netAmount)}</td>
                    <td>{euro(row.vatAmount)}</td>
                    <td>{euro(row.totalAmount)}</td>
                    <td>{row.paymentMethod}</td>
                    <td>
                      <span className={row.status === 'Betaald' ? 'badge ok' : row.status === 'Vervallen' ? 'badge warn' : ''}>
                        {row.status}
                      </span>
                    </td>
                    <td>{formatDate(row.paidAt)}</td>
                  </tr>
                ))}
                {r.rows.length === 0 ? (
                  <tr>
                    <td colSpan={10} className="muted">
                      Nog geen facturen of festival-betalingen in {r.year}.
                    </td>
                  </tr>
                ) : null}
              </tbody>
            </table>
          </div>
        </section>
      ) : (
        <p>Laden…</p>
      )}
    </>
  );
}

function statusBadge(status: string) {
  return status === 'Uitbetaald' ? 'badge ok' : status === 'Mislukt' ? 'badge warn' : 'badge';
}

/** Afrekeningen (fase 4b): de Mollie-uitbetalingen naar de bank (omzet − Mollie-kosten = netto), met per uitbetaling de herleide inkomsten en een verzamelstaat voor de boekhouding. */
export function SettlementsPage() {
  const api = useApi();
  const [selected, setSelected] = useState<string | null>(null);
  const [recipient, setRecipient] = useState('penningmeester@vrolijkedrammers.nl');
  const [sent, setSent] = useState<string | null>(null);
  const [downloadError, setDownloadError] = useState<unknown>(null);

  const settlements = useQuery({
    queryKey: ['finance', 'settlements'],
    queryFn: async () => (await api.GET('/api/v1/admin/finance/settlements')).data!,
  });

  const detail = useQuery({
    queryKey: ['finance', 'settlements', selected],
    enabled: selected !== null,
    queryFn: async () => (await api.GET('/api/v1/admin/finance/settlements/{id}', { params: { path: { id: selected! } } })).data!,
  });

  const send = useApiMutation(
    async (id: string) => api.POST('/api/v1/admin/finance/settlements/{id}/send', { params: { path: { id } }, body: { recipient } }),
    [],
  );

  async function download(id: string, name: string) {
    setDownloadError(null);
    try {
      const { data } = await api.GET('/api/v1/admin/finance/settlements/{id}/export', { params: { path: { id } }, parseAs: 'blob' });
      if (data) {
        const url = URL.createObjectURL(data as Blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `Afrekening ${name}.csv`;
        link.click();
        URL.revokeObjectURL(url);
      }
    } catch (error) {
      setDownloadError(error);
    }
  }

  const list = settlements.data;
  const d = detail.data;
  return (
    <>
      <div className="page-header">
        <div>
          <h1>Afrekeningen</h1>
          <p className="muted">
            De uitbetalingen van Mollie naar de bankrekening: per uitbetaling de omzet, de Mollie-kosten en het netto bedrag (de
            bankregel), met de inkomsten die erin zitten. De verzamelstaat is als CSV te downloaden of te mailen voor de boekhouding.
          </p>
        </div>
      </div>
      <ProblemAlert error={settlements.error ?? detail.error ?? downloadError ?? send.error} />

      {list ? (
        list.length === 0 ? (
          <p className="muted">Nog geen Mollie-afrekeningen. Zodra Mollie een bedrag naar de bank uitbetaalt, verschijnt het hier.</p>
        ) : (
          <section className="card">
            <div className="table-scroll" tabIndex={0} role="region" aria-label="Afrekeningen">
              <table className="table compact">
                <thead>
                  <tr>
                    <th scope="col">Datum</th>
                    <th scope="col">Referentie</th>
                    <th scope="col">Status</th>
                    <th scope="col">Omzet</th>
                    <th scope="col">Mollie-kosten</th>
                    <th scope="col">Uitbetaald</th>
                    <th scope="col">
                      <span className="visually-hidden">Acties</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {list.map((s) => (
                    <tr key={s.id}>
                      <td>{formatDate(s.date)}</td>
                      <td>{s.reference ?? s.id}</td>
                      <td>
                        <span className={statusBadge(s.status)}>{s.status}</span>
                      </td>
                      <td>{euro(s.gross)}</td>
                      <td>{euro(s.costs)}</td>
                      <td>{euro(s.net)}</td>
                      <td>
                        <button
                          type="button"
                          className="button small secondary"
                          onClick={() => {
                            setSent(null);
                            setSelected(s.id === selected ? null : s.id);
                          }}
                        >
                          {s.id === selected ? 'Verbergen' : 'Details'}
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        )
      ) : (
        <p>Laden…</p>
      )}

      {selected && d ? (
        <section className="card">
          <div className="page-header">
            <h2>Afrekening {d.settlement.reference ?? d.settlement.id}</h2>
            <button type="button" className="button secondary" onClick={() => void download(d.settlement.id, d.settlement.reference ?? d.settlement.id)}>
              Exporteren (CSV)
            </button>
          </div>
          <dl className="totals">
            <div>
              <dt>Omzet (bruto)</dt>
              <dd>{euro(d.settlement.gross)}</dd>
            </div>
            <div>
              <dt>Mollie-kosten</dt>
              <dd>{euro(d.settlement.costs)}</dd>
            </div>
            <div>
              <dt>Uitbetaald (netto)</dt>
              <dd>{euro(d.settlement.net)}</dd>
            </div>
          </dl>

          <div className="table-scroll" tabIndex={0} role="region" aria-label="Inkomsten in de afrekening">
            <table className="table compact">
              <thead>
                <tr>
                  <th scope="col">Bron</th>
                  <th scope="col">Nummer</th>
                  <th scope="col">Partij</th>
                  <th scope="col">Bedrag</th>
                </tr>
              </thead>
              <tbody>
                {d.lines.map((l) => (
                  <tr key={l.mollieId}>
                    <td>{l.source}</td>
                    <td>{l.number}</td>
                    <td>{l.party}</td>
                    <td>{euro(l.amount)}</td>
                  </tr>
                ))}
                {d.unmatchedCount > 0 ? (
                  <tr>
                    <td className="muted">Niet herleid</td>
                    <td className="muted" colSpan={2}>
                      {d.unmatchedCount} betaling(en) buiten de administratie (bijv. terugbetalingen)
                    </td>
                    <td>{euro(d.unmatchedTotal)}</td>
                  </tr>
                ) : null}
                {d.lines.length === 0 && d.unmatchedCount === 0 ? (
                  <tr>
                    <td colSpan={4} className="muted">
                      Geen betalingen in deze afrekening.
                    </td>
                  </tr>
                ) : null}
              </tbody>
            </table>
          </div>

          <form
            className="toolbar"
            onSubmit={(event) => {
              event.preventDefault();
              setSent(null);
              send.mutate(d.settlement.id, { onSuccess: () => setSent(recipient) });
            }}
          >
            <div className="field grow-field">
              <label htmlFor="settlement-recipient">Verzamelstaat mailen naar</label>
              <input
                id="settlement-recipient"
                type="email"
                required
                value={recipient}
                onChange={(event) => setRecipient(event.target.value)}
              />
            </div>
            <button type="submit" className="button" disabled={send.isPending}>
              {send.isPending ? 'Versturen…' : 'Versturen'}
            </button>
          </form>
          {sent ? <p className="muted">De verzamelstaat is onderweg naar {sent}, namens penningmeester@vrolijkedrammers.nl.</p> : null}
        </section>
      ) : null}
    </>
  );
}
