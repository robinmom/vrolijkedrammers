/**
 * Sleutels van gegevens die de app op het toestel bewaart, op één plek: bij uitloggen wist `clearLocalUserData`
 * (auth/localData) ze allemaal. Een nieuwe sleutel met gegevens van een gebruiker hoort hier en daar thuis.
 */
export const storageKeys = {
  /** Persistente querycache van TanStack Query (AsyncStorage). */
  queryCache: 'drammers-query-cache',
  /** Wachtrij met offline scans bij de ingang (AsyncStorage). */
  offlineScans: 'dvd.offline-scans.v1',
  /** Ledenticket voor de QR zonder internet (SecureStore); per kind met `.{childId}` erachter. */
  ticket: 'dvd.ticket.v1',
  /** Status van de hardwaresleutel van het ticket (SecureStore). */
  ticketKeyState: 'dvd.ticketkey.v1',
  /** Kinderen waarvan dit toestel een ticket bewaart (SecureStore). */
  ticketChildren: 'dvd.ticket.children.v1',
  /** Jurypunten van de optocht (AsyncStorage): `jury:scores:{userId}:{paradeId}`. */
  paradeJuryPrefix: 'jury:scores:',
  /** Juryfoto's die nog op internet wachten (AsyncStorage): `jury:photos:{userId}:{paradeId}`. */
  paradeJuryPhotoPrefix: 'jury:photos:',
  /** Punten en opmerkingen van het Dansgardefestival (AsyncStorage): `festival-jury:{userId}:{festivalId}`. */
  festivalJuryPrefix: 'festival-jury:',
} as const;
