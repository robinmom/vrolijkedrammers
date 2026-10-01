// Kaarten (fase 19b): producten, bestellen en betalen via Mollie; vol → wachtlijst. Zelfde API als de app.
(() => {
  'use strict';
  const api = '/api/v1/sales';
  const euro = new Intl.NumberFormat('nl-NL', { style: 'currency', currency: 'EUR' });
  const date = new Intl.DateTimeFormat('nl-NL', { weekday: 'long', day: 'numeric', month: 'long', timeZone: 'Europe/Amsterdam' });
  const form = document.getElementById('formulier');
  const error = document.getElementById('fout');
  let product = null;

  const show = (id) => {
    for (const section of ['producten', 'bestellen', 'wachtlijst-klaar']) {
      document.getElementById(section).hidden = section !== id;
    }
    document.getElementById(id).querySelector('h2')?.focus?.();
  };

  const when = (p) => (p.date ? date.format(new Date(p.date + 'T12:00:00Z')) : '');

  async function problem(response) {
    if (response.status === 429) return { message: 'Te veel pogingen vanaf dit netwerk. Probeer het over tien minuten opnieuw.' };
    try {
      const body = await response.json();
      return { message: body.detail ?? 'Controleer de ingevulde gegevens.', code: body.code };
    } catch {
      return { message: 'Er ging iets mis. Probeer het later opnieuw.' };
    }
  }

  function el(tag, attrs, ...children) {
    const node = document.createElement(tag);
    Object.entries(attrs ?? {}).forEach(([k, v]) => (k === 'class' ? (node.className = v) : node.setAttribute(k, v)));
    children.flat().forEach((c) => node.append(c));
    return node;
  }

  async function load() {
    const list = document.getElementById('lijst');
    try {
      const response = await fetch(`${api}/products`);
      if (!response.ok) throw new Error();
      const catalog = await response.json();
      // Munten zijn alleen voor leden (app); de rest kan iedereen hier kopen.
      const products = catalog.products.filter((p) => !p.membersOnly);
      document.getElementById('laden').hidden = products.length > 0;
      document.getElementById('laden').textContent = 'Er is nu niets te koop.';
      for (const p of products) {
        const status = p.soldOut ? el('span', { class: 'badge vol' }, 'Vol') : p.remaining !== null ? el('span', { class: 'badge' }, `${p.remaining} vrij`) : '';
        const button = el('button', { type: 'button', class: p.soldOut ? 'secondary' : '' }, p.soldOut ? 'Wachtlijst' : 'Bestellen');
        button.addEventListener('click', () => choose(p));
        list.append(
          el('article', { class: 'form-card product' },
            el('div', {},
              el('h3', {}, p.name),
              el('p', { class: 'muted' }, [when(p), p.description].filter(Boolean).join(' · ')),
              el('p', {}, el('span', { class: 'price' }, `${euro.format(p.priceCents / 100)} per kaart`), ' ', status)),
            button));
      }
    } catch {
      document.getElementById('laden').textContent = 'De kaartverkoop laden lukt nu niet. Probeer het later opnieuw.';
    }
  }

  function choose(p) {
    product = p;
    error.hidden = true;
    form.elements.quantity.max = String(p.maxPerOrder);
    document.getElementById('titel-bestellen').textContent = p.name;
    document.getElementById('product-info').textContent = [when(p), `${euro.format(p.priceCents / 100)} per kaart`].filter(Boolean).join(' · ');
    document.getElementById('opmerking').hidden = p.kind !== 'Pronkzitting';
    setSoldOut(p.soldOut);
    updateTotal();
    show('bestellen');
  }

  function setSoldOut(soldOut) {
    document.getElementById('wachtlijst').hidden = !soldOut;
    document.getElementById('naar-wachtlijst').hidden = !soldOut;
    document.getElementById('betalen').hidden = soldOut;
  }

  function updateTotal() {
    const quantity = Number(form.elements.quantity.value) || 0;
    document.getElementById('totaal').textContent = euro.format((quantity * (product?.priceCents ?? 0)) / 100);
  }

  function body() {
    const f = form.elements;
    return {
      productId: product.id, memberQuantity: 0, paidQuantity: Number(f.quantity.value), buyerName: f.buyerName.value.trim(),
      buyerEmail: f.buyerEmail.value.trim(), buyerPhone: f.buyerPhone.value.trim() || null, remark: f.remark.value.trim() || null, channel: 'Web',
    };
  }

  function fail(message) {
    error.textContent = message;
    error.hidden = false;
  }

  form.elements.quantity.addEventListener('input', updateTotal);
  document.getElementById('terug').addEventListener('click', () => show('producten'));

  form.addEventListener('submit', async (event) => {
    event.preventDefault();
    error.hidden = true;
    if (!form.checkValidity()) return form.reportValidity();
    const button = document.getElementById('betalen');
    button.disabled = true;
    try {
      const response = await fetch(`${api}/orders`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body()) });
      if (!response.ok) {
        const p = await problem(response);
        if (p.code === 'SOLD_OUT') setSoldOut(true);
        return fail(p.message);
      }
      const created = await response.json();
      window.location.assign(created.checkoutUrl ?? `/kaarten/bestelling/?id=${created.orderId}&t=${encodeURIComponent(created.token)}`);
    } catch {
      fail('Geen verbinding. Probeer het opnieuw.');
    } finally {
      button.disabled = false;
    }
  });

  document.getElementById('naar-wachtlijst').addEventListener('click', async () => {
    error.hidden = true;
    if (!form.checkValidity()) return form.reportValidity();
    try {
      const response = await fetch(`${api}/waitlist`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body()) });
      if (!response.ok) return fail((await problem(response)).message);
      show('wachtlijst-klaar');
    } catch {
      fail('Geen verbinding. Probeer het opnieuw.');
    }
  });

  void load();
})();
