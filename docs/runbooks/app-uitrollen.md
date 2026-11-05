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
pnpm ota:testflight --message "Gelijke stand bij de jury"

# en daarna naar de echte app
pnpm ota:production --message "Gelijke stand bij de jury"
```

Die twee scripts controleren eerst of de gekozen EAS-omgeving een API-adres heeft dat bij het kanaal past, en rollen
pas daarna uit. Slaat de controle aan, dan gebeurt er niets en staat er in de uitvoer wat je moet zetten. Los
controleren kan met `pnpm check:ota <kanaal> <omgeving>`.

Het kale commando werkt nog steeds, maar dan sla je de controle over:

```bash
EAS_SKIP_AUTO_FINGERPRINT=1 npx eas-cli update --channel testflight --environment preview \
  --message "Gelijke stand bij de jury"
```

`--channel` is genoeg: EAS zoekt zelf de branch op die eraan hangt. `--branch` erbij geven is dubbelop.

`--environment` is verplicht vanaf SDK 55. Het kiest welke omgevingsvariabelen in de bundel komen — dus welke API de
app aanspreekt. **Verwissel dit niet:** `production` met `--environment preview` zou de publieke app naar de
dev-omgeving laten praten.

### De valkuil die ons 5 november 2026 een halve dag kostte

Het `env`-blok in `eas.json` geldt **alleen voor builds**. `eas update` kijkt er niet naar; die gebruikt de
omgevingsvariabelen die bij EAS op de server staan (`eas env:list <omgeving>`). En daar ontbrak
`EXPO_PUBLIC_API_URL` in de omgeving `preview`.

Het gevolg was stil en vervelend: `src/api/config.ts` valt zonder die variabele terug op
`__DEV__ ? dev : productie`, en in een uitgerolde bundel is `__DEV__` onwaar. De dev-app op de iPad praatte daardoor
met de **productiedatabase**, zonder dat er ook maar iets aan te zien was. Pas toen de rollen van het ene toestel niet
met het andere overeenkwamen, viel het op.

Controleer daarom vóór een update naar `testflight` of `preview`:

```bash
npx eas-cli env:list preview | grep EXPO_PUBLIC_API_URL
```

Staat die er niet, zet hem dan eenmalig goed (dit hoeft maar één keer):

```bash
npx eas-cli env:set --name EXPO_PUBLIC_API_URL --value "https://app-dvd-api-dev.azurewebsites.net" \
  --environment preview --environment development --visibility plaintext --scope project --type string
```

Kan dat niet, geef de waarde dan mee aan het commando zelf, **met `--clear-cache`**:

```bash
EXPO_PUBLIC_API_URL="https://app-dvd-api-dev.azurewebsites.net" EAS_SKIP_AUTO_FINGERPRINT=1 \
  npx eas-cli update --channel testflight --environment preview --clear-cache --message "..."
```

Die `--clear-cache` is geen overbodige voorzichtigheid: Metro bakt de waarde bij het bundelen in en hergebruikt
anders een eerdere bundel waarin een oude (of geen) waarde zat.

**Nalopen achteraf** kan zonder de app te installeren: exporteer dezelfde bundel lokaal en kijk welke URL erin zit.

```bash
EXPO_PUBLIC_API_URL="https://app-dvd-api-dev.azurewebsites.net" npx expo export --platform ios --clear \
  --output-dir /tmp/bundelcontrole
strings /tmp/bundelcontrole/_expo/static/js/ios/*.hbc | grep -oE 'https://(app-dvd-api-dev[^"]*|www\.vrolijkedrammers\.nl)' | sort -u
```

Je hoort beide adressen te zien: het dev-adres is de ingevulde variabele, het productieadres staat er als
terugvaloptie altijd in. Zie je **alleen** het productieadres, dan is de variabele niet meegekomen.

### De drie vangnetten

Na die ochtend liggen er drie, zodat dit niet nog eens stilletjes kan gebeuren:

1. **De app raadt niet meer.** Ontbreekt `EXPO_PUBLIC_API_URL`, dan bepaalt het **kanaal van de build** de omgeving
   (`src/api/config.ts`). Dat kanaal wordt bij het bouwen in de app gezet en een update kan het niet veranderen, dus
   een testversie kan nooit meer productie kiezen. Vroeger hing die keuze aan `__DEV__`, en dat is in een uitgerolde
   bundel altijd onwaar — precies de fout.
2. **Je ziet het meteen.** Praat een testversie toch met productie, dan staat er een rode balk bovenin:
   *"LET OP · deze testversie praat met PRODUCTIE"*. In de winkelversie wordt daar nooit iets getoond. Rustiger
   terug te vinden is het op het accountscherm: een testversie zet daar bij welke omgeving hij gebruikt.
3. **Het uitrollen controleert zichzelf**, via de `pnpm ota:*`-scripts hierboven.

De app haalt de update op bij het opstarten en gebruikt hem bij de **volgende** keer opstarten. Een tester die nu in de
app zit, ziet het dus niet meteen.

Terugdraaien kan zonder build:

```bash
# alle toestellen terug naar de versie die in de winkel staat (werkt altijd)
npx eas-cli update:roll-back-to-embedded --channel production --message "Terug naar de winkelversie"

# of naar een eerdere OTA-update, als die er is
npx eas-cli update:list --branch production                     # welke updates staan er
npx eas-cli update:republish --branch production --group <id>   # zet een oudere terug
```

`update:republish` werkt alleen als er al een eerdere update op de branch staat. Is de huidige update de eerste, dan is
`update:roll-back-to-embedded` de enige weg terug. Gefaseerd uitrollen kan met `--rollout-percentage <n>` op
`eas update`.

## Een echte build

Zie runbook testflight voor de eenmalige Apple-stappen. Daarna, vanuit `apps/mobile`:

```bash
EAS_SKIP_AUTO_FINGERPRINT=1 npx eas-cli build --platform all --profile production --auto-submit
```

## Valkuilen

- **`EAS_SKIP_AUTO_FINGERPRINT=1` altijd meegeven — ook bij `eas update`.** Zonder die variabele blijft `eas build`
  hangen op "Computing project fingerprint", omdat de repository in een iCloud-map staat. `eas update` berekent die
  fingerprint óók; die kwam op 2026-11-04 wel door, maar met de waarschuwing "taking longer than expected". Zie runbook
  iCloud-duplicaten.
- **`eas update:configure` schrijft te veel.** Toen het eenmalig gedraaid werd, dupliceerde het de hele
  `blockedPermissions`-lijst in `app.json` en zette het er een `permissions`-lijst bij die het uit de config-plugins had
  uitgerekend — met de microfoon erin, die juist geblokkeerd hoort te zijn. Draai het niet opnieuw; is het toch nodig,
  controleer dan de diff van `app.json` regel voor regel en houd alleen `updates` en `runtimeVersion` over.
- **Een update op het verkeerde kanaal.** `--channel` en `--environment` horen bij elkaar (zie de tabel). Controleer het
  voordat je het naar `production` stuurt.
- **Een kanaal bestaat pas na een build met dat profiel.** De tabel hierboven staat in `eas.json`, maar `eas update`
  kan alleen naar een kanaal dat al door een build is aangemaakt. Op 2026-11-04 bestond daarom alleen `production`:
  de laatste dev-build was van 2026-10-29, van vóór deze wijziging, en kon dus ook geen update ophalen. Controleer
  `eas channel:list` en `eas build:list` voordat je belooft dat je eerst op de dev-app kunt testen.
