// Lid worden (fase 9b): formulier → e-mailcode → ingediend. Zelfde API en wachtrij als de app; bron "Website".
(() => {
  'use strict';
  const api = '/api/v1/membership-applications';
  const form = document.getElementById('aanmelden');
  const guardian = document.getElementById('ouder');
  const emailLabel = document.getElementById('label-email');
  let applicationId = null;

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
    return value ? ageOn(new Date(value + 'T12:00:00'), new Date()) < 16 : false;
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
      iban: f.iban.value, accountHolder: f.accountHolder.value, mandateConsent: f.mandateConsent.checked,
      privacyConsent: f.privacyConsent.checked, photoConsent: f.photoConsent.checked, source: 'Website',
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
