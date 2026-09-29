import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation } from '../api/hooks';
import {
  SALES_KEYS,
  useEvenings,
  useSaleProducts,
  useWaitlist,
  type Evening,
  type EveningRow,
  type PortalPayment,
  type SaleProduct,
  type WaitlistRow,
} from '../api/sales';
import { DataTable, columnHelper } from '../components/DataTable';
import { Dialog } from '../components/Dialog';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { SaleProductDialog } from '../components/SaleDialogs';
import { formatDate, formatDateTime } from '../format';

const waitlistLabels: Record<WaitlistRow['status'], string> = {
  Waiting: 'Wacht',
  Invited: 'Uitgenodigd',
  Granted: 'Toegekend',
  Expired: 'Verlopen',
  Withdrawn: 'Ingetrokken',
};

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
 * Pronkzitting (fase 19, Figma "Pronkzitting – overzicht per avond" en "wachtlijst en toekennen"): per avond de groepen
 * met het aantal personen en losse kaarten op naam, de export voor de tafelindeling en de wachtlijst. Uitnodigen gaat
 * op volgorde; het bestuur kan ook zelf toekennen, buiten de volgorde.
 */
export function PronkzittingPage() {
  const api = useApi();
  const evenings = useEvenings();
  const products = useSaleProducts();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [editing, setEditing] = useState<SaleProduct | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [granting, setGranting] = useState<WaitlistRow | null>(null);
  const [payment, setPayment] = useState<PortalPayment>('PaymentLink');
  const selected = evenings.data?.find((e) => e.productId === selectedId) ?? evenings.data?.[0] ?? null;
  const waitlist = useWaitlist(selected?.productId ?? null);
  const grant = useApiMutation(
    (v: { id: string; payment: PortalPayment }) =>
      api.POST('/api/v1/admin/sales/waitlist/{id}/grant', {
        params: { path: { id: v.id } },
        body: { payment: v.payment },
      }),
    SALES_KEYS,
  );
  const withdraw = useApiMutation(
    (id: string) => api.DELETE('/api/v1/admin/sales/waitlist/{id}', { params: { path: { id } } }),
    SALES_KEYS,
  );

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

  const waitHelper = columnHelper<WaitlistRow>();
  const waitColumns = [
    waitHelper.accessor('position', { header: '#', cell: (info) => info.getValue() || '' }),
    waitHelper.accessor('buyerName', {
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
    waitHelper.display({
      id: 'aantal',
      header: 'Aantal',
      cell: (info) => info.row.original.memberQuantity + info.row.original.paidQuantity,
    }),
    waitHelper.accessor('createdAt', { header: 'Sinds', cell: (info) => formatDateTime(info.getValue()) }),
    waitHelper.display({
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
    waitHelper.display({
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
              onClick={() =>
                withdraw.mutate(w.id, {
                  onSuccess: () => setMessage(`${w.buyerName} is van de wachtlijst gehaald.`),
                })
              }
            >
              Verwijderen <span className="visually-hidden">{w.groupName ?? w.buyerName}</span>
            </button>
          </div>
        );
      },
    }),
  ];

  const free = selected?.capacity != null ? selected.capacity - selected.sold - selected.held : null;
  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>Pronkzitting</h1>
          <p className="page-subtitle">
            Wie komt er per avond: groepen met het aantal personen en losse kaarten op naam. Uitnodigen van de wachtlijst
            gaat op volgorde; het bestuur kan een plek ook zelf toekennen.
          </p>
        </div>
        <div className="actions">
          <ExportSeating />
        </div>
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={evenings.error ?? withdraw.error} />
      {evenings.data && evenings.data.length === 0 ? (
        <p className="muted">
          Nog geen pronkzittingavonden. Voeg ze toe onder Kaartverkoop met + Product (soort Pronkzitting).
        </p>
      ) : null}
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

          <section className="card" aria-labelledby="wachtlijst-kop">
            <h2 id="wachtlijst-kop">
              Wachtlijst {selected.date ? formatDate(selected.date) : selected.name} ({selected.waiting})
            </h2>
            <p className="card-hint">
              {free === null ? 'Onbeperkt aantal plaatsen.' : `Nog ${Math.max(0, free)} plaats(en) vrij.`} Toekennen:
              groepskaarten voor leden zijn direct geldig; losse kaarten krijgen een betaallink die 48 uur geldig is, of
              je boekt ze contant.
            </p>
            <ProblemAlert error={waitlist.error} />
            <DataTable
              caption={`Wachtlijst ${selected.name}`}
              columns={waitColumns}
              data={waitlist.data ?? []}
              emptyText="Niemand op de wachtlijst."
            />
          </section>
        </>
      ) : null}

      <SaleProductDialog
        open={editing !== null}
        product={editing}
        onClose={() => setEditing(null)}
        onSaved={setMessage}
      />
      <Dialog
        open={granting !== null}
        title={granting ? `Toekennen: ${granting.groupName ?? granting.buyerName}` : ''}
        onClose={() => setGranting(null)}
      >
        <form
          onSubmit={(e) => {
            e.preventDefault();
            if (!granting) return;
            grant.mutate(
              { id: granting.id, payment },
              {
                onSuccess: () => {
                  setMessage(`${granting.buyerName} heeft de plaatsen gekregen en krijgt een e-mail.`);
                  setGranting(null);
                },
              },
            );
          }}
        >
          <p>
            {granting ? granting.memberQuantity + granting.paidQuantity : 0} plaats(en)
            {granting && granting.memberQuantity > 0 ? `, waarvan ${granting.memberQuantity} gratis groepskaarten` : ''}.
            {granting?.buyerIsMember ? ' De besteller krijgt ook een melding in de app.' : ''}
          </p>
          {granting && granting.paidQuantity > 0 ? (
            <fieldset>
              <legend>Losse kaarten ({granting.paidQuantity})</legend>
              <label className="checkbox">
                <input
                  type="radio"
                  name="toekennen-betaling"
                  checked={payment === 'PaymentLink'}
                  onChange={() => setPayment('PaymentLink')}
                />{' '}
                Uitnodigen met een betaallink (48 uur geldig)
              </label>
              <label className="checkbox">
                <input
                  type="radio"
                  name="toekennen-betaling"
                  checked={payment === 'Cash'}
                  onChange={() => setPayment('Cash')}
                />{' '}
                Contant ontvangen
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
      </Dialog>
    </>
  );
}
