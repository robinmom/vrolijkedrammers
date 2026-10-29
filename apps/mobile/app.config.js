const { existsSync } = require('node:fs');

/**
 * Aanvulling op app.json.
 *
 * `google-services.json` (Firebase, push op Android) staat niet in de openbare repository: EAS levert het tijdens de
 * build als geheime bestandsvariabele `GOOGLE_SERVICES_JSON` (runbook push). Lokaal mag het bestand naast app.json
 * staan (staat in .gitignore).
 *
 * `APP_VARIANT=development` (gezet door de dev-buildprofielen in eas.json) geeft de app een eigen identiteit en naam,
 * zodat de dev-app náást de productie-app op één toestel staat in plaats van hem te overschrijven. Het URL-schema
 * blijft bewust gelijk: inloggen en betalen vangen hun terugkeeradres op in de eigen in-app browsersessie, en
 * `drammers://auth` is als terugkeeradres in Entra geregistreerd. Een eigen schema zou het inloggen dus breken zonder
 * iets op te lossen.
 *
 * Let op bij Android: een andere packagenaam vraagt een tweede Android-app in Firebase met een bijbehorende
 * `google-services.json`, anders mislukt de build op de controle van de packagenaam.
 */
const IS_DEV = process.env.APP_VARIANT === 'development';

module.exports = ({ config }) => {
  const googleServicesFile =
    process.env.GOOGLE_SERVICES_JSON ?? (existsSync('./google-services.json') ? './google-services.json' : undefined);
  const identifier = IS_DEV ? 'nl.vrolijkedrammers.app.dev' : config.ios.bundleIdentifier;
  return {
    ...config,
    name: IS_DEV ? 'Drammers (Dev)' : config.name,
    ios: { ...config.ios, bundleIdentifier: identifier },
    android: { ...config.android, package: identifier, ...(googleServicesFile ? { googleServicesFile } : {}) },
  };
};
