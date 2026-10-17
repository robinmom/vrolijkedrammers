/**
 * Basisadres van de API. `EXPO_PUBLIC_API_URL` wordt bij het bundelen ingevuld (per EAS-profiel: preview en testflight
 * in eas.json, production als EAS-omgevingsvariabele). Zonder waarde gebruikt een ontwikkelbuild (`__DEV__`) de
 * Dev-omgeving en een releasebuild productie, zodat een vergeten variabele nooit een winkelversie naar Dev laat wijzen.
 * Alleen HTTPS; er staan geen secrets in de bundle.
 */
export const apiBaseUrl =
  process.env.EXPO_PUBLIC_API_URL ?? (__DEV__ ? 'https://app-dvd-api-dev.azurewebsites.net' : 'https://www.vrolijkedrammers.nl');
