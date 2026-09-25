/**
 * Datum- en tijdnotatie in het Nederlands, altijd in de tijdzone van Loil. De API levert UTC (met `Z`).
 * Bewust zonder `formatToParts`: elk onderdeel heeft een eigen formatter, zodat Hermes en Node gelijk werken.
 */
const zone = 'Europe/Amsterdam';
const fmt = (options: Intl.DateTimeFormatOptions) => new Intl.DateTimeFormat('nl-NL', { timeZone: zone, ...options });

const weekdayShort = fmt({ weekday: 'short' });
const dayTwoDigit = fmt({ day: '2-digit' });
const monthShort = fmt({ month: 'short' });
const time = fmt({ hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });
const hour = fmt({ hour: 'numeric', hourCycle: 'h23' });
const monthYear = fmt({ month: 'long', year: 'numeric' });
const longDate = fmt({ weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
const dayMonthYear = fmt({ day: 'numeric', month: 'long', year: 'numeric' });
const shortDate = fmt({ day: 'numeric', month: 'short', year: 'numeric' });

const clean = (value: string) => value.replace('.', '').toUpperCase();
const capitalize = (value: string) => value.charAt(0).toUpperCase() + value.slice(1);

/** Datumblok (Figma 02): "WO" · "11" · "NOV". */
export function dateBlockParts(iso: string) {
  const date = new Date(iso);
  return { weekday: clean(weekdayShort.format(date)).slice(0, 2), day: dayTwoDigit.format(date), month: clean(monthShort.format(date)).slice(0, 3) };
}

/** "11:11 uur", "20:00 – 00:30 uur" of "Hele dag". */
export function timeRange(startIso: string, endIso: string | null, allDay: boolean): string {
  if (allDay) {
    return 'Hele dag';
  }
  const start = time.format(new Date(startIso));
  return endIso ? `${start} – ${time.format(new Date(endIso))} uur` : `${start} uur`;
}

export const startTime = (iso: string) => `${time.format(new Date(iso))} uur`;

/** Sectiekop in het programma: "NOVEMBER 2026". */
export const monthSection = (iso: string) => monthYear.format(new Date(iso)).toUpperCase();

/** "Zaterdag 16 januari 2027". */
export const fullDate = (iso: string) => capitalize(longDate.format(new Date(iso)));

/** "24 september 2026" (uitgelicht nieuws) en "20 sep 2026" (nieuwslijst). */
export const newsDateLong = (iso: string) => dayMonthYear.format(new Date(iso));
export const newsDateShort = (iso: string) => shortDate.format(new Date(iso)).replace('.', '');

/** Groet op Home op basis van het uur in Loil. */
export function greeting(now: Date): string {
  const h = Number(hour.format(now));
  if (h < 12) {
    return 'Goedemorgen';
  }
  return h < 18 ? 'Goedemiddag' : 'Goedenavond';
}

/**
 * Middernacht (Loil) van een `DateOnly` als tijdstip. Carnaval valt altijd vóór de zomertijd
 * (uiterlijk begin maart), dus de UTC-afwijking is +1 uur.
 */
export function carnivalMidnight(dateOnly: string): Date {
  const [y = 1970, m = 1, d = 1] = dateOnly.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d) - 60 * 60 * 1000);
}

export interface Countdown {
  days: number;
  hours: number;
  minutes: number;
  seconds: number;
}

/** Resterende tijd tot `target`; `null` als het moment al voorbij is. */
export function countdown(target: Date, now: Date): Countdown | null {
  const ms = target.getTime() - now.getTime();
  if (ms <= 0) {
    return null;
  }
  const total = Math.floor(ms / 1000);
  return { days: Math.floor(total / 86400), hours: Math.floor((total % 86400) / 3600), minutes: Math.floor((total % 3600) / 60), seconds: total % 60 };
}

/** "2026/2027" → "Seizoen 2026–2027". */
export const seasonLabel = (name: string) => `Seizoen ${name.replace('/', '–')}`;
