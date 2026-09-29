import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation } from '../api/hooks';
import {
  SALES_KEYS,
  formatEuro,
  kindPages,
  statusLabels,
  useSaleOrders,
  useSaleProducts,
  useSalesSummary,
  useWaitlist,
  type PortalPayment,
  type SaleOrderRow,
  type SaleProduct,
  type SaleProductKind,
  type WaitlistRow,
} from '../api/sales';
import { formatDate, formatDateTime } from '../format';
import { DataTable, columnHelper } from './DataTable';
import { Dialog } from './Dialog';
import { Pagination } from './Pagination';
import { ProblemAlert } from './ProblemAlert';
import { NewOrderDialog, SaleProductDialog } from './SaleDialogs';

/** Knoppen in de paginakop: een bestelling invoeren of een losse betaallink maken, voor deze soort producten. */
export function OrderButtons({ kind, onMessage }: { kind: SaleProductKind; onMessage: (message: string) => void }) {
  const [ordering, setOrdering] = useState<'order' | 'link' | null>(null);
  return (
    <>
      {kind !== 'Tokens' ? (
        <button type="button" className="button secondary" onClick={() => setOrdering('link')}>
          Betaallink maken
        </button>
      ) : null}
      <button type="button" className="button" onClick={() => setOrdering('order')}>
        + Bestelling
      </button>
      <NewOrderDialog
        open={ordering !== null}
        kind={kind}
        paymentLinkOnly={ordering === 'link'}
        onClose={() => setOrdering(null)}
        onSaved={onMessage}
      />
    </>
  );
}

/** Kerncijfers voor één soort product. */
export function SalesKpis({ kind }: { kind: SaleProductKind }) {
  const summary = useSalesSummary(kind);
  const s = summary.data;
  if (!s) return <ProblemAlert error={summary.error} />;
  return (
    <section className="kpis" aria-label="Kerncijfers">
      <div className="kpi">
        <span className="kpi-label">Omzet (betaald)</span>
        <span className="kpi-value">{formatEuro(s.revenueCents)}</span>
        <span className="kpi-hint">via Mollie en contant</span>
      </div>
      <div className="kpi">
        <span className="kpi-label">Verkocht</span>
        <span className="kpi-value">{s.sold}</span>
        <span className="kpi-hint">{kindPages[kind].unit}, betaald of gratis</span>
      </div>
      <div className="kpi">
        <span className="kpi-label">Openstaande betaallinks</span>
        <span className="kpi-value">{s.openPaymentLinks}</span>
        <span className="kpi-hint">{formatEuro(s.openAmountCents)} nog niet betaald</span>
      </div>
      {kind === 'Tokens' ? (
        <div className="kpi">
          <span className="kpi-label">Af te halen</span>
          <span className="kpi-value">{s.tokensToCollect}</span>
          <span className="kpi-hint">van {s.tokensSold} verkocht</span>
        </div>
      ) : null}
    </section>
  );
}

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

