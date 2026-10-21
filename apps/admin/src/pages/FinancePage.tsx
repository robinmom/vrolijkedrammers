import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { euro } from '../api/advertisers';
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
