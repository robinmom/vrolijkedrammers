/**
 * Welke API de app kiest als `EXPO_PUBLIC_API_URL` niet is meegekomen in de bundel.
 *
 * Dit is de les van 5 november 2026: toen viel de app terug op `__DEV__ ? dev : productie`, en omdat `__DEV__` in een
 * uitgerolde bundel onwaar is, praatte de dev-app met de productiedatabase. De keuze hangt nu aan het kanaal, dat bij
 * het bouwen in de app wordt gezet en door een update niet te veranderen is.
 */

const DEV_API = 'https://app-dvd-api-dev.azurewebsites.net';
const PROD_API = 'https://www.vrolijkedrammers.nl';

/** Laadt config.ts opnieuw met een bepaald kanaal en een bepaalde omgevingsvariabele. */
function loadConfig(channel: string | null, apiUrl?: string) {
  let config!: typeof import('../api/config');
  jest.isolateModules(() => {
    jest.doMock('expo-updates', () => ({ channel }));
    const before = process.env.EXPO_PUBLIC_API_URL;
    if (apiUrl === undefined) {
      delete process.env.EXPO_PUBLIC_API_URL;
    } else {
      process.env.EXPO_PUBLIC_API_URL = apiUrl;
    }
    // require en geen import: de module moet per geval opnieuw geladen worden, want hij bepaalt de omgeving
    // eenmalig bij het laden.
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    config = require('../api/config');
    if (before === undefined) {
      delete process.env.EXPO_PUBLIC_API_URL;
    } else {
      process.env.EXPO_PUBLIC_API_URL = before;
    }
  });
  return config;
}

describe('Welke API de app aanspreekt', () => {
  it('de variabele wint: die wordt bij het bundelen ingevuld', () => {
    expect(loadConfig('production', DEV_API).apiBaseUrl).toBe(DEV_API);
    expect(loadConfig('testflight', PROD_API).apiBaseUrl).toBe(PROD_API);
  });

  it('zonder variabele praat alleen de winkelversie met productie', () => {
    expect(loadConfig('production').apiBaseUrl).toBe(PROD_API);
  });

  it('zonder variabele kiest een testversie nooit productie', () => {
    // Precies de fout van 5-11-2026: een update zonder variabele op de dev-app.
    expect(loadConfig('testflight').apiBaseUrl).toBe(DEV_API);
    expect(loadConfig('preview').apiBaseUrl).toBe(DEV_API);
    // Lokale ontwikkelbuild: dan is er geen kanaal.
    expect(loadConfig(null).apiBaseUrl).toBe(DEV_API);
  });

  it('een testversie herkent zichzelf, de winkelversie niet', () => {
    expect(loadConfig('testflight').isTestBuild).toBe(true);
    expect(loadConfig(null).isTestBuild).toBe(true);
    expect(loadConfig('production').isTestBuild).toBe(false);
  });

  it('slaat alarm als een testversie tóch met productie praat', () => {
    expect(loadConfig('testflight', PROD_API).isTestBuildOnProduction).toBe(true);
    // De winkelversie op productie is juist goed, en een testversie op dev ook.
    expect(loadConfig('production', PROD_API).isTestBuildOnProduction).toBe(false);
    expect(loadConfig('testflight', DEV_API).isTestBuildOnProduction).toBe(false);
  });

  it('noemt de omgeving bij naam, en anders het adres zelf', () => {
    expect(loadConfig('testflight', DEV_API).apiEnvironmentLabel).toBe('dev');
    expect(loadConfig('production', PROD_API).apiEnvironmentLabel).toBe('productie');
    expect(loadConfig('testflight', 'https://proef.example.com').apiEnvironmentLabel).toBe('proef.example.com');
  });
});
