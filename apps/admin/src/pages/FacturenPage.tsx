import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { euro } from '../api/advertisers';
import { ProblemAlert } from '../components/ProblemAlert';
import { AdvertiserInvoicesPage } from './AdvertiserPages';

function formatDate(date: string | null | undefined) {
  if (!date) return '—';
  const [y, m, d] = date.split('-');
  return `${d}-${m}-${y}`;
}

/** De festival-facturen (betaalverzoeken) van het carnavalsjaar; aanmaken en versturen gebeurt bij het Dansgardefestival. */
function FestivalInvoicesTab() {
  const api = useApi();
  const invoices = useQuery({
    queryKey: ['finance', 'festival-invoices'],
    queryFn: async () => (await api.GET('/api/v1/admin/finance/festival-invoices')).data!,
  });

  const rows = invoices.data;
  return (
    <>
      <ProblemAlert error={invoices.error} />
      <p className="muted">
        De betaalverzoeken van het Dansgardefestival van dit carnavalsjaar. Aanmaken en versturen gaat via{' '}
        <strong>Dansgarde → Dansgardefestival</strong>.
      </p>
      {rows ? (
        <section className="card">
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Festivalfacturen">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">Nummer</th>
                  <th scope="col">Club</th>
                  <th scope="col">Datum</th>
                  <th scope="col">Bedrag</th>
                  <th scope="col">Status</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.number}>
                    <td>{row.number}</td>
                    <td>{row.club}</td>
                    <td>{formatDate(row.date)}</td>
                    <td>{euro(row.total)}</td>
                    <td>
                      <span className={row.status === 'Betaald' ? 'badge ok' : row.status === 'Ingetrokken' ? 'badge' : 'badge warn'}>
                        {row.status}
                      </span>
                    </td>
                  </tr>
                ))}
                {rows.length === 0 ? (
                  <tr>
                    <td colSpan={5} className="muted">
                      Nog geen festivalfacturen in dit carnavalsjaar.
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

const TABS = [
  { key: 'adverteerders', label: 'Adverteerders' },
  { key: 'festival', label: 'Dansgardefestival' },
] as const;

type TabKey = (typeof TABS)[number]['key'];

/** Facturen (fase 4c): adverteerders- en festivalfacturen op één plek, met een tabblad per soort. */
export function FacturenPage() {
  const [tab, setTab] = useState<TabKey>('adverteerders');
  return (
    <>
      <div className="tabs" role="tablist" aria-label="Soort facturen">
        {TABS.map((t) => (
          <button
            key={t.key}
            type="button"
            role="tab"
            className="tab"
            aria-selected={t.key === tab}
            onClick={() => setTab(t.key)}
          >
            {t.label}
          </button>
        ))}
      </div>
      {tab === 'adverteerders' ? <AdvertiserInvoicesPage /> : <FestivalInvoicesTab />}
    </>
  );
}
