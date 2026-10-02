// Contactformulier (fase 21i): één formulier voor alle ontvangers; de e-mailadressen staan niet op de site.
// ?aan=optocht kiest de ontvanger vooraf. Cloudflare Turnstile alleen als de API een sleutel teruggeeft.
(() => {
  'use strict';
  const api = '/api/v1/contact';
  const form = document.getElementById('contact');
  const opened = Date.now();
  let turnstileToken = null;

  const recipient = new URLSearchParams(location.search).get('aan');
  if (recipient && [...form.elements.recipient.options].some((o) => o.value === recipient)) {
    form.elements.recipient.value = recipient;
  }

  fetch(api)
    .then((response) => (response.ok ? response.json() : null))
    .then((config) => {
      if (!config?.turnstileSiteKey) return;
      window.drammersTurnstile = (token) => {
        turnstileToken = token;
      };
      const widget = document.getElementById('turnstile-widget');
      widget.className = 'cf-turnstile';
      widget.dataset.sitekey = config.turnstileSiteKey;
      widget.dataset.callback = 'drammersTurnstile';
      widget.dataset.language = 'nl';
      const script = document.createElement('script');
      script.src = 'https://challenges.cloudflare.com/turnstile/v0/api.js';
      script.async = true;
      document.head.appendChild(script);
    })
    .catch(() => {
      /* zonder configuratie werkt het formulier zonder Turnstile */
    });

  async function problem(response) {
    if (response.status === 429) return 'Je hebt al een paar berichten gestuurd. Probeer het over tien minuten opnieuw.';
    try {
      const body = await response.json();
      if (body.detail) return body.detail;
      if (body.errors) return 'Controleer de ingevulde gegevens.';
    } catch {
      /* geen JSON */
    }
    return 'Er ging iets mis. Probeer het later opnieuw.';
  }

  form.addEventListener('submit', async (event) => {
    event.preventDefault();
    const error = document.getElementById('fout');
    error.hidden = true;
    if (!form.checkValidity()) {
      form.reportValidity();
      return;
    }
    const f = form.elements;
    const body = {
      recipient: f.recipient.value,
      name: f.name.value.trim(),
      email: f.email.value.trim(),
      phone: f.phone.value.trim() || null,
      message: f.message.value.trim(),
      website: f.website.value || null,
      elapsedMs: Date.now() - opened,
      turnstileToken,
    };
    const button = form.querySelector('button[type=submit]');
    button.disabled = true;
    try {
      const response = await fetch(api, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) });
      if (!response.ok) {
        error.textContent = await problem(response);
        error.hidden = false;
        window.turnstile?.reset();
        return;
      }
      document.getElementById('formulier').hidden = true;
      const done = document.getElementById('klaar');
      done.hidden = false;
      done.querySelector('h2').focus();
    } catch {
      error.textContent = 'Geen verbinding. Probeer het opnieuw.';
      error.hidden = false;
    } finally {
      button.disabled = false;
    }
  });
})();
