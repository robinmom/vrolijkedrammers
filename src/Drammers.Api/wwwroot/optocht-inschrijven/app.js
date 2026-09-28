// Inschrijven optocht zonder account (fase 11c): formulier → e-mailcode → opgavenummer. Met ?status=<token> alleen de status.
(() => {
  'use strict';
  const api = '/api/v1/parade';
  const form = document.getElementById('inschrijven');
  const sections = ['laden', 'gesloten', 'formulier', 'code', 'klaar', 'status'];
  let registrationId = null;
  let categories = [];

  const show = (id) => {
    for (const section of sections) document.getElementById(section).hidden = section !== id;
    document.getElementById(id).querySelector('h2')?.focus();
  };
  const text = (id, value) => (document.getElementById(id).textContent = value ?? '–');

  const statusTexts = {
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

  async function start() {
    const token = new URLSearchParams(location.search).get('status');
    if (token) return showStatus(token);
    const [paradeResponse, categoryResponse] = await Promise.all([fetch(`${api}/current`).catch(() => null), fetch(`${api}/categories`).catch(() => null)]);
    if (!paradeResponse?.ok) {
      text('gesloten-tekst', paradeResponse?.status === 404 ? 'Er is nog geen optocht bekend. Kijk later nog eens.' : 'De gegevens ophalen lukt nu niet. Probeer het later opnieuw.');
      return show('gesloten');
    }
    const parade = await paradeResponse.json();
    text('optocht-naam', `Inschrijven: ${parade.name}`);
    if (!parade.registrationOpen) {
      const opens = new Date(parade.registrationOpensAt);
      text('gesloten-tekst', opens > new Date()
        ? `De inschrijving opent op ${opens.toLocaleString('nl-NL', { dateStyle: 'long', timeStyle: 'short' })}.`
        : 'De inschrijving voor deze optocht is gesloten.');
      return show('gesloten');
    }
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
    show('formulier');
  }

  form.elements.categoryId.addEventListener('change', categoryHint);
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
      buildAddress: address('build'),
      juryInspectionSameAsBuildAddress: f.jurySame.checked,
      juryInspectionAddress: f.jurySame.checked ? null : address('jury'),
      estimatedLengthMeters: length ? Math.round(Number(length) * 10) / 10 : null,
      additionalInformation: f.additionalInformation.value || null,
    };
    const button = form.querySelector('button[type=submit]');
    button.disabled = true;
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
