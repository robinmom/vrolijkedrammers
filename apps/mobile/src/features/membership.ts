/** "21-03-2015" (ook met / of .) → "2015-03-21"; ongeldig of onvolledig → null. */
export function parseDutchDate(value: string): string | null {
  const match = /^(\d{1,2})[-/.](\d{1,2})[-/.](\d{4})$/.exec(value.trim());
  if (!match) {
    return null;
  }
  const [, d, m, y] = match;
  const date = new Date(Date.UTC(Number(y), Number(m) - 1, Number(d)));
  const valid =
    date.getUTCFullYear() === Number(y) && date.getUTCMonth() === Number(m) - 1 && date.getUTCDate() === Number(d);
  return valid ? `${y}-${m!.padStart(2, '0')}-${d!.padStart(2, '0')}` : null;
}

/** Leeftijd in hele jaren op een datum (voor de ouder-sectie onder de 16). */
export function ageOn(isoDate: string, today: Date): number {
  const [y, m, d] = isoDate.split('-').map(Number) as [number, number, number];
  const age = today.getFullYear() - y;
  const beforeBirthday = today.getMonth() + 1 < m || (today.getMonth() + 1 === m && today.getDate() < d);
  return beforeBirthday ? age - 1 : age;
}
