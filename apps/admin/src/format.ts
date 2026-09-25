const dateTime = new Intl.DateTimeFormat('nl-NL', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Europe/Amsterdam' });
const date = new Intl.DateTimeFormat('nl-NL', { dateStyle: 'medium', timeZone: 'Europe/Amsterdam' });

/** Tijden komen als UTC uit de API en worden in Europe/Amsterdam getoond (docs/03 §7). */
export function formatDateTime(value: string | null | undefined): string {
  return value ? dateTime.format(new Date(value)) : '—';
}

export function formatDate(value: string | null | undefined): string {
  return value ? date.format(new Date(`${value}T12:00:00Z`)) : '—';
}

export const accountStatusLabels: Record<string, string> = {
  Active: 'Actief',
  Blocked: 'Geblokkeerd',
  Disabled: 'Uitgeschakeld',
  Deleted: 'Verwijderd',
};

/** ISO (UTC) → waarde voor &lt;input type="datetime-local"&gt; in de lokale tijd van de browser. */
export function toLocalInput(value: string | null | undefined): string {
  if (!value) {
    return '';
  }
  const d = new Date(value);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** Waarde van &lt;input type="datetime-local"&gt; → ISO (UTC); leeg wordt null. */
export function fromLocalInput(value: string): string | null {
  return value ? new Date(value).toISOString() : null;
}

export const visibilityLabels: Record<string, string> = { Public: 'Iedereen', Members: 'Leden', Restricted: 'Beperkt (rollen)' };
export const statusLabels: Record<string, string> = { Draft: 'Concept', Scheduled: 'Gepland', Published: 'Gepubliceerd', Archived: 'Gearchiveerd' };
