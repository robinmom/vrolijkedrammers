import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { kindPages, useEvenings, useSaleProducts, type Evening, type EveningRow, type SaleProduct } from '../api/sales';
import { DataTable, columnHelper } from '../components/DataTable';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { SaleProductDialog } from '../components/SaleDialogs';
import { OrderButtons, OrdersCard, ProductsCard, SalesKpis, WaitlistCard } from '../components/SaleSections';
import { formatDate } from '../format';

/** Export voor de tafelindeling: één tabblad per avond. */
function ExportSeating() {
  const api = useApi();
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);

  async function exportExcel() {
    setError(null);
    setBusy(true);
    try {
      const { data, response } = await api.GET('/api/v1/admin/sales/pronkzitting/export', { parseAs: 'blob' });
      if (data) {
        const disposition = response.headers.get('content-disposition') ?? '';
        const name = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1];
        const url = URL.createObjectURL(data as Blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = name ? decodeURIComponent(name) : 'Pronkzitting tafelindeling.xlsx';
        link.click();
        URL.revokeObjectURL(url);
      }
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <button type="button" className="button secondary" disabled={busy} onClick={() => void exportExcel()}>
        {busy ? 'Exporteren…' : 'Exporteren voor tafelindeling (Excel)'}
      </button>
      {error ? <ProblemAlert error={error} /> : null}
    </>
  );
}

function EveningCard({
  evening,
  selected,
  onSelect,
  onEdit,
}: {
  evening: Evening;
  selected: boolean;
  onSelect: () => void;
  onEdit: () => void;
}) {
  const taken = evening.sold + evening.held;
  const full = evening.capacity !== null && taken >= evening.capacity;
  const percent = evening.capacity ? Math.min(100, Math.round((taken / evening.capacity) * 100)) : 0;
  return (
    <section className={selected ? 'card accent-gold' : 'card'} aria-labelledby={`avond-${evening.productId}`}>
      <div className="card-header">
        <h2 id={`avond-${evening.productId}`}>{evening.date ? formatDate(evening.date) : evening.name}</h2>
        {full ? (
          <span className="badge error">Vol</span>
        ) : evening.capacity !== null ? (
          <span className="badge ok">{evening.capacity - taken} vrij</span>
        ) : null}
      </div>
      <p>
        {taken} van {evening.capacity ?? '∞'} plaatsen
        {evening.held > 0 ? ` (${evening.held} wacht op betaling)` : ''}
        {evening.waiting > 0 ? ` · ${evening.waiting} op de wachtlijst` : ''}
      </p>
      {evening.capacity ? (
        <div
          className="progress"
          role="progressbar"
          aria-label={`Bezetting ${evening.name}`}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={percent}
        >
          <span style={{ width: `${percent}%` }} />
        </div>
      ) : null}
      <div className="actions">
        <button type="button" className="button secondary small" onClick={onEdit}>
          Plaatsen aanpassen <span className="visually-hidden">{evening.name}</span>
        </button>
        <button type="button" className="button ghost small" aria-pressed={selected} onClick={onSelect}>
          Overzicht en wachtlijst <span className="visually-hidden">{evening.name}</span>
        </button>
      </div>
    </section>
  );
}

/**
 * Pronkzitting onder Verkoop (fase 19, Figma "Pronkzitting – overzicht per avond" en "wachtlijst en toekennen"): per
 * avond de groepen met het aantal personen en losse kaarten op naam, de export voor de tafelindeling, de wachtlijst, de
 * avonden (plaatsen per avond) en alle bestellingen.
 */
export function PronkzittingPage() {
  const evenings = useEvenings();
  const products = useSaleProducts();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [editing, setEditing] = useState<SaleProduct | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const selected = evenings.data?.find((e) => e.productId === selectedId) ?? evenings.data?.[0] ?? null;
  const selectedProduct = products.data?.find((p) => p.id === selected?.productId) ?? null;

  const rowHelper = columnHelper<EveningRow>();
  const rowColumns = [
    rowHelper.accessor('name', {
      header: 'Groep / naam',
      cell: (info) => (
        <>
          <strong>{info.getValue()}</strong>
          <div className="muted small-text">{info.row.original.isGroup ? 'groep' : 'losse kaarten'}</div>
        </>
      ),
    }),
    rowHelper.accessor('quantity', { header: 'Aantal' }),
    rowHelper.accessor('orderers', {
      header: 'Besteller',
      cell: (info) => (
        <>
          {info.getValue()}
          <div className="muted small-text">
            {[info.row.original.phones, info.row.original.emails].filter(Boolean).join(' · ')}
          </div>
        </>
      ),
    }),
    rowHelper.display({
      id: 'lid',
      header: 'Lid / betaald',
      cell: (info) => (
        <>
          <span className={info.row.original.membership.startsWith('Lid') ? 'badge ok' : 'badge info'}>
            {info.row.original.membership}
          </span>
          <div className="muted small-text">{info.row.original.paid}</div>
        </>
      ),
    }),
    rowHelper.accessor('remarks', { header: 'Opmerking / wensen', cell: (info) => info.getValue() ?? '' }),
  ];

  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>Pronkzitting</h1>
          <p className="page-subtitle">{kindPages.Pronkzitting.subtitle}</p>
        </div>
        <div className="actions">
          <ExportSeating />
          <OrderButtons kind="Pronkzitting" onMessage={setMessage} />
        </div>
      </div>
      <SuccessMessage message={message} />
      <SalesKpis kind="Pronkzitting" />
      <ProblemAlert error={evenings.error} />
      <div className="grid-2">
        {(evenings.data ?? []).map((e) => (
          <EveningCard
            key={e.productId}
            evening={e}
            selected={selected?.productId === e.productId}
            onSelect={() => setSelectedId(e.productId)}
            onEdit={() => setEditing(products.data?.find((p) => p.id === e.productId) ?? null)}
          />
        ))}
      </div>

      {selected ? (
        <>
          <section className="card" aria-labelledby="avond-overzicht">
            <h2 id="avond-overzicht">
              {selected.date ? formatDate(selected.date) : selected.name} ·{' '}
              {selected.rows.reduce((n, r) => n + r.quantity, 0)} personen in{' '}
              {selected.rows.reduce((n, r) => n + r.orderNumbers.length, 0)} bestellingen
            </h2>
            <p className="card-hint">
              Gesorteerd op groep. Losse kaarten staan op naam van de besteller. Wensen komen uit de opmerking bij het
              bestellen.
            </p>
            <DataTable
              caption={`Overzicht ${selected.name}`}
              columns={rowColumns}
              data={selected.rows}
              emptyText="Nog geen bestellingen voor deze avond."
            />
          </section>
          {selectedProduct ? (
            <WaitlistCard
              product={selectedProduct}
              title={`Wachtlijst ${selected.date ? formatDate(selected.date) : selected.name}`}
              onMessage={setMessage}
            />
          ) : null}
        </>
      ) : null}

      <ProductsCard kind="Pronkzitting" title="Avonden" onMessage={setMessage} />
      <OrdersCard kind="Pronkzitting" onMessage={setMessage} />

      <SaleProductDialog
        open={editing !== null}
        product={editing}
        initialKind="Pronkzitting"
        fixedKind
        onClose={() => setEditing(null)}
        onSaved={setMessage}
      />
    </>
  );
}