/** De producten van deze soort (bijv. de pronkzittingavonden) met instellen, toevoegen en de wachtlijst. */
export function ProductsCard({
  kind,
  title,
  onMessage,
}: {
  kind: SaleProductKind;
  title: string;
  onMessage: (message: string) => void;
}) {
  const products = useSaleProducts();
  const [editing, setEditing] = useState<SaleProduct | 'new' | null>(null);
  const [waitlistFor, setWaitlistFor] = useState<SaleProduct | null>(null);
  const rows = (products.data ?? []).filter((p) => p.kind === kind);
  const unit = kind === 'Tokens' ? 'munt' : 'kaart';

  const helper = columnHelper<SaleProduct>();
  const columns = [
    helper.accessor('name', {
      header: 'Product',
      cell: (info) => {
        const p = info.row.original;
        return (
          <>
            <strong>{p.name}</strong>
            <div className="muted small-text">
              {p.date ? `${formatDate(p.date)} · ` : ''}
              {formatEuro(p.priceCents)} per {unit}
              {kind === 'Pronkzitting' ? ' · leden gratis via hun groep' : ''}
            </div>
          </>
        );
      },
    }),
    helper.display({
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
    helper.display({ id: 'status', header: 'Status', cell: (info) => productStatus(info.row.original) }),
    helper.accessor('revenueCents', { header: 'Omzet', cell: (info) => formatEuro(info.getValue()) }),
    helper.display({
      id: 'acties',
      header: 'Acties',
      cell: (info) => (
        <div className="actions">
          <button type="button" className="button secondary small" onClick={() => setEditing(info.row.original)}>
            Instellen <span className="visually-hidden">{info.row.original.name}</span>
          </button>
          {kind !== 'Pronkzitting' && info.row.original.waiting > 0 ? (
            <button type="button" className="button ghost small" onClick={() => setWaitlistFor(info.row.original)}>
              Wachtlijst <span className="visually-hidden">{info.row.original.name}</span>
            </button>
          ) : null}
        </div>
      ),
    }),
  ];

  return (
    <section className="card" aria-labelledby={`producten-${kind}`}>
      <div className="card-header">
        <h2 id={`producten-${kind}`}>{title}</h2>
        <button type="button" className="button secondary small" onClick={() => setEditing('new')}>
          + {kind === 'Pronkzitting' ? 'Avond' : 'Product'}
        </button>
      </div>
      <ProblemAlert error={products.error} />
      <DataTable
        caption={title}
        columns={columns}
        data={rows}
        emptyText={
          kind === 'Pronkzitting'
            ? 'Nog geen avonden. Voeg de vrijdag en zaterdag toe, met het aantal plaatsen per avond.'
            : kind === 'EventTicket'
              ? 'Nog geen kaarten voor activiteiten. Voeg een product toe en kies de activiteit uit de agenda.'
              : 'Nog niets te koop.'
        }
      />
      <SaleProductDialog
        open={editing !== null}
        product={editing === 'new' ? null : editing}
        initialKind={kind}
        fixedKind
        onClose={() => setEditing(null)}
        onSaved={onMessage}
      />
      <Dialog open={waitlistFor !== null} title={waitlistFor ? `Wachtlijst ${waitlistFor.name}` : ''} onClose={() => setWaitlistFor(null)}>
        {waitlistFor ? <WaitlistCard product={waitlistFor} onMessage={onMessage} bare /> : null}
        <div className="actions">
          <button type="button" className="button secondary" onClick={() => setWaitlistFor(null)}>
            Sluiten
          </button>
        </div>
      </Dialog>
    </section>
  );
}

function orderBadge(o: SaleOrderRow, kind: SaleProductKind) {
  if (kind === 'Tokens' && o.status === 'Confirmed') {
    return o.collected ? <span className="badge">Afgehaald</span> : <span className="badge ok">Betaald · af te halen</span>;
  }
  const className = o.status === 'Confirmed' ? 'badge ok' : o.status === 'AwaitingPayment' ? 'badge warn' : 'badge';
  return <span className={className}>{statusLabels[o.status]}</span>;
}

/** Bestellingen van deze soort met contant ontvangen, betaallink opnieuw en annuleren (nooit terugbetalen). */
export function OrdersCard({ kind, onMessage }: { kind: SaleProductKind; onMessage: (message: string) => void }) {
  const api = useApi();
  const products = useSaleProducts();
  const [productId, setProductId] = useState('');
  const [status, setStatus] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const orders = useSaleOrders(kind, productId, status, search, page);
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
  const own = (products.data ?? []).filter((p) => p.kind === kind);

  const helper = columnHelper<SaleOrderRow>();
  const columns = [
    helper.accessor('number', {
      header: 'Bestelling',
      cell: (info) => (
        <>
          <strong>{info.getValue()}</strong>
          <div className="muted small-text">{formatDateTime(info.row.original.createdAt)}</div>
        </>
      ),
    }),
    ...(own.length > 1 ? [helper.accessor('productName', { header: 'Product' })] : []),
    helper.accessor('buyerName', {
      header: kind === 'Tokens' ? 'Lid' : 'Besteller',
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
    helper.display({
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
    helper.accessor('amountCents', {
      header: 'Bedrag',
      cell: (info) => (
        <>
          {formatEuro(info.getValue())}
          <div className="muted small-text">
            {info.row.original.paymentMethod === 'Cash' ? 'contant' : info.row.original.paymentMethod === 'Free' ? 'gratis' : 'iDEAL'}
          </div>
        </>
      ),
    }),
    helper.accessor('status', {
      header: 'Status',
      cell: (info) => (
        <>
          {orderBadge(info.row.original, kind)}
          {info.row.original.holdUntil ? (
            <div className="muted small-text">tot {formatDateTime(info.row.original.holdUntil)}</div>
          ) : null}
        </>
      ),
    }),
    helper.display({
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
                onClick={() => paidCash.mutate(o.id, { onSuccess: () => onMessage(`${o.number} contant betaald; bevestiging gemaild.`) })}
              >
                Contant ontvangen <span className="visually-hidden">{o.number}</span>
              </button>
            ) : null}
            {o.status === 'AwaitingPayment' ? (
              <button
                type="button"
                className="button ghost small"
                disabled={resend.isPending}
                onClick={() => resend.mutate(o.id, { onSuccess: () => onMessage(`Betaallink opnieuw gemaild naar ${o.buyerEmail}.`) })}
              >
                Link opnieuw <span className="visually-hidden">{o.number}</span>
              </button>
            ) : null}
            {(o.status === 'AwaitingPayment' || o.status === 'Confirmed') && !o.collected ? (
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

  return (
    <section className="card" aria-labelledby={`bestellingen-${kind}`}>
      <h2 id={`bestellingen-${kind}`}>Bestellingen</h2>
      <div className="toolbar">
        <div className="field grow-field">
          <label htmlFor={`zoeken-${kind}`}>Zoeken op nummer, naam, e-mail of groep</label>
          <input
            id={`zoeken-${kind}`}
            type="search"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
          />
        </div>
        {own.length > 1 ? (
          <div className="field">
            <label htmlFor={`product-${kind}`}>Product</label>
            <select
              id={`product-${kind}`}
              value={productId}
              onChange={(e) => {
                setProductId(e.target.value);
                setPage(1);
              }}
            >
              <option value="">Alle</option>
              {own.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
          </div>
        ) : null}
        <div className="field">
          <label htmlFor={`status-${kind}`}>Status</label>
          <select
            id={`status-${kind}`}
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
      <ProblemAlert error={orders.error ?? paidCash.error ?? resend.error} />
      <DataTable
        caption="Bestellingen"
        columns={columns}
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
                  onMessage(`Bestelling ${cancelling.number} geannuleerd.`);
                  setCancelling(null);
                },
              },
            );
          }}
        >
          <p>De QR vervalt direct en de plaatsen komen weer vrij. Er wordt niets terugbetaald.</p>
          <div className="field">
            <label htmlFor="annuleren-reden">Reden (optioneel)</label>
            <textarea id="annuleren-reden" rows={2} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
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
    </section>
  );
}

const waitlistLabels: Record<WaitlistRow['status'], string> = {
  Waiting: 'Wacht',
  Invited: 'Uitgenodigd',
  Granted: 'Toegekend',
  Expired: 'Verlopen',
  Withdrawn: 'Ingetrokken',
};

/** Wachtlijst van een product: toekennen met een betaallink (48 uur) of contant, ook buiten de volgorde. */
export function WaitlistCard({
  product,
  title,
  onMessage,
  bare,
}: {
  product: { id: string; name: string; remaining: number | null; waiting: number };
  title?: string;
  onMessage: (message: string) => void;
  /** Zonder kaart en kop (in een dialoog). */
  bare?: boolean;
}) {
  const api = useApi();
  const waitlist = useWaitlist(product.id);
  const [granting, setGranting] = useState<WaitlistRow | null>(null);
  const [payment, setPayment] = useState<PortalPayment>('PaymentLink');
  const grant = useApiMutation(
    (v: { id: string; payment: PortalPayment }) =>
      api.POST('/api/v1/admin/sales/waitlist/{id}/grant', { params: { path: { id: v.id } }, body: { payment: v.payment } }),
    SALES_KEYS,
  );
  const withdraw = useApiMutation(
    (id: string) => api.DELETE('/api/v1/admin/sales/waitlist/{id}', { params: { path: { id } } }),
    SALES_KEYS,
  );

  const helper = columnHelper<WaitlistRow>();
  const columns = [
    helper.accessor('position', { header: '#', cell: (info) => info.getValue() || '' }),
    helper.accessor('buyerName', {
      header: 'Groep of besteller',
      cell: (info) => {
        const w = info.row.original;
        return (
          <>
            <strong>{w.groupName ?? w.buyerName}</strong>
            <div className="muted small-text">
              {w.groupName ? `groep · via ${w.buyerName}` : 'losse kaarten'}
              {w.buyerIsMember ? ' (lid)' : ' · gast'}
            </div>
          </>
        );
      },
    }),
    helper.display({ id: 'aantal', header: 'Aantal', cell: (info) => info.row.original.memberQuantity + info.row.original.paidQuantity }),
    helper.accessor('createdAt', { header: 'Sinds', cell: (info) => formatDateTime(info.getValue()) }),
    helper.display({
      id: 'status',
      header: 'Status',
      cell: (info) => {
        const w = info.row.original;
        if (w.status !== 'Waiting')
          return (
            <span className="badge">
              {waitlistLabels[w.status]}
              {w.orderNumber ? ` · ${w.orderNumber}` : ''}
            </span>
          );
        return w.fits ? <span className="badge ok">Past</span> : <span className="badge warn">Past niet</span>;
      },
    }),
    helper.display({
      id: 'acties',
      header: 'Acties',
      cell: (info) => {
        const w = info.row.original;
        if (w.status !== 'Waiting') return null;
        return (
          <div className="actions">
            <button
              type="button"
              className="button small"
              disabled={!w.fits}
              onClick={() => {
                grant.reset();
                setPayment('PaymentLink');
                setGranting(w);
              }}
            >
              Toekennen <span className="visually-hidden">{w.groupName ?? w.buyerName}</span>
            </button>
            <button
              type="button"
              className="button ghost small"
              onClick={() => withdraw.mutate(w.id, { onSuccess: () => onMessage(`${w.buyerName} is van de wachtlijst gehaald.`) })}
            >
              Verwijderen <span className="visually-hidden">{w.groupName ?? w.buyerName}</span>
            </button>
          </div>
        );
      },
    }),
  ];

  const body = (
    <>
      <p className="card-hint">
        {product.remaining === null ? 'Onbeperkt aantal plaatsen.' : `Nog ${Math.max(0, product.remaining)} plaats(en) vrij.`} Toekennen:
        groepskaarten voor leden zijn direct geldig; losse kaarten krijgen een betaallink die 48 uur geldig is, of je boekt
        ze contant.
      </p>
      <ProblemAlert error={waitlist.error ?? withdraw.error} />
      <DataTable caption={`Wachtlijst ${product.name}`} columns={columns} data={waitlist.data ?? []} emptyText="Niemand op de wachtlijst." />
      {granting ? (
        <form
          className="card"
          aria-label={`Toekennen: ${granting.groupName ?? granting.buyerName}`}
          onSubmit={(e) => {
            e.preventDefault();
            grant.mutate(
              { id: granting.id, payment },
              {
                onSuccess: () => {
                  onMessage(`${granting.buyerName} heeft de plaatsen gekregen en krijgt een e-mail.`);
                  setGranting(null);
                },
              },
            );
          }}
        >
          <h3>Toekennen: {granting.groupName ?? granting.buyerName}</h3>
          <p>
            {granting.memberQuantity + granting.paidQuantity} plaats(en)
            {granting.memberQuantity > 0 ? `, waarvan ${granting.memberQuantity} gratis groepskaarten` : ''}.
            {granting.buyerIsMember ? ' De besteller krijgt ook een melding in de app.' : ''}
          </p>
          {granting.paidQuantity > 0 ? (
            <fieldset>
              <legend>Losse kaarten ({granting.paidQuantity})</legend>
              <label className="checkbox">
                <input type="radio" name="toekennen-betaling" checked={payment === 'PaymentLink'} onChange={() => setPayment('PaymentLink')} />{' '}
                Uitnodigen met een betaallink (48 uur geldig)
              </label>
              <label className="checkbox">
                <input type="radio" name="toekennen-betaling" checked={payment === 'Cash'} onChange={() => setPayment('Cash')} /> Contant
                ontvangen
              </label>
            </fieldset>
          ) : null}
          <ProblemAlert error={grant.error} />
          <div className="actions">
            <button type="button" className="button secondary" onClick={() => setGranting(null)}>
              Annuleren
            </button>
            <button type="submit" className="button" disabled={grant.isPending}>
              Toekennen
            </button>
          </div>
        </form>
      ) : null}
    </>
  );

  if (bare) return body;
  return (
    <section className="card" aria-labelledby={`wachtlijst-${product.id}`}>
      <h2 id={`wachtlijst-${product.id}`}>
        {title ?? `Wachtlijst ${product.name}`} ({product.waiting})
      </h2>
      {body}
    </section>
  );
}
