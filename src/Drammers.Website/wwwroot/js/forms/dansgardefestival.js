// Dansgardefestival (2026-10-12): inschrijven met meerdere dansen, wijzigen via de link uit de bevestigingsmail
// (?inschrijving=…&t=…). De kosten zijn een indicatie; betalen gaat pas na goedkeuring via een betaallink.
(() => {
  'use strict';
  const api = '/api/v1/dance-festival';
  const page = document.querySelector('.festival-page');
  const form = document.getElementById('festival');
  const list = document.getElementById('dansen');
  const opened = Date.now();
  const params = new URLSearchParams(location.search);
  const editId = params.get('inschrijving');
  const editToken = params.get('t');
  const prices = {
    soloDuo: Number(page.dataset.priceSolo),
    groupOne: Number(page.dataset.priceGroupOne),
    groupTwo: Number(page.dataset.priceGroupTwo),
  };
  const euro = (cents) => (cents / 100).toLocaleString('nl-NL', { style: 'currency', currency: 'EUR' });
  const typeLabel = { Garde: 'Gardedans', Show: 'Showdans' };
  const formatLabel = { Solo: 'Solo', Duo: 'Duo', Group: 'Groep' };
  const styleLabel = { Character: 'Karakter', Modern: 'Modern' };
  const statusLabel = {
    New: 'Ingediend',
    InfoRequested: 'Meer informatie gevraagd',
    Approved: 'Goedgekeurd',
    Waitlisted: 'Wachtlijst',
    Rejected: 'Niet geplaatst',
    Changed: 'Gewijzigd, wordt beoordeeld',
  };
  let turnstileToken = null;
  let entries = [];
  let editing = -1;

  function category(age) {
    if (!(age > 0)) return null;
    if (age < 9) return ['Minioren', 't/m 8 jaar'];
    if (age < 12) return ['Junioren', '9 t/m 11 jaar'];
    if (age < 16) return ['Jeugd', '12 t/m 15 jaar'];
    return ['Senioren', '16 jaar en ouder'];
  }

  function el(tag, attrs = {}, ...children) {
    const node = document.createElement(tag);
    for (const [key, value] of Object.entries(attrs)) {
      if (value === false || value == null) continue;
      if (key === 'class') node.className = value;
      else if (key.startsWith('on')) node.addEventListener(key.slice(2), value);
      else node.setAttribute(key, value === true ? '' : value);
    }
    for (const child of children.flat()) if (child != null) node.append(child);
    return node;
  }

  // ---- Dansen ----------------------------------------------------------------------------------------------------

  function newEntry(type) {
    return { id: null, danceType: type, format: 'Group', names: '', averageAge: '', dancerCount: '', leaderName: '', title: '', showStyle: null };
  }

  function segmented(name, label, options, value, onChange) {
    return el(
      'fieldset',
      { class: 'segmented' },
      el('legend', { class: 'sub' }, label),
      el(
        'div',
        { class: 'segments', role: 'radiogroup' },
        options.map(([key, text]) =>
          el(
            'label',
            {},
            el('input', { type: 'radio', name, value: key, checked: value === key, onchange: () => onChange(key) }),
            el('span', {}, text),
          ),
        ),
      ),
    );
  }

  function field(label, input, hint) {
    return el('label', {}, label, input, hint ? el('small', { class: 'hint' }, hint) : null);
  }

  function renderEditor(entry, index) {
    const box = el('div', { class: 'dance editing' });
    const sync = () => renderCosts();
    const age = el('input', { inputmode: 'decimal', required: true, value: entry.averageAge, 'aria-describedby': `cat-${index}` });
    const catHint = el('small', { class: 'hint ok', id: `cat-${index}` });
    const showCat = () => {
      const cat = category(Number(String(age.value).replace(',', '.')));
      catHint.textContent = cat ? `→ Categorie ${cat[0]} (${cat[1]})` : ' ';
    };
    age.addEventListener('input', () => {
      entry.averageAge = age.value;
      showCat();
    });
    showCat();
    const fixedCount = entry.format === 'Solo' ? 1 : entry.format === 'Duo' ? 2 : null;
    if (fixedCount) entry.dancerCount = fixedCount;
    const count = el('input', {
      type: 'number',
      min: fixedCount ?? 3,
      max: 200,
      required: true,
      value: entry.dancerCount,
      readonly: fixedCount != null,
      oninput: (e) => (entry.dancerCount = e.target.value),
    });
    const names = el('input', { required: true, maxlength: 300, value: entry.names, oninput: (e) => (entry.names = e.target.value) });
    const leader = el('input', { required: true, maxlength: 100, value: entry.leaderName, oninput: (e) => (entry.leaderName = e.target.value) });
    box.append(
      el(
        'div',
        { class: 'dance-head' },
        el('strong', {}, `Dans ${index + 1}`),
        el('button', { type: 'button', class: 'link danger', onclick: () => remove(index) }, 'Verwijderen'),
      ),
      el(
        'div',
        { class: 'row two' },
        segmented(`type-${index}`, 'Soort dans', [['Garde', 'Gardedans'], ['Show', 'Showdans']], entry.danceType, (v) => {
          entry.danceType = v;
          render();
        }),
        segmented(`format-${index}`, 'Vorm', [['Solo', 'Solo'], ['Duo', 'Duo'], ['Group', 'Groep']], entry.format, (v) => {
          entry.format = v;
          if (v === 'Group' && Number(entry.dancerCount) < 3) entry.dancerCount = '';
          render();
        }),
      ),
      field(entry.format === 'Group' ? 'Naam groep' : entry.format === 'Duo' ? 'Namen dansers' : 'Naam danser', names),
      el(
        'div',
        { class: 'row three-even' },
        field('Gemiddelde leeftijd', age, catHint),
        field('Aantal dansers', count, fixedCount ? el('small', { class: 'hint' }, `Vast bij een ${formatLabel[entry.format].toLowerCase()}`) : null),
        field('Naam leiding', leader),
      ),
    );
    if (entry.danceType === 'Show') {
      box.append(
        el(
          'div',
          { class: 'row two' },
          field('Titel dans', el('input', { required: true, maxlength: 150, value: entry.title ?? '', oninput: (e) => (entry.title = e.target.value) })),
          segmented(`style-${index}`, 'Karakter of modern', [['Character', 'Karakter'], ['Modern', 'Modern']], entry.showStyle, (v) => (entry.showStyle = v)),
        ),
      );
    }
    box.append(el('div', { class: 'actions end' }, el('button', { type: 'button', class: 'secondary', onclick: () => save(index, box) }, 'Dans opslaan')));
    box.addEventListener('input', sync);
    return box;
  }

  function renderSummary(entry, index) {
    const cat = category(Number(String(entry.averageAge).replace(',', '.')));
    const tags = [typeLabel[entry.danceType], formatLabel[entry.format], cat?.[0], entry.showStyle ? styleLabel[entry.showStyle] : null].filter(Boolean);
    const title = entry.danceType === 'Show' && entry.title ? `“${entry.title}”` : entry.names;
    const detail = [
      entry.danceType === 'Show' ? entry.names : null,
      `${entry.dancerCount} danser${Number(entry.dancerCount) === 1 ? '' : 's'}`,
      `gem. ${String(entry.averageAge).replace('.', ',')} jaar`,
      `leiding: ${entry.leaderName}`,
    ].filter(Boolean);
    return el(
      'div',
      { class: `dance summary ${entry.danceType === 'Garde' ? 'garde' : 'show'}` },
      el(
        'div',
        { class: 'dance-text' },
        el('div', { class: 'dance-title' }, el('strong', {}, title), tags.map((t, i) => el('span', { class: `tag${i === 0 ? ' type' : ''}` }, t))),
        el('div', { class: 'muted small' }, detail.join(' · ')),
        entry.status ? el('div', { class: `status-line ${entry.status}` }, statusLabel[entry.status] + (entry.waitlistPosition ? ` (plaats ${entry.waitlistPosition})` : '')) : null,
      ),
      el('button', { type: 'button', class: 'link', onclick: () => edit(index) }, 'Wijzigen'),
    );
  }

  function render() {
    list.replaceChildren(...entries.map((entry, index) => (index === editing ? renderEditor(entry, index) : renderSummary(entry, index))));
    renderCosts();
  }

  function valid(entry) {
    const age = Number(String(entry.averageAge).replace(',', '.'));
    const count = Number(entry.dancerCount);
    return (
      entry.names.trim() &&
      entry.leaderName.trim() &&
      age >= 2 &&
      age <= 99 &&
      (entry.format === 'Solo' ? count === 1 : entry.format === 'Duo' ? count === 2 : count >= 3 && count <= 200) &&
      (entry.danceType !== 'Show' || (entry.title?.trim() && entry.showStyle))
    );
  }

  function save(index, box) {
    if (!valid(entries[index])) {
      for (const input of box.querySelectorAll('input')) input.reportValidity?.();
      showError('Vul alle gegevens van deze dans in' + (entries[index].danceType === 'Show' ? ', ook de titel en karakter of modern.' : '.'));
      return;
    }
    hideError();
    editing = -1;
    render();
  }

  function edit(index) {
    if (editing >= 0 && editing !== index && !valid(entries[editing])) {
      showError('Sla eerst de dans op die u aan het bewerken bent.');
      return;
    }
    editing = index;
    render();
    list.children[index]?.querySelector('input:not([type=radio])')?.focus();
  }

  function remove(index) {
    entries.splice(index, 1);
    editing = -1;
    render();
  }

  for (const button of document.querySelectorAll('[data-add]')) {
    button.addEventListener('click', () => {
      if (editing >= 0 && !valid(entries[editing])) {
        showError('Sla eerst de dans op die u aan het bewerken bent.');
        return;
      }
      hideError();
      entries.push(newEntry(button.dataset.add));
      editing = entries.length - 1;
      render();
      list.lastElementChild?.querySelector('input:not([type=radio])')?.focus();
    });
  }

  // ---- Kosten ----------------------------------------------------------------------------------------------------

  function renderCosts() {
    const lines = [];
    const groups = new Map();
    for (const e of entries) {
      if (e.format === 'Group') {
        const key = e.names.trim().toLowerCase() || `(groep ${groups.size + 1})`;
        groups.set(key, [...(groups.get(key) ?? []), e]);
      } else {
        lines.push([`${e.names || formatLabel[e.format]} – ${typeLabel[e.danceType].toLowerCase()}`, prices.soloDuo]);
      }
    }
    for (const dances of groups.values()) {
      const name = dances[0].names || 'Groep';
      for (let i = 0; i + 1 < dances.length; i += 2) lines.push([`${name} – 2 dansen`, prices.groupTwo]);
      if (dances.length % 2) lines.push([`${name} – ${typeLabel[dances[dances.length - 1].danceType].toLowerCase()}`, prices.groupOne]);
    }
    const target = document.getElementById('kosten-regels');
    target.replaceChildren(
      ...(lines.length
        ? lines.map(([label, cents]) => el('li', {}, el('span', {}, label), el('span', {}, euro(cents))))
        : [el('li', { class: 'muted' }, 'Nog geen dansen toegevoegd.')]),
    );
    document.getElementById('kosten-totaal').textContent = euro(lines.reduce((sum, [, cents]) => sum + cents, 0));
  }

  // ---- Fouten en status ------------------------------------------------------------------------------------------

  function showError(text) {
    const error = document.getElementById('fout');
    error.textContent = text;
    error.hidden = false;
  }

  function hideError() {
    document.getElementById('fout').hidden = true;
  }

  async function problem(response) {
    if (response.status === 429) return 'U heeft al een paar keer verstuurd. Probeer het over tien minuten opnieuw.';
    try {
      const body = await response.json();
      if (body.detail) return body.detail;
      if (body.errors) return 'Controleer de ingevulde gegevens.';
    } catch {
      /* geen JSON */
    }
    return 'Er ging iets mis. Probeer het later opnieuw.';
  }

  function notice(text) {
    const box = document.getElementById('melding');
    box.textContent = text;
    box.hidden = false;
  }

  function closed(text) {
    document.getElementById('status-dicht-tekst').textContent = text;
    document.getElementById('status-dicht').hidden = false;
  }

  const dateText = (iso) =>
    new Date(iso).toLocaleDateString('nl-NL', { weekday: 'long', day: 'numeric', month: 'long', timeZone: 'Europe/Amsterdam' });

  function places(festival) {
    if (!festival.capacity) return;
    const free = Math.max(0, festival.capacity - festival.placesTaken);
    document.getElementById('plekken-titel').textContent =
      free > 0 ? `Nog ${free} van de ${festival.capacity} plekken vrij` : 'Vol: nieuwe dansen komen op de wachtlijst';
    document.getElementById('plekken-balk').style.width = `${Math.min(100, (festival.placesTaken / festival.capacity) * 100)}%`;
    document.getElementById('plekken').hidden = false;
  }

  function turnstile(siteKey) {
    if (!siteKey || editId) return;
    window.drammersTurnstile = (token) => (turnstileToken = token);
    const widget = document.getElementById('turnstile-widget');
    widget.className = 'cf-turnstile';
    widget.dataset.sitekey = siteKey;
    widget.dataset.callback = 'drammersTurnstile';
    widget.dataset.language = 'nl';
    const script = document.createElement('script');
    script.src = 'https://challenges.cloudflare.com/turnstile/v0/api.js';
    script.async = true;
    document.head.appendChild(script);
  }

  // ---- Wijzigen via de link --------------------------------------------------------------------------------------

  function fill(registration) {
    const f = form.elements;
    for (const key of ['clubName', 'contactName', 'addressLine', 'postalCode', 'city', 'phone', 'email', 'remarks']) f[key].value = registration[key] ?? '';
    for (const radio of form.querySelectorAll('[name=photoConsent]')) radio.checked = radio.value === String(registration.photoConsent);
    entries = registration.entries.map((e) => ({ ...e, averageAge: String(e.averageAge).replace('.', ','), title: e.title ?? '' }));
    editing = -1;
    // Akkoord is al gegeven bij de inschrijving.
    form.querySelector('[name=privacyAccepted]').closest('label').hidden = true;
    form.querySelector('[name=rulesAccepted]').closest('label').hidden = true;
    form.querySelector('[name=privacyAccepted]').required = false;
    form.querySelector('[name=rulesAccepted]').required = false;
    document.getElementById('versturen').textContent = 'Wijzigingen opslaan';
    render();
  }

  async function loadOwn() {
    const response = await fetch(`${api}/registrations/${encodeURIComponent(editId)}?t=${encodeURIComponent(editToken)}`);
    if (!response.ok) {
      closed('Deze link is niet (meer) geldig. Neem contact op met de organisatie.');
      return;
    }
    const registration = await response.json();
    fill(registration);
    const open = registration.payments.filter((p) => p.status === 'Open');
    const paid = registration.payments.filter((p) => p.status === 'Paid').reduce((s, p) => s + p.amountCents, 0);
    const parts = [`Inschrijving ${registration.number} van ${registration.clubName}.`];
    if (registration.infoRequest) parts.push(`Vraag van de organisatie: ${registration.infoRequest}`);
    if (paid) parts.push(`Betaald: ${euro(paid)}.`);
    if (open.length) parts.push(`Openstaand: ${euro(open.reduce((s, p) => s + p.amountCents, 0))} (zie de betaallink in uw mail).`);
    if (params.get('betaald')) parts.push('Bedankt voor uw betaling; de verwerking kan even duren.');
    if (!registration.editable) {
      parts.push('Wijzigen kan niet meer; neem contact op met de organisatie als er iets niet klopt.');
      for (const control of form.querySelectorAll('input, textarea, button')) control.disabled = true;
      form.querySelector('#akkoord').hidden = true;
      for (const button of document.querySelectorAll('[data-add]')) button.hidden = true;
      for (const button of list.querySelectorAll('button')) button.hidden = true;
    }
    notice(parts.join(' '));
    form.hidden = false;
  }

  // ---- Start -----------------------------------------------------------------------------------------------------

  // Alleen vaste meldingen (code in de link van de API); nooit vrije tekst uit de link tonen.
  const notices = {
    betaald: 'Dit bedrag is al betaald.',
    verlopen: 'Deze betaallink is niet meer geldig.',
    mislukt: 'Betalen lukt nu niet. Probeer het over een paar minuten opnieuw.',
    'niet-ingesteld': 'Online betalen is nog niet beschikbaar.',
  };
  if (Object.hasOwn(notices, params.get('melding') ?? '')) notice(notices[params.get('melding')]);

  fetch(api)
    .then((response) => (response.ok ? response.json() : null))
    .then(async (config) => {
      if (!config) {
        closed('Inschrijven is nu niet mogelijk.');
        return;
      }
      const festival = config.festival;
      places(festival);
      if (editId && editToken) {
        await loadOwn();
        return;
      }
      if (festival.state === 'NotYetOpen') {
        closed(festival.registrationOpensAt ? `De inschrijving opent op ${dateText(festival.registrationOpensAt)}.` : 'De inschrijving is nog niet open.');
        return;
      }
      if (festival.state !== 'Open') {
        closed('De inschrijving is gesloten.');
        return;
      }
      turnstile(config.turnstileSiteKey);
      form.hidden = false;
      entries = [newEntry('Garde')];
      editing = 0;
      render();
    })
    .catch(() => closed('Geen verbinding. Probeer het later opnieuw.'));

  form.addEventListener('submit', async (event) => {
    event.preventDefault();
    hideError();
    if (editing >= 0) {
      if (!valid(entries[editing])) {
        showError('Vul eerst alle gegevens van de dans in die u aan het bewerken bent.');
        return;
      }
      editing = -1;
      render();
    }
    if (!form.checkValidity()) {
      form.reportValidity();
      return;
    }
    if (!entries.length) {
      showError('Voeg minstens één dans toe.');
      return;
    }
    const f = form.elements;
    const consent = form.querySelector('[name=photoConsent]:checked');
    const body = {
      clubName: f.clubName.value.trim(),
      contactName: f.contactName.value.trim(),
      addressLine: f.addressLine.value.trim(),
      postalCode: f.postalCode.value.trim(),
      city: f.city.value.trim(),
      phone: f.phone.value.trim(),
      email: f.email.value.trim(),
      remarks: f.remarks.value.trim() || null,
      photoConsent: consent ? consent.value === 'true' : null,
      privacyAccepted: f.privacyAccepted.checked,
      rulesAccepted: f.rulesAccepted.checked,
      entries: entries.map((e) => ({
        id: e.id ?? null,
        danceType: e.danceType,
        format: e.format,
        names: e.names.trim(),
        averageAge: Number(String(e.averageAge).replace(',', '.')),
        dancerCount: Number(e.dancerCount),
        leaderName: e.leaderName.trim(),
        title: e.danceType === 'Show' ? e.title.trim() : null,
        showStyle: e.danceType === 'Show' ? e.showStyle : null,
      })),
      website: f.website.value || null,
      elapsedMs: Date.now() - opened,
      turnstileToken,
    };
    const button = document.getElementById('versturen');
    button.disabled = true;
    try {
      const response = editId
        ? await fetch(`${api}/registrations/${encodeURIComponent(editId)}?t=${encodeURIComponent(editToken)}`, {
            method: 'PUT',
            headers: { 'content-type': 'application/json' },
            body: JSON.stringify(body),
          })
        : await fetch(`${api}/registrations`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) });
      if (!response.ok) {
        showError(await problem(response));
        window.turnstile?.reset();
        return;
      }
      const result = editId ? null : await response.json();
      form.hidden = true;
      const done = document.getElementById('klaar');
      document.getElementById('titel-klaar').textContent = editId ? 'Wijzigingen opgeslagen' : 'Bedankt voor uw inschrijving!';
      document.getElementById('klaar-tekst').textContent = editId
        ? 'De organisatie beoordeelt de gewijzigde dansen opnieuw. U krijgt bericht per e-mail.'
        : `${result?.number ? `Uw inschrijfnummer is ${result.number}. ` : ''}U ontvangt een bevestiging per e-mail met een link om uw inschrijving te bekijken of te wijzigen.`;
      done.hidden = false;
      done.querySelector('h2').focus();
    } catch {
      showError('Geen verbinding. Probeer het opnieuw.');
    } finally {
      button.disabled = false;
    }
  });
})();
