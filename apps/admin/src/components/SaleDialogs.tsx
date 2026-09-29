import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import { useAdminEvents, useApiMutation } from '../api/hooks';
import {
  SALES_KEYS,
  formatEuro,
  kindLabels,
  useSaleGroups,
  useSaleProducts,
  type PortalPayment,
  type SaleProduct,
  type SaleProductKind,
} from '../api/sales';
import { fromLocalInput, toLocalInput } from '../format';
import { Dialog } from './Dialog';
import { ProblemAlert } from './ProblemAlert';

interface ProductForm {
  kind: SaleProductKind;
  name: string;
  description: string;
  eventId: string;
  date: string;
  price: string;
  capacity: string;
  maxPerOrder: string;
  saleOpensAt: string;
  saleClosesAt: string;
  onSale: boolean;
  sortOrder: string;
}

function toForm(p: SaleProduct | null, kind: SaleProductKind): ProductForm {
  return {
    kind: p?.kind ?? kind,
    name: p?.name ?? '',
    description: p?.description ?? '',
    eventId: p?.eventId ?? '',
    date: p?.date ?? '',
    price: p ? (p.priceCents / 100).toFixed(2).replace('.', ',') : '',
    capacity: p?.capacity?.toString() ?? '',
    maxPerOrder: (p?.maxPerOrder ?? (kind === 'Tokens' ? 100 : 10)).toString(),
    saleOpensAt: toLocalInput(p?.saleOpensAt),
    saleClosesAt: toLocalInput(p?.saleClosesAt),
    onSale: p?.onSale ?? false,
    sortOrder: (p?.sortOrder ?? 0).toString(),
  };
}

function cents(value: string): number {
  const n = Number(value.replace(',', '.'));
  return Number.isFinite(n) ? Math.round(n * 100) : NaN;
}

/**
 * Product aanmaken of instellen (fase 19): prijs, plaatsen, maximum per bestelling en de verkoopperiode. Bij een
 * pronkzittingavond zijn de plaatsen per avond instelbaar; kaarten per activiteit horen bij een activiteit in de agenda.
 */
