import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { euro } from '../api/advertisers';
import { useApiMutation, useCarnivalYears } from '../api/hooks';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';

type Source = 'Advertisers' | 'Festival' | 'Contribution';

const SOURCES: { value: Source; label: string }[] = [
  { value: 'Advertisers', label: 'Adverteerders' },
  { value: 'Festival', label: 'Dansgardefestival' },
  { value: 'Contribution', label: 'Contributie' },
];

function formatDate(date: string | null | undefined) {
  if (!date) return '—';
  const [y, m, d] = date.split('-');
  return `${d}-${m}-${y}`;
}

/**
 * Verzamelfacturen (fase 4d): per onderdeel en carnavalsjaar één factuur met alle betaalde transacties, regel per regel
 * met een totaal. Elke factuur wordt bewaard, zodat een volgende alleen de sinds de vorige nieuw-betaalde regels pakt (delta).
 */
export function VerzamelfacturenPage() {
  const api = useApi();
  const years = useCarnivalYears();
  const [carnivalYearId, setCarnivalYearId] = useState<number | undefined>(undefined);
  const [source, setSource] = useState<Source>('Advertisers');
  const [message, setMessage] = useState<string | null>(null);
  const [downloadError, setDownloadError] = useState<unknown>(null);

  const key = ['finance', 'collective-invoices', String(carnivalYearId ?? 'current'), source];
  const overview = useQuery({
    queryKey: key,
    queryFn: async () =>
      (await api.GET('/api/v1/admin/finance/collective-invoices', { params: { query: { carnivalYearId, source } } })).data!,
  });

  const create = useApiMutation(
    async () =>
      (await api.POST('/api/v1/admin/finance/collective-invoices', { body: { carnivalYearId: carnivalYearId ?? null, source } }))
        .data,
    [key],
  );

  async function download(id: string, number: string) {
    setDownloadError(null);
    try {
      const { data } = await api.GET('/api/v1/admin/finance/collective-invoices/{id}/pdf', {
        params: { path: { id } },
        parseAs: 'blob',
      });
      if (data) {
        const url = URL.createObjectURL(data as Blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `Verzamelfactuur ${number}.pdf`;
        link.click();
        URL.revokeObjectURL(url);
      }
    } catch (error) {
      setDownloadError(error);
    }
  }

  const r = overview.data;
  const sourceLabel = SOURCES.find((s) => s.value === source)!.label;
  return (
    <>
      <div className="page-header">
        <div>
          <h1>Verzamelfacturen{r?.year ? ` ${r.year}` : ''}</h1>
          <p className="muted">
            Eén factuur per onderdeel en carnavalsjaar met alle betaalde transacties, regel per regel. Een volgende factuur
            pakt alleen de sinds de vorige nieuw-betaalde regels (delta).
          </p>
        </div>
        <div className="toolbar">
          <div className="field">
            <label htmlFor="vf-jaar">Carnavalsjaar</label>
            <select
              id="vf-jaar"
              value={carnivalYearId ?? ''}
              onChange={(e) => setCarnivalYearId(e.target.value ? Number(e.target.value) : undefined)}
            >
              <option value="">Huidig carnavalsjaar</option>
              {years.data?.map((y) => (
                <option key={y.id} value={y.id}>
                  {y.name}
                  {y.active ? ' (actief)' : ''}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor="vf-onderdeel">Onderdeel</label>
            <select
              id="vf-onderdeel"
              value={source}
              onChange={(e) => {
                setSource(e.target.value as Source);
                setMessage(null);
              }}
            >
              {SOURCES.map((s) => (
                <option key={s.value} value={s.value}>
                  {s.label}
                </option>
              ))}
            </select>
          </div>
        </div>
      </div>
      <ProblemAlert error={overview.error ?? create.error ?? downloadError} />
      <SuccessMessage message={message} />

      {r ? (
        <>
          <section className="card">
            <div className="page-header">
              <h2>Nieuwe verzamelfactuur · {sourceLabel}</h2>
              <button
                type="button"
                className="button"
                disabled={r.preview.newLines.length === 0 || create.isPending}
                onClick={() =>
                  create.mutate(undefined, {
                    onSuccess: () => setMessage(`Verzamelfactuur gemaakt met ${r.preview.newLines.length} regel(s).`),
                  })
                }
              >
                {create.isPending ? 'Bezig…' : 'Verzamelfactuur maken'}
              </button>
            </div>
            {r.preview.newLines.length === 0 ? (
              <p className="muted">
                Er zijn geen nieuwe betaalde transacties sinds de vorige verzamelfactuur
                {r.preview.alreadyInvoicedCount > 0 ? ` (${r.preview.alreadyInvoicedCount} al verwerkt)` : ''}.
              </p>
            ) : (
              <>
                <p className="muted">
                  {r.preview.newLines.length} nieuwe betaalde transactie(s) · totaal {euro(r.preview.total)}
                  {r.preview.alreadyInvoicedCount > 0 ? ` · ${r.preview.alreadyInvoicedCount} al eerder verwerkt` : ''}.
                </p>
                <div className="table-scroll" tabIndex={0} role="region" aria-label="Nieuwe transacties">
                  <table className="table compact">
                    <thead>
                      <tr>
                        <th scope="col">Partij</th>
                        <th scope="col">Kenmerk</th>
                        <th scope="col">Betaald op</th>
                        <th scope="col">Bedrag</th>
                      </tr>
                    </thead>
                    <tbody>
                      {r.preview.newLines.map((l, i) => (
                        <tr key={`${l.reference}-${i}`}>
                          <td>{l.party}</td>
                          <td className="muted">{l.reference}</td>
                          <td className="muted">{formatDate(l.paidOn)}</td>
                          <td>{euro(l.amount)}</td>
                        </tr>
                      ))}
                      <tr className="total-row">
                        <th scope="row" colSpan={3}>
                          Totaal
                        </th>
                        <td>{euro(r.preview.total)}</td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </>
            )}
          </section>

          <section className="card">
            <h2>Gemaakte verzamelfacturen</h2>
            {r.invoices.length === 0 ? (
              <p className="muted">Nog geen verzamelfacturen voor {sourceLabel} in dit carnavalsjaar.</p>
            ) : (
              <div className="table-scroll" tabIndex={0} role="region" aria-label="Verzamelfacturen">
                <table className="table">
                  <thead>
                    <tr>
                      <th scope="col">Nummer</th>
                      <th scope="col">Datum</th>
                      <th scope="col">Regels</th>
                      <th scope="col">Totaal</th>
                      <th scope="col">
                        <span className="visually-hidden">Acties</span>
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {r.invoices.map((inv) => (
                      <tr key={inv.id}>
                        <td>{inv.number}</td>
                        <td>{formatDate(inv.invoiceDate)}</td>
                        <td>{inv.lineCount}</td>
                        <td>{euro(inv.total)}</td>
                        <td>
                          <button type="button" className="button small secondary" onClick={() => void download(inv.id, inv.number)}>
                            PDF
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </>
      ) : (
        <p>Laden…</p>
      )}
    </>
  );
}
