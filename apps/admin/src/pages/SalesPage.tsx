import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation } from '../api/hooks';
import {
  SALES_KEYS,
  formatEuro,
  kindLabels,
  statusLabels,
  useSaleOrders,
  useSaleProducts,
  useSalesSummary,
  type SaleOrderRow,
  type SaleProduct,
} from '../api/sales';
import { DataTable, columnHelper } from '../components/DataTable';
import { Dialog } from '../components/Dialog';
import { Pagination } from '../components/Pagination';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { NewOrderDialog, SaleProductDialog } from '../components/SaleDialogs';
import { formatDate, formatDateTime } from '../format';

function productStatus(p: SaleProduct) {
  if (!p.onSale) return <span className="badge">Niet te koop</span>;
  if (p.remaining === 0)
    return (
      <>
        <span className="badge error">Vol</span>
        {p.waiting > 0 ? <span className="badge warn">{p.waiting} wachtlijst</span> : null}
      </>
    );
  return <span className="badge ok">Open</span>;
}

function orderBadge(o: SaleOrderRow) {
  const className =
    o.status === 'Confirmed' ? 'badge ok' : o.status === 'AwaitingPayment' ? 'badge warn' : 'badge';
  return <span className={className}>{statusLabels[o.status]}</span>;
}

/**
 * Kaartverkoop (fase 19, Figma "Portal / Kaartverkoop"): kerncijfers, de producten met verkocht/capaciteit en omzet,
 * en alle bestellingen met contant ontvangen, betaallink opnieuw sturen en annuleren (nooit terugbetalen).
 */
