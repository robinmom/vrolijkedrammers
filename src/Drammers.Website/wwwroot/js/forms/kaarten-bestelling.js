// Bestelling (fase 19b): status na het betalen en de QR. Het token in de link geeft toegang (niet te raden).
(() => {
  'use strict';
  const params = new URLSearchParams(window.location.search);
  const id = params.get('id');
  const token = params.get('t');
  const euro = new Intl.NumberFormat('nl-NL', { style: 'currency', currency: 'EUR' });
  const date = new Intl.DateTimeFormat('nl-NL', { weekday: 'long', day: 'numeric', month: 'long', timeZone: 'Europe/Amsterdam' });
  const time = new Intl.DateTimeFormat('nl-NL', { weekday: 'long', day: 'numeric', month: 'long', hour: '2-digit', minute: '2-digit', timeZone: 'Europe/Amsterdam' });
  const $ = (x) => document.getElementById(x);
  let tries = 0;

  if (params.get('melding')) {
    $('melding').textContent = params.get('melding');
    $('melding').hidden = false;
  }
  if (params.get('app') === '1' && id && token) {
    $('app').href = `drammers://kaarten/bestelling?id=${encodeURIComponent(id)}&t=${encodeURIComponent(token)}`;
    $('app').hidden = false;
  }

  function render(order) {
    const what = order.kind === 'Tokens' ? `${order.memberQuantity + order.paidQuantity} munten` : `${order.memberQuantity + order.paidQuantity} kaart(en)`;
    $('titel').textContent = `${order.productName} · ${order.number}`;
    $('info').textContent = [order.date ? date.format(new Date(order.date + 'T12:00:00Z')) : null, what, order.amountCents ? euro.format(order.amountCents / 100) : 'gratis']
      .filter(Boolean).join(' · ');
    $('betalen').hidden = true;
    $('qr').replaceChildren();
    if (order.status === 'Confirmed') {
      $('status').replaceChildren(Object.assign(document.createElement('span'), { className: 'ok', textContent: 'Betaald. ' }),
        order.kind === 'Tokens'
          ? 'Haal je munten op bij de kassa met de munten-QR in de app.'
          : 'Laat de QR scannen bij de ingang. Eén QR geldt voor alle kaarten: bij het scannen gaan alle personen tegelijk naar binnen.');
      for (const ticket of order.tickets.filter((x) => x.status === 'Active' && order.kind !== 'Tokens')) {
        const card = document.createElement('article');
        card.className = 'form-card ticket';
        const img = document.createElement('img');
        img.src = `/api/v1/sales/orders/${encodeURIComponent(id)}/tickets/${encodeURIComponent(ticket.id)}/qr.svg?t=${encodeURIComponent(token)}`;
        img.alt = `QR-code voor ${ticket.quantity} ${ticket.quantity === 1 ? 'persoon' : 'personen'}`;
        img.width = 280;
        img.height = 280;
        const label = document.createElement('p');
        label.textContent = `Geldig voor ${ticket.quantity} ${ticket.quantity === 1 ? 'persoon' : 'personen'}`;
        card.append(img, label);
        $('qr').append(card);
      }
      if (order.tickets.some((x) => x.status === 'Used')) {
        $('qr').append(Object.assign(document.createElement('p'), { className: 'muted', textContent: 'Deze QR is al gescand.' }));
      }
    } else if (order.status === 'AwaitingPayment') {
      $('status').textContent = order.holdUntil
        ? `Nog niet betaald. De plaatsen worden vastgehouden tot ${time.format(new Date(order.holdUntil))}.`
        : 'Nog niet betaald.';
      $('betalen').href = `/api/v1/sales/orders/${encodeURIComponent(id)}/pay?t=${encodeURIComponent(token)}`;
      $('betalen').hidden = false;
      // Net terug van Mollie: de betaling komt vaak een paar seconden later binnen.
      if (tries++ < 20) setTimeout(load, 3000);
    } else {
      $('status').textContent = order.status === 'Cancelled' ? 'Deze bestelling is geannuleerd.' : 'Deze bestelling is verlopen: er is niet op tijd betaald.';
    }
  }

  async function load() {
    if (!id || !token) {
      $('titel').textContent = 'Bestelling niet gevonden';
      return;
    }
    try {
      const response = await fetch(`/api/v1/sales/orders/${encodeURIComponent(id)}?t=${encodeURIComponent(token)}`);
      if (response.status === 404) {
        $('titel').textContent = 'Bestelling niet gevonden';
        $('status').textContent = 'Controleer de link in je e-mail.';
        return;
      }
      if (!response.ok) throw new Error();
      render(await response.json());
    } catch {
      $('status').textContent = 'Je bestelling laden lukt nu niet. Probeer het later opnieuw.';
    }
  }

  void load();
})();
