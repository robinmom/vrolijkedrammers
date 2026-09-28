const { existsSync } = require('node:fs');

/**
 * Aanvulling op app.json. `google-services.json` (Firebase, push op Android) staat niet in de openbare repository:
 * EAS levert het tijdens de build als geheime bestandsvariabele `GOOGLE_SERVICES_JSON` (runbook push). Lokaal mag het
 * bestand naast app.json staan (staat in .gitignore).
 */
module.exports = ({ config }) => {
  const googleServicesFile =
    process.env.GOOGLE_SERVICES_JSON ?? (existsSync('./google-services.json') ? './google-services.json' : undefined);
  return {
    ...config,
    android: { ...config.android, ...(googleServicesFile ? { googleServicesFile } : {}) },
  };
};
