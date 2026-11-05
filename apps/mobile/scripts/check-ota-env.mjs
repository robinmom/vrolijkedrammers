#!/usr/bin/env node
/**
 * Controleert vóór een OTA-update of de gekozen EAS-omgeving een API-adres heeft, en of dat adres bij het kanaal past.
 *
 * Nodig omdat `eas update` het `env`-blok uit eas.json **niet** gebruikt; die kijkt alleen naar de variabelen die bij
 * EAS op de server staan. Ontbrak er één, dan viel de app stil terug op een ander adres. Op 5 november 2026 praatte de
 * dev-app daardoor een halve ochtend met de productiedatabase. Zie runbook app-uitrollen.
 */
import { execFileSync } from 'node:child_process';

const DEV_API = 'https://app-dvd-api-dev.azurewebsites.net';
const PROD_API = 'https://www.vrolijkedrammers.nl';

/** Welk adres bij welk kanaal hoort. Alleen de winkelversie mag met productie praten. */
const expected = { production: PROD_API, testflight: DEV_API, preview: DEV_API };

const [channel, environment] = process.argv.slice(2);
if (!channel || !environment) {
  console.error('Gebruik: node scripts/check-ota-env.mjs <kanaal> <omgeving>');
  process.exit(2);
}

const wanted = expected[channel];
if (!wanted) {
  console.error(`Onbekend kanaal "${channel}". Bekend: ${Object.keys(expected).join(', ')}.`);
  process.exit(2);
}

let output;
try {
  output = execFileSync('npx', ['eas-cli', 'env:list', environment], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
} catch (error) {
  console.error(`Kan de omgevingsvariabelen van "${environment}" niet ophalen. Ben je ingelogd bij EAS?`);
  console.error(error.stderr?.toString().trim() ?? error.message);
  process.exit(1);
}

const found = output.match(/^EXPO_PUBLIC_API_URL=(.+)$/m)?.[1]?.trim();

if (!found) {
  console.error(`EXPO_PUBLIC_API_URL ontbreekt in de EAS-omgeving "${environment}".`);
  console.error('Zonder die variabele kiest de app zelf een omgeving, en dat is precies wat er op 5-11-2026 misging.');
  console.error('Zet hem eenmalig goed:');
  console.error(`  npx eas-cli env:set --name EXPO_PUBLIC_API_URL --value "${wanted}" \\`);
  console.error(`    --environment ${environment} --visibility plaintext --scope project --type string`);
  process.exit(1);
}

if (found !== wanted) {
  console.error(`Het adres in omgeving "${environment}" past niet bij kanaal "${channel}".`);
  console.error(`  gevonden:  ${found}`);
  console.error(`  verwacht:  ${wanted}`);
  console.error(
    channel === 'production'
      ? 'Zo zou de publieke app met een testomgeving praten.'
      : 'Zo zou een testversie met de echte ledenadministratie praten.',
  );
  process.exit(1);
}

console.log(`OK: kanaal "${channel}" gebruikt ${found} (omgeving "${environment}").`);
