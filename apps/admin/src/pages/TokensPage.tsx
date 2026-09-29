import { formatEuro, statusLabels, useTokenOrders, type SaleOrderRow } from '../api/sales';
import { DataTable, columnHelper } from '../components/DataTable';
import { ProblemAlert } from '../components/ProblemAlert';
import { formatDateTime } from '../format';

/**
 * Munten (fase 19): wie heeft munten gekocht, hoeveel, betaald en al afgehaald. Munten zijn alleen voor leden en
 * persoonsgebonden; het lid haalt ze zelf op bij de kassa met de munten-QR (de rol Kassa scant en geeft uit).
 */
export function TokensPage() {
  const orders = useTokenOrders();
  const rows = orders.data ?? [];
  const paid = rows.filter((o) => o.status === 'Confirmed');
  const toCollect = paid.filter((o) => !o.collected).reduce((n, o) => n + o.paidQuantity, 0);
  const collected = paid.filter((o) => o.collected).reduce((n, o) => n + o.paidQuantity, 0);

  const helper = columnHelper<SaleOrderRow>();
  const columns = [
    helper.accessor('buyerName', {
      header: 'Lid',
      cell: (info) => (
        <>
          <strong>{info.getValue()}</strong>
          <div className="muted small-text">{info.row.original.buyerEmail}</div>
        </>
      ),
    }),
    helper.accessor('paidQuantity', { header: 'Munten' }),
    helper.accessor('number', {
      header: 'Bestelling',
      cell: (info) => (
        <>
          {info.getValue()}
          <div className="muted small-text">{formatDateTime(info.row.original.createdAt)}</div>
        </>
      ),
    }),
    helper.accessor('amountCents', { header: 'Bedrag', cell: (info) => formatEuro(info.getValue()) }),
    helper.display({
      id: 'status',
      header: 'Status',
      cell: (info) => {
        const o = info.row.original;
        if (o.status !== 'Confirmed') return <span className="badge">{statusLabels[o.status]}</span>;
        return o.collected ? (
          <span className="badge">Afgehaald</span>
        ) : (
          <span className="badge ok">Betaald · af te halen</span>
        );
      },
    }),
  ];

  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>Munten</h1>
          <p className="page-subtitle">
            Alleen voor leden en persoonsgebonden. Het lid haalt de munten zelf op bij de kassa met de munten-QR in de
            app; afhalen kan pas als de betaling is afgerond.
          </p>
        </div>
      </div>
      <ProblemAlert error={orders.error} />
      <section className="kpis" aria-label="Munten">
        <div className="kpi">
          <span className="kpi-label">Af te halen</span>
          <span className="kpi-value">{toCollect}</span>
          <span className="kpi-hint">betaald, nog niet uitgegeven</span>
        </div>
        <div className="kpi">
          <span className="kpi-label">Afgehaald</span>
          <span className="kpi-value">{collected}</span>
          <span className="kpi-hint">uitgegeven bij de kassa</span>
        </div>
      </section>
      <DataTable caption="Munten" columns={columns} data={rows} emptyText="Nog geen munten verkocht." />
    </>
  );
}
