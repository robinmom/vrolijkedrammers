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