export function SalesPage() {
  const api = useApi();
  const summary = useSalesSummary();
  const products = useSaleProducts();
  const [editing, setEditing] = useState<SaleProduct | 'new' | null>(null);
  const [ordering, setOrdering] = useState<'order' | 'link' | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [productId, setProductId] = useState('');
  const [status, setStatus] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const orders = useSaleOrders(productId, status, search, page);
  const [cancelling, setCancelling] = useState<SaleOrderRow | null>(null);
  const [reason, setReason] = useState('');
  const paidCash = useApiMutation(
    (id: string) => api.POST('/api/v1/admin/sales/orders/{id}/paid-cash', { params: { path: { id } } }),
    SALES_KEYS,
  );
  const resend = useApiMutation(
    (id: string) => api.POST('/api/v1/admin/sales/orders/{id}/resend-link', { params: { path: { id } } }),
    SALES_KEYS,
  );
  const cancel = useApiMutation(
    (v: { id: string; reason: string | null }) =>
      api.POST('/api/v1/admin/sales/orders/{id}/cancel', { params: { path: { id: v.id } }, body: { reason: v.reason } }),
    SALES_KEYS,
  );

  const productHelper = columnHelper<SaleProduct>();
  const productColumns = [
    productHelper.accessor('name', {
      header: 'Product',
      cell: (info) => {
        const p = info.row.original;
        return (
          <>
            <strong>{p.name}</strong>
            <div className="muted small-text">
              {kindLabels[p.kind]}
              {p.date ? ` · ${formatDate(p.date)}` : ''} · {formatEuro(p.priceCents)}
              {p.kind === 'Tokens' ? ' per munt' : ' per kaart'}
              {p.kind === 'Pronkzitting' ? ' · leden gratis via hun groep' : ''}
            </div>
          </>
        );
      },
    }),
    productHelper.display({
      id: 'verkocht',
      header: 'Verkocht / capaciteit',
      cell: (info) => {
        const p = info.row.original;
        return (
          <>
            {p.sold} / {p.capacity ?? '–'}
            {p.held > 0 ? <div className="muted small-text">{p.held} wacht op betaling</div> : null}
          </>
        );
      },
    }),
    productHelper.display({ id: 'status', header: 'Status', cell: (info) => productStatus(info.row.original) }),
    productHelper.accessor('revenueCents', { header: 'Omzet', cell: (info) => formatEuro(info.getValue()) }),
    productHelper.display({
      id: 'acties',
      header: 'Acties',
      cell: (info) => (
        <button type="button" className="button secondary small" onClick={() => setEditing(info.row.original)}>
          Instellen <span className="visually-hidden">{info.row.original.name}</span>
        </button>
      ),
    }),
  ];

  const orderHelper = columnHelper<SaleOrderRow>();
  const orderColumns = [
    orderHelper.accessor('number', {
      header: 'Bestelling',
      cell: (info) => (
        <>
          <strong>{info.getValue()}</strong>
          <div className="muted small-text">{formatDateTime(info.row.original.createdAt)}</div>
        </>
      ),
    }),
    orderHelper.accessor('productName', { header: 'Product' }),
    orderHelper.accessor('buyerName', {
      header: 'Besteller',
      cell: (info) => {
        const o = info.row.original;
        return (
          <>
            {o.buyerName}
            <div className="muted small-text">
              {o.buyerEmail}
              {o.buyerPhone ? ` · ${o.buyerPhone}` : ''}
            </div>
          </>
        );
      },
    }),
    orderHelper.display({
      id: 'aantal',
      header: 'Aantal',
      cell: (info) => {
        const o = info.row.original;
        return (
          <>
            {o.memberQuantity + o.paidQuantity}
            {o.groupName ? (
              <div className="muted small-text">
                {o.memberQuantity} gratis ({o.groupName})
                {o.paidQuantity > 0 ? ` + ${o.paidQuantity} los` : ''}
              </div>
            ) : null}
          </>
        );
      },
    }),
    orderHelper.accessor('amountCents', {
      header: 'Bedrag',
      cell: (info) => (
        <>
          {formatEuro(info.getValue())}
          <div className="muted small-text">
            {info.row.original.paymentMethod === 'Cash'
              ? 'contant'
              : info.row.original.paymentMethod === 'Free'
                ? 'gratis'
                : 'iDEAL'}
          </div>
        </>
      ),
    }),
    orderHelper.accessor('status', {
      header: 'Status',
      cell: (info) => (
        <>
          {orderBadge(info.row.original)}
          {info.row.original.holdUntil ? (
            <div className="muted small-text">tot {formatDateTime(info.row.original.holdUntil)}</div>
          ) : null}
        </>
      ),
    }),
    orderHelper.display({
      id: 'acties',
      header: 'Acties',
      cell: (info) => {
        const o = info.row.original;
        return (
          <div className="actions">
            {o.status === 'AwaitingPayment' || o.status === 'Expired' ? (
              <button
                type="button"
                className="button secondary small"
                disabled={paidCash.isPending}
                onClick={() =>
                  paidCash.mutate(o.id, { onSuccess: () => setMessage(`${o.number} contant betaald; kaarten gemaild.`) })
                }
              >
                Contant ontvangen <span className="visually-hidden">{o.number}</span>
              </button>
            ) : null}
            {o.status === 'AwaitingPayment' ? (
              <button
                type="button"
                className="button ghost small"
                disabled={resend.isPending}
                onClick={() =>
                  resend.mutate(o.id, { onSuccess: () => setMessage(`Betaallink opnieuw gemaild naar ${o.buyerEmail}.`) })
                }
              >
                Link opnieuw <span className="visually-hidden">{o.number}</span>
              </button>
            ) : null}
            {o.status === 'AwaitingPayment' || o.status === 'Confirmed' ? (
              <button
                type="button"
                className="button danger small"
                onClick={() => {
                  setReason('');
                  cancel.reset();
                  setCancelling(o);
                }}
              >
                Annuleren <span className="visually-hidden">{o.number}</span>
              </button>
            ) : null}
          </div>
        );
      },
    }),
  ];

  const s = summary.data;
  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>Kaartverkoop</h1>
          <p className="page-subtitle">
            Pronkzitting, dagkaarten, kaarten per activiteit en munten. Betalen via Mollie (iDEAL); leden bestellen
            pronkzittingkaarten voor hun groep (in de contributie). Kaarten worden niet terugbetaald.
          </p>
        </div>
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => setOrdering('link')}>
            Betaallink maken
          </button>
          <button type="button" className="button" onClick={() => setOrdering('order')}>
            + Bestelling
          </button>
        </div>
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={summary.error ?? paidCash.error ?? resend.error} />
      {s ? (
        <section className="kpis" aria-label="Kerncijfers">
          <div className="kpi">
            <span className="kpi-label">Omzet (betaald)</span>
            <span className="kpi-value">{formatEuro(s.revenueCents)}</span>
            <span className="kpi-hint">via Mollie en contant</span>
          </div>
          <div className="kpi">
            <span className="kpi-label">Openstaande betaallinks</span>
            <span className="kpi-value">{s.openPaymentLinks}</span>
            <span className="kpi-hint">{formatEuro(s.openAmountCents)} nog niet betaald</span>
          </div>
          <div className="kpi">
            <span className="kpi-label">Munten af te halen</span>
            <span className="kpi-value">{s.tokensToCollect}</span>
            <span className="kpi-hint">van {s.tokensSold} verkocht</span>
          </div>
        </section>
      ) : null}

      <section className="card" aria-labelledby="verkoop-kop">
        <div className="card-header">
          <h2 id="verkoop-kop">Verkoop</h2>
          <button type="button" className="button secondary small" onClick={() => setEditing('new')}>
            + Product
          </button>
        </div>
        <ProblemAlert error={products.error} />
        <DataTable
          caption="Producten"
          columns={productColumns}
          data={products.data ?? []}
          emptyText="Nog niets te koop. Voeg een product toe: een pronkzittingavond, dagkaarten, kaarten voor een activiteit of munten."
        />
        <p className="card-hint">
          Kaarten per activiteit horen bij een activiteit uit de agenda. Nooit terugbetalen: het bestuur kan een bestelling
          wel annuleren (de QR vervalt), zonder geld terug.
        </p>
      </section>

      <section className="card" aria-labelledby="bestellingen-kop">
        <h2 id="bestellingen-kop">Bestellingen</h2>
        <div className="toolbar">
          <div className="field grow-field">
            <label htmlFor="bestelling-zoeken">Zoeken op nummer, naam, e-mail of groep</label>
            <input
              id="bestelling-zoeken"
              type="search"
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                setPage(1);
              }}
            />
          </div>
          <div className="field">
            <label htmlFor="bestelling-filter-product">Product</label>
            <select
              id="bestelling-filter-product"
              value={productId}
              onChange={(e) => {
                setProductId(e.target.value);
                setPage(1);
              }}
            >
              <option value="">Alle</option>
              {(products.data ?? []).map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor="bestelling-filter-status">Status</label>
            <select
              id="bestelling-filter-status"
              value={status}
              onChange={(e) => {
                setStatus(e.target.value);
                setPage(1);
              }}
            >
              <option value="">Alle</option>
              {Object.entries(statusLabels).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </div>
        </div>
        <ProblemAlert error={orders.error} />
        <DataTable
          caption="Bestellingen"
          columns={orderColumns}
          data={orders.data?.items ?? []}
          emptyText="Nog geen bestellingen."
          footer={
            orders.data ? (
              <Pagination
                page={orders.data.page}
                pageSize={orders.data.pageSize}
                totalCount={orders.data.totalCount}
                onPage={setPage}
                noun="bestellingen"
              />
            ) : null
          }
        />
      </section>

      <SaleProductDialog
        open={editing !== null}
        product={editing === 'new' ? null : editing}
        onClose={() => setEditing(null)}
        onSaved={setMessage}
      />
      <NewOrderDialog
        open={ordering !== null}
        paymentLinkOnly={ordering === 'link'}
        onClose={() => setOrdering(null)}
        onSaved={setMessage}
      />
      <Dialog
        open={cancelling !== null}
        title={cancelling ? `Bestelling ${cancelling.number} annuleren` : ''}
        onClose={() => setCancelling(null)}
      >
        <form
          onSubmit={(e) => {
            e.preventDefault();
            if (!cancelling) return;
            cancel.mutate(
              { id: cancelling.id, reason: reason.trim() || null },
              {
                onSuccess: () => {
                  setMessage(`Bestelling ${cancelling.number} geannuleerd.`);
                  setCancelling(null);
                },
              },
            );
          }}
        >
          <p>De QR vervalt direct en de plaatsen komen weer vrij. Er wordt niets terugbetaald.</p>
          <div className="field">
            <label htmlFor="annuleren-reden">Reden (optioneel)</label>
            <textarea
              id="annuleren-reden"
              rows={2}
              maxLength={500}
              value={reason}
              onChange={(e) => setReason(e.target.value)}
            />
          </div>
          <ProblemAlert error={cancel.error} />
          <div className="actions">
            <button type="button" className="button secondary" onClick={() => setCancelling(null)}>
              Terug
            </button>
            <button type="submit" className="button danger" disabled={cancel.isPending}>
              Annuleren
            </button>
          </div>
        </form>
      </Dialog>
    </>
  );
}
