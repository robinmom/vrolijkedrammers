/** Alleen GUID's accepteren uit deeplinks (drammers://activiteit/{id}); al het andere is "niet gevonden". */
export const isGuid = (value: unknown): value is string =>
  typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
