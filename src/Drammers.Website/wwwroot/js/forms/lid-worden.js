// Lid worden (fase 9b): formulier → e-mailcode → ingediend. Zelfde API en wachtrij als de app; bron "Website".
(() => {
  'use strict';
  const api = '/api/v1/membership-applications';
  const form = document.getElementById('aanmelden');
  const guardian = document.getElementById('ouder');
  const emailLabel = document.getElementById('label-email');
  let applicationId = null;

  // Lid splitsen (fase 25): met de persoonlijke link uit de mail aan het hoofdlid (?splitsen=…) staat alles klaar.
  const splitToken = new URLSearchParams(location.search).get('splitsen');
  const splitInfo = document.getElementById('splitsen-info');
  const title = document.getElementById('titel-formulier');
  let splitReady = false;
  const isSplit = () => form.elements.kind.value === 'splitsen';

  function applyMode() {
    const split = isSplit();
    document.getElementById('soort').hidden = split;
    document.getElementById('contributie').hidden = split;
    for (const name of ['iban', 'accountHolder', 'mandateConsent']) form.elements[name].required = !split;
    title.textContent = split ? 'Tweede lid registreren' : 'Aanmelden als lid';
    splitInfo.hidden = !split;
    if (split && !splitReady) {
      splitInfo.textContent =
        'Gebruik de persoonlijke link uit de e-mail van het secretariaat aan het hoofdlid. Geen e-mail ontvangen? Stuur ons een bericht via de contactpagina.';
    }
    form.querySelector('button[type=submit]').disabled = split && !splitReady;
  }

  for (const radio of form.querySelectorAll('input[name=kind]')) radio.addEventListener('change', applyMode);

  if (splitToken) {
    form.elements.kind.value = 'splitsen';
    applyMode();
    fetch(`${api}/split/${encodeURIComponent(splitToken)}`)
      .then(async (response) => {
        if (!response.ok) {
          splitInfo.textContent = 'Deze link is niet (meer) geldig. Vraag het secretariaat om een nieuwe via de contactpagina.';
          return;
        }
        const p = await response.json();
        const f = form.elements;
        const set = (name, value) => {
          if (value && !f[name].value) f[name].value = value;
        };
        set('firstName', p.secondFirstName);
        set('namePrefix', p.secondNamePrefix);
        set('lastName', p.secondLastName);
        set('addressLine', p.addressLine);
        set('postalCode', p.postalCode);
        set('city', p.city);
        set('email', p.email);
        splitReady = true;
        splitInfo.textContent = `Je registreert het tweede lid van het lidmaatschap van ${p.mainMemberName}. De contributie blijft via ${p.mainMemberName} lopen; een IBAN is niet nodig. Heeft het tweede lid een eigen e-mailadres, vul dat dan in.`;
        applyMode();
      })
      .catch(() => {
        splitInfo.textContent = 'Geen verbinding. Laad de pagina opnieuw.';
      });
  }

  const show = (id) => {
    for (const section of ['formulier', 'code', 'klaar']) {
      document.getElementById(section).hidden = section !== id;
    }
    document.getElementById(id).querySelector('h2').focus?.();
  };

  const ageOn = (birth, today) => {
    let age = today.getFullYear() - birth.getFullYear();
    const beforeBirthday = today.getMonth() < birth.getMonth() || (today.getMonth() === birth.getMonth() && today.getDate() < birth.getDate());
    return beforeBirthday ? age - 1 : age;
  };

  const isMinor = () => {
    const value = form.elements.birthDate.value;
    return value ? ageOn(new Date(value + 'T12:00:00'), new Date()) < 15 : false;
  };

  form.elements.birthDate.addEventListener('change', () => {
    const minor = isMinor();
    guardian.hidden = !minor;
    form.elements.guardianName.required = minor;
    form.elements.guardianPhone.required = minor;
    emailLabel.textContent = minor ? 'E-mailadres ouder/verzorger' : 'E-mailadres';
  });

  async function problem(response) {
    if (response.status === 429) return 'Te veel pogingen vanaf dit netwerk. Probeer het over tien minuten opnieuw.';
    try {
      const body = await response.json();
      if (body.detail) return body.detail;
      if (body.errors) return 'Controleer de ingevulde gegevens.';
    } catch {
      /* geen JSON */
    }
    return 'Er ging iets mis. Probeer het later opnieuw.';
  }

  function fail(element, message) {
    element.textContent = message;
    element.hidden = false;
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
      firstName: f.firstName.value, namePrefix: f.namePrefix.value || null, lastName: f.lastName.value,
      gender: f.gender.value || null, birthDate: f.birthDate.value, addressLine: f.addressLine.value,
      postalCode: f.postalCode.value, city: f.city.value, email: f.email.value, phone: f.phone.value || null,
      guardianName: isMinor() ? f.guardianName.value : null, guardianPhone: isMinor() ? f.guardianPhone.value : null,
      iban: isSplit() ? null : f.iban.value, accountHolder: isSplit() ? null : f.accountHolder.value,
      mandateConsent: isSplit() ? false : f.mandateConsent.checked,
      privacyConsent: f.privacyConsent.checked, photoConsent: f.photoConsent.checked, source: 'Website',
      membershipType: isSplit() ? 'Individual' : f.membershipType.value, splitToken: isSplit() ? splitToken : null,
    };
    const button = form.querySelector('button[type=submit]');
    button.disabled = true;
    try {
      const response = await fetch(api, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) });
      if (!response.ok) return fail(error, await problem(response));
      applicationId = (await response.json()).id;
      document.getElementById('code-email').textContent = body.email;
      show('code');
    } catch {
      fail(error, 'Geen verbinding. Probeer het opnieuw.');
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
    if (!/^\d{6}$/.test(code)) return fail(error, 'Vul de 6 cijfers uit de e-mail in.');
    try {
      const response = await fetch(`${api}/${applicationId}/verify-email`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ code }) });
      if (!response.ok) return fail(error, await problem(response));
      show('klaar');
    } catch {
      fail(error, 'Geen verbinding. Probeer het opnieuw.');
    }
  });

  document.getElementById('opnieuw').addEventListener('click', async () => {
    const error = document.getElementById('code-fout');
    const response = await fetch(`${api}/${applicationId}/resend-code`, { method: 'POST' }).catch(() => null);
    fail(error, response?.ok ? 'Er is een nieuwe code verstuurd.' : 'Een nieuwe code sturen lukt nu niet.');
  });
})();
