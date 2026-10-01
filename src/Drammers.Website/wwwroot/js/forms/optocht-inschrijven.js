// Inschrijven optocht (fase 11c, sinds 21d op de website): eerst kiezen tussen inloggen en zonder account.
// - Zonder account: formulier → e-mailcode → opgavenummer. Met ?status=<token> alleen de status.
// - Ingelogd (groepsverantwoordelijke): Mijn inschrijving, en een nieuwe inschrijving via dezelfde API als de app
//   (concept → opslaan → indienen), zonder e-mailcode. Inschrijvingen zonder account met hetzelfde e-mailadres koppelt de
//   API aan het account.
(() => {
  'use strict';
  const api = '/api/v1/parade';
  const form = document.getElementById('inschrijven');
  const sections = ['laden', 'gesloten', 'keuze', 'mijn', 'geen-rechten', 'formulier', 'code', 'klaar', 'status'];
  const login = window.DrammersLogin;
  let registrationId = null;
  let categories = [];
  let parade = null;
  // Ingelogd invullen: het concept uit de API (met versie voor het opslaan).
  let draft = null;
  let mine = [];
  let previousBuild = null;

  const show = (id) => {
    for (const section of sections) document.getElementById(section).hidden = section !== id;
    document.getElementById(id).querySelector('h2')?.focus();
  };
  const signedIn = () => Boolean(login?.state.account);
  const text = (id, value) => (document.getElementById(id).textContent = value ?? '–');

  const statusTexts = {
    Draft: ['Concept', 'Nog niet ingediend: vul hem verder in en dien hem in.'],
    Submitted: ['Ingediend', 'De optochtcommissie gaat je inschrijving beoordelen.'],
    UnderReview: ['In behandeling', 'De optochtcommissie beoordeelt je inschrijving.'],
    AdditionalInformationRequired: ['Aanvulling gevraagd', 'De optochtcommissie heeft meer informatie nodig. Je hebt hierover een e-mail gekregen.'],
    Approved: ['Goedgekeurd', 'Je inschrijving is goedgekeurd en daarmee definitief. Tot bij de optocht!'],
    Rejected: ['Afgewezen', 'Je inschrijving is helaas afgewezen.'],
    Withdrawn: ['Ingetrokken', 'Deze inschrijving is ingetrokken.'],
    StartNumberAssigned: ['Startnummer toegekend', 'Je inschrijving is definitief en heeft een startnummer.'],
    Final: ['Definitief', 'Je inschrijving is definitief.'],
  };

  async function problem(response) {
    if (response.status === 429) return { message: 'Te veel pogingen vanaf dit netwerk. Probeer het over tien minuten opnieuw.' };
    try {
      const body = await response.json();
      return { message: body.detail ?? 'Controleer de ingevulde gegevens.', issues: body.issues ?? [] };
    } catch {
      return { message: 'Er ging iets mis. Probeer het later opnieuw.' };
    }
  }

  function fail(element, { message, issues = [] }) {
    element.replaceChildren();
    const p = document.createElement('p');
    p.textContent = message;
    element.append(p);
    const blocking = issues.filter((i) => i.severity === 'Block');
    if (blocking.length) {
      const list = document.createElement('ul');
      list.className = 'issues';
      for (const issue of blocking) {
        const li = document.createElement('li');
        li.textContent = issue.message;
        list.append(li);
      }
      element.append(list);
    }
    element.hidden = false;
  }

  function address(prefix) {
    const f = form.elements;
    return {
      street: f[`${prefix}Street`].value || null,
      houseNumber: f[`${prefix}HouseNumber`].value || null,
      addition: f[`${prefix}Addition`].value || null,
      postalCode: f[`${prefix}PostalCode`].value || null,
      city: f[`${prefix}City`].value || null,
      country: 'NL',
    };
  }

  function categoryHint() {
    const hint = document.getElementById('categorie-hint');
    const category = categories.find((c) => String(c.id) === form.elements.categoryId.value);
    if (!category || (category.minimumParticipants == null && category.maximumParticipants == null)) {
      hint.hidden = true;
      return;
    }
    const min = category.minimumParticipants, max = category.maximumParticipants;
    hint.textContent = `Deze categorie is voor ${min != null && max != null ? `${min} tot en met ${max}` : min != null ? `minimaal ${min}` : `maximaal ${max}`} deelnemers.`;
    hint.hidden = false;
  }

  async function showStatus(token) {
    const response = await fetch(`${api}/public-registrations/status?token=${encodeURIComponent(token)}`).catch(() => null);
    if (!response?.ok) {
      const notFound = response?.status === 404;
      text('gesloten-tekst', notFound
        ? 'Deze statuslink is niet (meer) geldig. Gebruik de link uit de meest recente bevestigingsmail.'
        : response?.status === 429
          ? 'Te veel verzoeken vanaf dit netwerk. Probeer het over een paar minuten opnieuw.'
          : 'De status ophalen lukt nu niet. Probeer het later opnieuw.');
      document.getElementById('titel-gesloten').textContent = notFound ? 'Status niet gevonden' : 'Status nu niet beschikbaar';
      return show('gesloten');
    }
    const s = await response.json();
    const [label, explanation] = statusTexts[s.status] ?? [s.status, ''];
    text('status-tekst', label);
    text('status-uitleg', explanation);
    text('status-optocht', s.paradeName);
    text('status-groep', s.groupName);
    text('status-categorie', s.categoryName);
    text('status-nummer', s.registrationNumber);
    text('status-start', s.startNumber ?? 'nog niet bekend');
    const reason = document.getElementById('status-reden');
    reason.hidden = !s.reason;
    reason.textContent = s.reason ? `Toelichting van de commissie: ${s.reason}` : '';
    show('status');
  }

  function showClosed() {
    if (parade.registrationOpen) return false;
    const opens = new Date(parade.registrationOpensAt);
    if (opens > new Date()) {
      text('gesloten-tekst', `De inschrijving voor de ${parade.name} opent op ${opens.toLocaleString('nl-NL', { dateStyle: 'long', timeStyle: 'short' })}.`);
    } else {
      text('titel-gesloten', 'Inschrijven is gesloten');
      text('gesloten-tekst', `De inschrijving voor de ${parade.name} is gesloten.`);
    }
    document.getElementById('gesloten-inloggen').hidden = !login?.state.available || signedIn();
    show('gesloten');
    return true;
  }

  // Formulier voor gasten (met e-mailcode) of ingelogd (concept van de API, zonder code).
  function showForm(asGuest) {
    document.getElementById('formulier-uitleg').textContent = asGuest
      ? 'Na het versturen krijg je een code per e-mail. Pas na die code is je inschrijving ingediend en krijg je een opgavenummer. De optochtcommissie beoordeelt elke inschrijving; pas na goedkeuring is hij definitief.'
      : 'Je bent ingelogd: na het versturen is je inschrijving direct ingediend en krijg je een opgavenummer. De optochtcommissie beoordeelt elke inschrijving; pas na goedkeuring is hij definitief.';
    show('formulier');
  }

  function fillForm(r) {
    const f = form.elements;
    const set = (name, value) => (f[name].value = value ?? '');
    set('groupName', r.groupName);
    set('contactName', r.contactName);
    set('contactPhone', r.contactPhoneDisplay ?? r.contactPhone);
    set('contactEmail', r.contactEmail);
    set('categoryId', r.categoryId == null ? '' : String(r.categoryId));
    set('subject', r.subject);
    set('subjectDescription', r.subjectDescription);
    set('adultCount', String(r.adultCount ?? 0));
    set('childrenCount', String(r.childrenCount ?? 0));
    if (r.hasMusic != null) f.hasMusic.value = String(r.hasMusic);
    set('estimatedLengthMeters', r.estimatedLengthMeters == null ? '' : String(r.estimatedLengthMeters));
    for (const [prefix, a] of [['build', r.buildAddress], ['jury', r.juryInspectionAddress]]) {
      set(`${prefix}Street`, a?.street);
      set(`${prefix}HouseNumber`, a?.houseNumber);
      set(`${prefix}Addition`, a?.addition);
      set(`${prefix}PostalCode`, a?.postalCode);
      set(`${prefix}City`, a?.city);
    }
    // Bouwlocatie van vorig jaar: kiezen tussen dezelfde of een andere locatie (dan leeg invullen).
    previousBuild = r.buildAddress?.street ? r.buildAddress : null;
    document.getElementById('bouw-keuze').hidden = !previousBuild;
    if (previousBuild) {
      text('bouw-vorig', [`${previousBuild.street} ${previousBuild.houseNumber ?? ''}${previousBuild.addition ?? ''}`.trim(), previousBuild.city].filter(Boolean).join(', '));
      f.bouwKeuze.value = 'zelfde';
    }
    f.jurySame.checked = r.juryInspectionSameAsBuildAddress !== false;
    f.jurySame.dispatchEvent(new Event('change'));
    set('additionalInformation', r.additionalInformation);
    categoryHint();
  }

  // Alleen de inschrijving van de huidige optocht is "Mijn inschrijving"; die van eerdere optochten staan eronder.
  const currentMine = () => mine.filter((r) => r.paradeId === parade?.id);

  function renderMine() {
    const list = document.getElementById('mijn-lijst');
    list.replaceChildren();
    const earlier = document.getElementById('eerder-lijst');
    earlier.replaceChildren();
    for (const r of mine) {
      const target = r.paradeId === parade?.id ? list : earlier;
      const [label, explanation] = statusTexts[r.status] ?? [r.status, ''];
      const li = document.createElement('li');
      const head = document.createElement('p');
      const name = document.createElement('strong');
      name.textContent = r.groupName || 'Inschrijving zonder naam';
      const badge = document.createElement('span');
      badge.className = 'status';
      badge.textContent = label;
      head.append(name, ' ', badge);
      const numbers = document.createElement('p');
      numbers.className = 'muted';
      numbers.textContent = [
        r.registrationNumber ? `Opgavenummer ${r.registrationNumber}` : null,
        r.startNumber ? `startnummer ${r.startNumber}` : null,
        explanation,
      ].filter(Boolean).join(' · ');
      if (target === earlier && r.paradeName) head.prepend(`${r.paradeName}: `);
      li.append(head, numbers);
      target.append(li);
    }
    const active = currentMine().filter((r) => r.status !== 'Withdrawn');
    document.getElementById('mijn-leeg').hidden = currentMine().length > 0;
    document.getElementById('eerder').hidden = earlier.childElementCount === 0;
    const open = Boolean(parade?.registrationOpen);
    document.getElementById('nieuw').hidden = !open || active.length > 0;
    document.getElementById('verder').hidden = !open || !active.some((r) => r.status === 'Draft');
    show('mijn');
  }

  async function startSignedIn() {
    const account = document.getElementById('account');
    text('account-naam', login.state.account.name || login.state.account.username);
    account.hidden = false;
    let me = null;
    try {
      const response = await login.fetch('/api/v1/me');
      me = response.ok ? await response.json() : null;
    } catch {
      me = null;
    }
    if (!me || !me.permissions.includes('parade.register')) {
      if (!me) {
        text('geen-rechten-tekst', 'Je inlog is nog niet gekoppeld aan een account van De Vrolijke Drammers. Maak je account in de app af, of vraag het bestuur om hulp.');
      }
      document.getElementById('als-gast-2').hidden = !parade?.registrationOpen;
      return show('geen-rechten');
    }
    const response = await login.fetch(`${api}/registrations`).catch(() => null);
    mine = response?.ok ? await response.json() : [];
    renderMine();
  }

  async function start() {
    const token = new URLSearchParams(location.search).get('status');
    if (token) return showStatus(token);
    if (login) await login.init('/optocht-inschrijven/');
    const [paradeResponse, categoryResponse] = await Promise.all([fetch(`${api}/current`).catch(() => null), fetch(`${api}/categories`).catch(() => null)]);
    if (!paradeResponse?.ok) {
      text('gesloten-tekst', paradeResponse?.status === 404 ? 'Er is nog geen optocht bekend. Kijk later nog eens.' : 'De gegevens ophalen lukt nu niet. Probeer het later opnieuw.');
      if (paradeResponse?.status !== 404) text('titel-gesloten', 'Inschrijven lukt nu niet');
      return show('gesloten');
    }
    parade = await paradeResponse.json();
    const title = document.querySelector('.page-band h1');
    if (title) title.textContent = `Inschrijven: ${parade.name}`;
    // De API zet de Markdown om naar veilige HTML (alleen opmaak, links en lijsten).
    if (parade.infoHtml) document.getElementById('info').innerHTML = parade.infoHtml;
    form.elements.subject.required = parade.subjectRequired;
    text('label-onderwerp', parade.subjectRequired ? 'Onderwerp' : 'Onderwerp (optioneel)');
    categories = categoryResponse?.ok ? await categoryResponse.json() : [];
    for (const category of categories) {
      const option = document.createElement('option');
      option.value = String(category.id);
      option.textContent = category.name;
      form.elements.categoryId.append(option);
    }
    if (signedIn()) return startSignedIn();
    if (showClosed()) return;
    // Altijd eerst de keuze om in te loggen; zonder werkende login direct het formulier.
    if (login?.state.available) return show('keuze');
    showForm(true);
  }

  async function openDraft(create) {
    const error = document.getElementById('mijn-fout');
    error.hidden = true;
    try {
      const existing = currentMine().find((r) => r.status === 'Draft');
      const response = create
        ? await login.fetch(`${api}/registrations`, { method: 'POST' })
        : await login.fetch(`${api}/registrations/${existing.id}`);
      if (!response.ok) {
        const p = await problem(response);
        const list = await login.fetch(`${api}/registrations`).catch(() => null);
        mine = list?.ok ? await list.json() : mine;
        renderMine();
        return fail(error, p);
      }
      draft = await response.json();
      fillForm(draft);
      showForm(false);
    } catch {
      fail(error, { message: 'Geen verbinding. Probeer het opnieuw.' });
    }
  }

  async function submitSignedIn(registration, error) {
    let response = await login.fetch(`${api}/registrations/${draft.id}`, {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ ...registration, version: draft.version }),
    });
    if (!response.ok) return fail(error, await problem(response));
    draft = await response.json();
    response = await login.fetch(`${api}/registrations/${draft.id}/submit`, { method: 'POST' });
    if (!response.ok) return fail(error, await problem(response));
    const submitted = await response.json();
    text('opgavenummer', String(submitted.registrationNumber));
    document.getElementById('klaar-status').hidden = true;
    document.getElementById('naar-mijn').hidden = false;
    draft = null;
    show('klaar');
  }

  document.getElementById('inloggen').addEventListener('click', () => login.signIn());
  document.getElementById('inloggen-2').addEventListener('click', () => login.signIn());
  document.getElementById('uitloggen').addEventListener('click', () => login.signOut());
  document.getElementById('als-gast').addEventListener('click', () => showForm(true));
  document.getElementById('als-gast-2').addEventListener('click', () => showForm(true));
  document.getElementById('nieuw').addEventListener('click', () => openDraft(true));
  document.getElementById('verder').addEventListener('click', () => openDraft(false));
  document.getElementById('naar-mijn').addEventListener('click', () => startSignedIn());

  form.elements.categoryId.addEventListener('change', categoryHint);
  for (const radio of form.querySelectorAll('input[name=bouwKeuze]')) {
    radio.addEventListener('change', () => {
      const same = form.elements.bouwKeuze.value === 'zelfde';
      for (const field of ['Street', 'HouseNumber', 'Addition', 'PostalCode', 'City']) {
        const key = field.charAt(0).toLowerCase() + field.slice(1);
        form.elements[`build${field}`].value = same && previousBuild ? (previousBuild[key] ?? '') : '';
      }
      if (!same) form.elements.buildStreet.focus();
    });
  }
  form.elements.jurySame.addEventListener('change', () => {
    const separate = !form.elements.jurySame.checked;
    document.getElementById('jury').hidden = !separate;
    for (const name of ['juryStreet', 'juryHouseNumber', 'juryPostalCode', 'juryCity']) form.elements[name].required = separate;
  });

  form.addEventListener('submit', async (event) => {
    event.preventDefault();
    const error = document.getElementById('fout');
    error.hidden = true;
    if (!form.checkValidity()) {
      form.reportValidity();
      return;
    }
    const f = form.elements;
    const length = f.estimatedLengthMeters.value;
    const registration = {
      version: null,
      groupName: f.groupName.value,
      contactName: f.contactName.value,
      contactPhone: f.contactPhone.value,
      contactEmail: f.contactEmail.value.trim(),
      categoryId: Number(f.categoryId.value),
      subject: f.subject.value || null,
      subjectDescription: f.subjectDescription.value || null,
      childrenCount: Number(f.childrenCount.value),
      adultCount: Number(f.adultCount.value),
      hasMusic: f.hasMusic.value === '' ? null : f.hasMusic.value === 'true',
      buildAddress: address('build'),
      juryInspectionSameAsBuildAddress: f.jurySame.checked,
      juryInspectionAddress: f.jurySame.checked ? null : address('jury'),
      estimatedLengthMeters: length ? Math.round(Number(length) * 10) / 10 : null,
      additionalInformation: f.additionalInformation.value || null,
    };
    const button = form.querySelector('button[type=submit]');
    button.disabled = true;
    if (draft && signedIn()) {
      try {
        await submitSignedIn(registration, error);
      } catch {
        fail(error, { message: 'Geen verbinding. Probeer het opnieuw.' });
      } finally {
        button.disabled = false;
      }
      return;
    }
    try {
      const response = await fetch(`${api}/public-registrations`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ registration, rulesAccepted: f.rulesAccepted.checked }),
      });
      if (!response.ok) return fail(error, await problem(response));
      registrationId = (await response.json()).id;
      text('code-email', registration.contactEmail);
      show('code');
    } catch {
      fail(error, { message: 'Geen verbinding. Probeer het opnieuw.' });
    } finally {
      button.disabled = false;
    }
  });

  const verify = document.getElementById('bevestigen');
  verify.addEventListener('submit', async (event) => {
    event.preventDefault();
    const error = document.getElementById('code-fout');
    error.hidden = true;
    const code = verify.elements.code.value.trim();
    if (!/^\d{6}$/.test(code)) return fail(error, { message: 'Vul de 6 cijfers uit de e-mail in.' });
    try {
      const response = await fetch(`${api}/public-registrations/${registrationId}/verify-email`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ code }),
      });
      if (!response.ok) return fail(error, await problem(response));
      const result = await response.json();
      text('opgavenummer', String(result.registrationNumber));
      document.getElementById('status-link').href = `?status=${encodeURIComponent(result.statusToken)}`;
      show('klaar');
    } catch {
      fail(error, { message: 'Geen verbinding. Probeer het opnieuw.' });
    }
  });

  document.getElementById('opnieuw').addEventListener('click', async () => {
    const error = document.getElementById('code-fout');
    const response = await fetch(`${api}/public-registrations/${registrationId}/resend-code`, { method: 'POST' }).catch(() => null);
    fail(error, { message: response?.ok ? 'Er is een nieuwe code verstuurd.' : 'Een nieuwe code sturen lukt nu niet.' });
  });

  start();
})();