export function SaleProductDialog({
  open,
  product,
  initialKind = 'Pronkzitting',
  onClose,
  onSaved,
}: {
  open: boolean;
  product: SaleProduct | null;
  initialKind?: SaleProductKind;
  onClose: () => void;
  onSaved: (message: string) => void;
}) {
  const api = useApi();
  const [form, setForm] = useState<ProductForm>(() => toForm(product, initialKind));
  const events = useAdminEvents(false, open && form.kind === 'EventTicket');
  useEffect(() => {
    if (open) setForm(toForm(product, initialKind));
  }, [open, product, initialKind]);
  const save = useApiMutation(async (f: ProductForm) => {
    const body = {
      kind: f.kind,
      name: f.name.trim(),
      description: f.description.trim() || null,
      eventId: f.kind === 'EventTicket' ? f.eventId || null : null,
      date: f.kind === 'Tokens' ? null : f.date || null,
      priceCents: cents(f.price || '0'),
      capacity: f.capacity === '' ? null : Number(f.capacity),
      maxPerOrder: Number(f.maxPerOrder),
      saleOpensAt: fromLocalInput(f.saleOpensAt),
      saleClosesAt: fromLocalInput(f.saleClosesAt),
      onSale: f.onSale,
      sortOrder: Number(f.sortOrder) || 0,
    };
    return product
      ? api.PUT('/api/v1/admin/sales/products/{id}', { params: { path: { id: product.id } }, body })
      : api.POST('/api/v1/admin/sales/products', { body });
  }, SALES_KEYS);
  const set = (patch: Partial<ProductForm>) => setForm({ ...form, ...patch });

  function submit(event: FormEvent) {
    event.preventDefault();
    save.mutate(form, {
      onSuccess: () => {
        onSaved(product ? `${form.name} opgeslagen.` : `${form.name} toegevoegd.`);
        onClose();
      },
    });
  }

  const priceLabel = form.kind === 'Tokens' ? 'Prijs per munt (€)' : 'Prijs per kaart (€)';
  return (
    <Dialog open={open} title={product ? `Instellen: ${product.name}` : 'Nieuw product'} onClose={onClose}>
      <form onSubmit={submit}>
        <div className="field">
          <label htmlFor="product-soort">Soort</label>
          <select
            id="product-soort"
            value={form.kind}
            disabled={product !== null && product.sold + product.held > 0}
            onChange={(e) => set({ kind: e.target.value as SaleProductKind })}
          >
            {(Object.keys(kindLabels) as SaleProductKind[]).map((k) => (
              <option key={k} value={k}>
                {kindLabels[k]}
              </option>
            ))}
          </select>
        </div>
        <div className="field">
          <label htmlFor="product-naam">Naam</label>
          <input
            id="product-naam"
            required
            maxLength={120}
            value={form.name}
            onChange={(e) => set({ name: e.target.value })}
          />
        </div>
        {form.kind === 'EventTicket' ? (
          <div className="field">
            <label htmlFor="product-activiteit">Activiteit uit de agenda</label>
            <select
              id="product-activiteit"
              required
              value={form.eventId}
              onChange={(e) => set({ eventId: e.target.value })}
            >
              <option value="">Kies een activiteit</option>
              {(events.data ?? []).map((e) => (
                <option key={e.id} value={e.id}>
                  {e.title}
                </option>
              ))}
            </select>
          </div>
        ) : null}
        {form.kind !== 'Tokens' ? (
          <div className="field">
            <label htmlFor="product-datum">{form.kind === 'Pronkzitting' ? 'Avond' : 'Datum'}</label>
            <input id="product-datum" type="date" value={form.date} onChange={(e) => set({ date: e.target.value })} />
          </div>
        ) : null}
        <div className="field">
          <label htmlFor="product-prijs">{priceLabel}</label>
          <input
            id="product-prijs"
            inputMode="decimal"
            required
            pattern="\d{1,4}([,.]\d{1,2})?"
            value={form.price}
            onChange={(e) => set({ price: e.target.value })}
          />
          {form.kind === 'Pronkzitting' ? (
            <small className="muted">Voor niet-leden. Leden bestellen gratis voor hun groep (in de contributie).</small>
          ) : null}
        </div>
        <div className="field">
          <label htmlFor="product-plaatsen">{form.kind === 'Tokens' ? 'Maximaal te verkopen' : 'Plaatsen'}</label>
          <input
            id="product-plaatsen"
            type="number"
            min={0}
            max={100000}
            value={form.capacity}
            onChange={(e) => set({ capacity: e.target.value })}
          />
          <small className="muted">Leeg is onbeperkt.</small>
        </div>
        <div className="field">
          <label htmlFor="product-max">Maximaal per bestelling</label>
          <input
            id="product-max"
            type="number"
            min={1}
            max={500}
            required
            value={form.maxPerOrder}
            onChange={(e) => set({ maxPerOrder: e.target.value })}
          />
        </div>
        <div className="field">
          <label htmlFor="product-open">Verkoop opent</label>
          <input
            id="product-open"
            type="datetime-local"
            value={form.saleOpensAt}
            onChange={(e) => set({ saleOpensAt: e.target.value })}
          />
        </div>
        <div className="field">
          <label htmlFor="product-sluit">Verkoop sluit</label>
          <input
            id="product-sluit"
            type="datetime-local"
            value={form.saleClosesAt}
            onChange={(e) => set({ saleClosesAt: e.target.value })}
          />
        </div>
        <div className="field">
          <label htmlFor="product-omschrijving">Omschrijving (optioneel)</label>
          <textarea
            id="product-omschrijving"
            rows={2}
            maxLength={1000}
            value={form.description}
            onChange={(e) => set({ description: e.target.value })}
          />
        </div>
        <label className="checkbox">
          <input type="checkbox" checked={form.onSale} onChange={(e) => set({ onSale: e.target.checked })} /> Te koop
          (binnen de verkoopperiode)
        </label>
        <ProblemAlert error={save.error} />
        <div className="actions">
          <button type="button" className="button secondary" onClick={onClose}>
            Annuleren
          </button>
          <button type="submit" className="button" disabled={save.isPending}>
            Opslaan
          </button>
        </div>
      </form>
    </Dialog>
  );
}

