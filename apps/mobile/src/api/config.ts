import * as Updates from 'expo-updates';

const DEV_API = 'https://app-dvd-api-dev.azurewebsites.net';
const PROD_API = 'https://www.vrolijkedrammers.nl';

/**
 * Het kanaal waarop deze build staat: `production` voor de winkelversie, `preview` of `testflight` voor de dev-app.
 * Het wordt bij het **bouwen** in de app gezet en een update kan het niet veranderen — een update bepaalt juist aan de
 * hand van dit kanaal of hij voor deze app bedoeld is. In een lokale ontwikkelbuild is het `null`.
 */
const channel = Updates.channel;

/** Alleen de winkelversie praat met productie; al het andere is een testversie. */
const isProductionBuild = channel === 'production';

/**
 * Basisadres van de API. `EXPO_PUBLIC_API_URL` wordt bij het bundelen ingevuld en is de normale weg.
 *
 * Ontbreekt die variabele, dan is er iets misgegaan in het uitrolcommando en bepaalt het kanaal van de build de
 * omgeving. Dat is een bewuste wijziging na 5 november 2026: toen viel de app terug op `__DEV__ ? dev : productie`,
 * en omdat `__DEV__` in een uitgerolde bundel onwaar is, praatte de **dev-app een halve ochtend met de
 * productiedatabase** zonder dat daar iets aan te zien was. Het kanaal kan die fout niet maken: een testbuild staat
 * nooit op `production`. Zie runbook app-uitrollen.
 *
 * Alleen HTTPS; er staan geen secrets in de bundle.
 */
export const apiBaseUrl = process.env.EXPO_PUBLIC_API_URL ?? (isProductionBuild ? PROD_API : DEV_API);

/**
 * Of dit een testversie is: de dev-app of een lokale ontwikkelbuild. Gebruikt om de omgeving in beeld te tonen, zodat
 * een verkeerd uitgerolde bundel meteen opvalt in plaats van pas na een halve ochtend zoeken.
 */
export const isTestBuild = !isProductionBuild;

/** Korte naam van de omgeving waar de app mee praat; voor de balk in een testversie. */
export const apiEnvironmentLabel =
  apiBaseUrl === PROD_API ? 'productie' : apiBaseUrl === DEV_API ? 'dev' : apiBaseUrl.replace(/^https:\/\//, '');

/** True als een testversie met productie praat: dat hoort nooit en moet opvallen. */
export const isTestBuildOnProduction = isTestBuild && apiBaseUrl === PROD_API;
