# Runbook: de app uitrollen — OTA-update of echte build?

Sinds 2026-11-04 heeft de app **EAS Update** aan boord (`expo-updates`). Daarmee kan een wijziging in JavaScript
rechtstreeks naar de toestellen, zonder nieuwe build en zonder beoordeling door Apple of Google. Buildtegoed is schaars,
dus dit is de standaardmanier.

**Let op:** een OTA-update komt alleen aan op een app die zélf al met deze configuratie is gebouwd. De versies die op
2026-10-18 in de winkels kwamen (iOS build 12, Android versiecode 4) hebben de updates-client niet en krijgen dus nooit
een OTA. De eerste build na deze wijziging is de laatste die Apple en Google nog moeten beoordelen.

## Welke van de twee heb ik nodig?

| Wat je veranderde | Uitrollen met |
|---|---|
| Schermen, logica, teksten, styling, afbeeldingen in `assets` | **OTA-update** |
| Een nieuwe of gewijzigde afhankelijkheid **met native code** (`npx expo install ...`) | echte build |
| `app.json`, `app.config.js`, een config-plugin, rechten, iconen, naam of bundle-id | echte build |
| Een Expo-SDK-upgrade | echte build |

Twijfel je, dan is het een build. Een OTA die niet bij de runtime past komt niet aan (of erger: hij komt aan en de app
crasht op een module die er niet is).

## De runtime hangt aan `expo.version`

`app.json` staat op `runtimeVersion: { "policy": "appVersion" }`. Een build en een update horen bij elkaar als
`expo.version` gelijk is — nu `1.1.0`. Niet de fingerprint-policy, want het berekenen van een fingerprint loopt op deze
repo vast omdat hij in iCloud staat (zie de valkuil onderaan).

Daar hoort één regel bij, en die is belangrijk:

> **Verander je iets native, verhoog dan `expo.version` in `app.json` in dezelfde wijziging.**

Doe je dat niet, dan sturen latere OTA-updates JavaScript dat native modules verwacht die niet in de geïnstalleerde app
zitten. Apple vraagt bij een release toch al om een hoger versienummer, dus dit sluit aan bij wat je al moet doen.
Het buildnummer gaat automatisch omhoog (`autoIncrement` met `appVersionSource: remote`); dat is iets anders dan
`version`.

## Kanalen

Elk buildprofiel heeft een eigen kanaal, zodat een testupdate nooit op de publieke app landt:

| Profiel | Kanaal | App | API |
|---|---|---|---|
| `preview` | `preview` | Drammers (Dev), APK | dev |
| `testflight` | `testflight` | Drammers (Dev), TestFlight | dev |
| `production` | `production` | De Vrolijke Drammers, App Store en Play Store | prod |

## Een OTA-update uitrollen

Vanuit `apps/mobile`, met `main` bijgewerkt en de controles groen (`pnpm lint`, `pnpm typecheck`, `pnpm test`):

```bash
# eerst op de dev-app, zodat je het ziet werken
npx eas-cli update --branch testflight --channel testflight --environment preview \
  --message "Gelijke stand bij de jury"

# en daarna naar de echte app
npx eas-cli update --branch production --channel production --environment production \
  --message "Gelijke stand bij de jury"
```

`--environment` is verplicht vanaf SDK 55. Het kiest welke omgevingsvariabelen in de bundel komen — dus welke API de
app aanspreekt. **Verwissel dit niet:** `production` met `--environment preview` zou de publieke app naar de
dev-omgeving laten praten.

De app haalt de update op bij het opstarten en gebruikt hem bij de **volgende** keer opstarten. Een tester die nu in de
app zit, ziet het dus niet meteen.

Terugdraaien kan zonder build:

```bash
npx eas-cli update:list --branch production          # welke updates staan er
npx eas-cli update:republish --branch production --group <id>   # zet een oudere terug
```

## Een echte build

Zie runbook testflight voor de eenmalige Apple-stappen. Daarna, vanuit `apps/mobile`:

```bash
EAS_SKIP_AUTO_FINGERPRINT=1 npx eas-cli build --platform all --profile production --auto-submit
```

## Valkuilen

- **`EAS_SKIP_AUTO_FINGERPRINT=1` altijd meegeven.** Zonder die variabele blijft `eas build` hangen op "Computing
  project fingerprint", omdat de repository in een iCloud-map staat. Zie runbook iCloud-duplicaten.
- **`eas update:configure` schrijft te veel.** Toen het eenmalig gedraaid werd, dupliceerde het de hele
  `blockedPermissions`-lijst in `app.json` en zette het er een `permissions`-lijst bij die het uit de config-plugins had
  uitgerekend — met de microfoon erin, die juist geblokkeerd hoort te zijn. Draai het niet opnieuw; is het toch nodig,
  controleer dan de diff van `app.json` regel voor regel en houd alleen `updates` en `runtimeVersion` over.
- **Een update op het verkeerde kanaal.** `--channel` en `--environment` horen bij elkaar (zie de tabel). Controleer het
  voordat je het naar `production` stuurt.