/**
 * Nieuwe bestelling in het portal (Figma "Nieuwe bestelling"): gratis groepskaarten voor een groep uit vrij veld 3
 * en/of losse kaarten, contant ontvangen of met een betaallink per e-mail. Met alleen losse kaarten en een betaallink
 * is dit de betaallink voor de vrije verkoop.
 */
export function NewOrderDialog({
  open,
  paymentLinkOnly,
  onClose,
  onSaved,
}: {
  open: boolean;
  paymentLinkOnly: boolean;
  onClose: () => void;
  onSaved: (message: string) => void;
}) {
  const api = useApi();
  const products = useSaleProducts();
  const [productId, setProductId] = useState('');
  const [groupName, setGroupName] = useState('');
  const [memberQuantity, setMemberQuantity] = useState('0');
  const [paidQuantity, setPaidQuantity] = useState('1');
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [phone, setPhone] = useState('');
  const [remark, setRemark] = useState('');
  const [payment, setPayment] = useState<PortalPayment>('PaymentLink');
  const product = products.data?.find((p) => p.id === productId);
  const groupOrders = !paymentLinkOnly && product?.kind === 'Pronkzitting';
  const groups = useSaleGroups(open && groupOrders);
  const group = groups.data?.find((g) => g.groupName === groupName);
  useEffect(() => {
    if (open) {
      setProductId('');
      setGroupName('');
      setMemberQuantity('0');
      setPaidQuantity('1');
      setName('');
      setEmail('');
      setPhone('');
      setRemark('');
      setPayment('PaymentLink');
    }
  }, [open]);
  const create = useApiMutation(
    () =>
      api.POST('/api/v1/admin/sales/orders', {
        body: {
          productId,
          groupName: groupOrders && Number(memberQuantity) > 0 ? groupName : null,
          memberQuantity: groupOrders ? Number(memberQuantity) : 0,
          paidQuantity: Number(paidQuantity),
          buyerName: name.trim(),
          buyerEmail: email.trim(),
          buyerPhone: phone.trim() || null,
          remark: remark.trim() || null,
          payment: paymentLinkOnly ? 'PaymentLink' : payment,
        },
      }),
    SALES_KEYS,
  );
  const paid = Number(paidQuantity) || 0;
  const total = product ? paid * product.priceCents : 0;

  function submit(event: FormEvent) {
    event.preventDefault();
    create.mutate(undefined, {
      onSuccess: (response) => {
        const data = (response as { data?: { number: string; status: string } }).data;
        onSaved(
          data?.status === 'AwaitingPayment'
            ? `Bestelling ${data.number} aangemaakt; de betaallink is gemaild naar ${email.trim()}.`
            : `Bestelling ${data?.number ?? ''} aangemaakt en bevestigd; de kaarten zijn gemaild.`,
        );
        onClose();
      },
    });
  }

  return (
    <Dialog open={open} title={paymentLinkOnly ? 'Betaallink maken' : 'Nieuwe bestelling'} onClose={onClose}>
      <form onSubmit={submit}>
        {paymentLinkOnly ? (
          <p className="muted">
            Voor de vrije verkoop: de koper krijgt een betaallink per e-mail (48 uur geldig). Na betalen komen de kaarten
            per e-mail.
          </p>
        ) : null}
        <div className="field">
          <label htmlFor="bestelling-product">Product</label>
          <select id="bestelling-product" required value={productId} onChange={(e) => setProductId(e.target.value)}>
            <option value="">Kies een product</option>
            {(products.data ?? []).map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
                {p.remaining !== null ? ` (nog ${p.remaining} vrij)` : ''}
              </option>
            ))}
          </select>
        </div>
        {groupOrders ? (
          <fieldset>
            <legend>Groepskaarten (leden, gratis)</legend>
            <div className="field">
              <label htmlFor="bestelling-groep">Groep</label>
              <select id="bestelling-groep" value={groupName} onChange={(e) => setGroupName(e.target.value)}>
                <option value="">Geen groepskaarten</option>
                {(groups.data ?? []).map((g) => (
                  <option key={g.groupName} value={g.groupName}>
                    {g.groupName} (nog {g.remaining} van {g.activeMembers})
                  </option>
                ))}
              </select>
            </div>
            {groupName ? (
              <div className="field">
                <label htmlFor="bestelling-groepskaarten">Aantal groepskaarten</label>
                <input
                  id="bestelling-groepskaarten"
                  type="number"
                  min={0}
                  max={group?.remaining ?? 0}
                  value={memberQuantity}
                  onChange={(e) => setMemberQuantity(e.target.value)}
                />
                <small className="muted">
                  Hooguit het aantal actieve leden van de groep, beide avonden samen. Dat is geen reservering.
                </small>
              </div>
            ) : null}
          </fieldset>
        ) : null}
        <div className="field">
          <label htmlFor="bestelling-losse">
            {product?.kind === 'Tokens' ? 'Aantal munten' : groupOrders ? 'Losse kaarten (niet-leden)' : 'Aantal'}
          </label>
          <input
            id="bestelling-losse"
            type="number"
            min={0}
            max={500}
            value={paidQuantity}
            onChange={(e) => setPaidQuantity(e.target.value)}
          />
          {product ? (
            <small className="muted">
              {formatEuro(product.priceCents)} per stuk · totaal {formatEuro(total)}
            </small>
          ) : null}
        </div>
        <div className="field">
          <label htmlFor="bestelling-naam">Naam besteller</label>
          <input
            id="bestelling-naam"
            required
            maxLength={200}
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
        </div>
        <div className="field">
          <label htmlFor="bestelling-email">E-mailadres</label>
          <input
            id="bestelling-email"
            type="email"
            required
            maxLength={254}
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />
        </div>
        <div className="field">
          <label htmlFor="bestelling-telefoon">Telefoon (optioneel)</label>
          <input
            id="bestelling-telefoon"
            type="tel"
            maxLength={40}
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
          />
        </div>
        <div className="field">
          <label htmlFor="bestelling-opmerking">Opmerking of wensen (optioneel)</label>
          <textarea
            id="bestelling-opmerking"
            rows={2}
            maxLength={500}
            value={remark}
            onChange={(e) => setRemark(e.target.value)}
          />
        </div>
        {!paymentLinkOnly && paid > 0 ? (
          <fieldset>
            <legend>Betaling ({formatEuro(total)})</legend>
            <label className="checkbox">
              <input
                type="radio"
                name="betaling"
                checked={payment === 'PaymentLink'}
                onChange={() => setPayment('PaymentLink')}
              />{' '}
              Betaallink per e-mail (iDEAL, 48 uur geldig)
            </label>
            <label className="checkbox">
              <input type="radio" name="betaling" checked={payment === 'Cash'} onChange={() => setPayment('Cash')} />{' '}
              Contant ontvangen
            </label>
          </fieldset>
        ) : null}
        <p className="muted small-text">Kaarten worden niet terugbetaald.</p>
        <ProblemAlert error={create.error} />
        <div className="actions">
          <button type="button" className="button secondary" onClick={onClose}>
            Annuleren
          </button>
          <button
            type="submit"
            className="button"
            disabled={create.isPending || !productId || (Number(memberQuantity) || 0) + paid === 0}
          >
            {paymentLinkOnly || (paid > 0 && payment === 'PaymentLink') ? 'Betaallink versturen' : 'Bestelling opslaan'}
          </button>
        </div>
      </form>
    </Dialog>
  );
}
