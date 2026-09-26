/**
 * Basisadres van de API. `EXPO_PUBLIC_API_URL` wordt bij het bundelen ingevuld (per EAS-profiel);
 * zonder waarde gebruikt de app de Dev-omgeving. Alleen HTTPS; er staan geen secrets in de bundle.
 */
export const apiBaseUrl = process.env.EXPO_PUBLIC_API_URL ?? 'https://app-dvd-api-dev.azurewebsites.net';
