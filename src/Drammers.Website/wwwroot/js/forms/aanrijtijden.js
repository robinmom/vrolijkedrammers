// Openbare aanrijtijden (fase 16, sinds 21d een pagina van de website): per startnummer de tijd bij de meldplek.
(() => {
  'use strict';
  const status = document.getElementById('status');
  const date = new Intl.DateTimeFormat('nl-NL', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'Europe/Amsterdam' });

  const cell = (text) => {
    const td = document.createElement('td');
    td.textContent = text ?? '';
    return td;
  };

  fetch('/api/v1/parade/arrival-times')
    .then((response) => (response.ok ? response.json() : Promise.reject(response.status)))
    .then((data) => {
      // Titel en datum in de paginakop van de website.
      const title = document.querySelector('.page-band h1');
      const lead = document.querySelector('.page-band .lead');
      if (data.paradeName && title) title.textContent = `Aanrijtijden ${data.paradeName}`;
      if (data.paradeDate && lead) lead.textContent = `Optocht op ${date.format(new Date(`${data.paradeDate}T12:00:00Z`))}.`;
      if (!data.published || data.rows.length === 0) {
        status.textContent = 'De aanrijtijden zijn nog niet bekend. Kijk later nog eens.';
        return;
      }
      document.getElementById('plek').textContent = data.location || 'Aanrijtijd';
      document.getElementById('uitleg').textContent = data.location
        ? `Tijd waarop de wagen bij ${data.location} moet zijn.`
        : 'Tijd waarop de wagen aanwezig moet zijn.';
      const body = document.getElementById('rijen');
      for (const row of data.rows) {
        const tr = document.createElement('tr');
        tr.append(cell(String(row.startNumber ?? '')), cell(row.category), cell(row.groupName), cell(`${row.arrivalTime} uur`));
        body.append(tr);
      }
      status.textContent = '';
      status.hidden = true;
      document.getElementById('lijst').hidden = false;
    })
    .catch(() => {
      status.textContent = 'De aanrijtijden kunnen nu niet worden geladen. Probeer het later opnieuw.';
    });
})();
