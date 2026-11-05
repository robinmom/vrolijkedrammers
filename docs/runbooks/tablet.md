# Runbook: de app op een tablet (iPad, Android-tablet, vouwbare telefoon)

Tot versie 1.1.0 was de app een telefoon-app: `ios.supportsTablet` stond uit, de stand stond vast op portret en geen
enkel scherm had een maximale leesbreedte. Op een Android-tablet rekte alles mee tot de volle breedte; op een iPad
draaide de app in iPhone-modus. Vanaf **1.2.0** niet meer.

Het ontwerp staat in Figma op de pagina **📐 Tablet & vouwbaar** van bestand `8EzBpDFQ28pJTf5Ciq6XHh`, met het
toelichtingskader (`545:709`) als besluitenlijst.

## Het uitgangspunt: reageren op breedte, niet op "is dit een tablet"

Eén vouwbare telefoon kan opengevouwen breed zijn én in Split View naast een andere app in een smalle strook staan. Een
tablet is in portret smal en in landschap breed. Daarom kijkt de **layout** naar de beschikbare breedte en niet naar het
toestel:

```ts
import { useIsWide, WIDE_BREAKPOINT, MAX_READING_WIDTH } from '../ui';
```

- `WIDE_BREAKPOINT` (700) — hierboven gebruiken we de tabletindeling.
- `useIsWide()` — true zodra er genoeg breedte is.
- `MAX_READING_WIDTH` (700) — de leeskolom in `Screen`.

Dezelfde ontwerpen gelden dus voor iPad, Android-tablet en de opengevouwen iPhone Duo.

## De schermstand is de uitzondering: die hangt wél aan het toestel

Draaien verandert de breedte, dus de stand op de breedte baseren zou rondjes draaien. Daarom kijkt de stand naar de
**kortste zijde van het scherm** (600 dp, dezelfde grens als Androids `sw600dp`). Het besluit: **een telefoon blijft
portret, een tablet mag draaien.**

De twee platforms doen dat op een andere manier, en dat is niet uit luiheid:

| | Hoe |
|---|---|
| **iOS** | Volledig in `app.json`: `UISupportedInterfaceOrientations` (iPhone, alleen portret) en `UISupportedInterfaceOrientations~ipad` (alle vier). Geen code, en het klopt vanaf het eerste frame. |
| **Android** | In code, via `useOrientationLock()` in de root-layout. `android:screenOrientation` staat op de MainActivity en geldt voor élk toestel, dus per toestel kan daar niet. |

Twee dingen om te weten als je hieraan werkt:

- **`expo.orientation` in `app.json` staat op `default` en moet dat blijven.** Zet je het terug op `portrait`, dan
  staat een iPad ook weer vast. De iOS-waarden in `infoPlist` winnen van Expo's eigen orientation-plugin: die slaat
  zichzelf over zodra je de sleutel zelf zet ("If the user explicitly sets a value in the infoPlist, we should respect
  that").
- **Op iOS wordt niet gelockt, en dat is opzettelijk.** Volgens de Expo-documentatie moet Split View uit staan
  (`ios.requireFullScreen`) voordat locken op een iPad werkt — en Split View willen we juist houden. Voeg dus geen
  `expo-screen-orientation`-aanroep toe voor iOS.
- Voor breed gebruiken we `unlockAsync()` en niet `lockAsync(ALL)`: `ALL` is volgens dezelfde documentatie ongeldig op
  toestellen die geen omgekeerd portret ondersteunen.

## De leeskolom

`Screen` zet zijn inhoud in een gecentreerde kolom van maximaal 700 breed. Dat raakt **48 van de 58 schermen** in één
wijziging — nieuws, agenda, programma, uitslagen en alles wat je leest. Op een telefoon is het scherm smaller dan 700,
dus daar verandert er niets; `maxWidth` doet dan niets.

De tien schermen die `Screen` niet gebruiken hebben hun eigen scrollvlak (de jureerschermen en de camera-schermen van
kassa en deurcontrole). Die krijgen per scherm een eigen indeling.

## Wat een build kost en wat niet

Alleen `supportsTablet` en de schermstand zijn native, plus `expo-screen-orientation` als nieuwe module met native code.
Die drie vragen **één build** (versie 1.2.0). Álle layout hierna is JavaScript en gaat met een OTA-update, zonder build
en zonder beoordeling door Apple of Google — zie runbook app-uitrollen.

Daarom hoort de leeskolom in diezelfde eerste build en niet in een OTA erna: **Apple test op een iPad**, en een app die
daar stuk oogt wordt afgewezen. Zie runbook app-store-review.

## De twee kolommen bij jureren en de kassa

De drie schermen met een eigen scrollvlak hebben sinds 1.2.0 een tabletindeling. Ontwerpen: Figma `551:981` (optocht),
`551:863` (festival), `552:858` (kassa).

**Jureren** (optocht en festival) houdt de pager die er al was. De pagina's worden alleen smaller — `pageWidth` in plaats
van de schermbreedte — en links komt `JuryColumn` ernaast te staan. Swipen blijft dus gewoon werken, de lijst is een
snelkoppeling erbij; beide sturen dezelfde index aan. Daardoor hoefde er niets aan de doorswipe-logica te veranderen,
en dat was bij de optocht het belangrijkst: die pagina's lopen pass-major door alle drie de voorbijtrekkens heen.

De kolom is 30% van de breedte, tussen 280 en 360 punten. Op een iPad in portret (834 punten) blijft er zo ruim 450
over voor de sliders, op een iPad in landschap ruim 800.

**Kassa** toont op een tablet de camera links in een eigen vlak en Recent vast ernaast; de knop "Recent" verdwijnt dan,
want er valt niets meer open te klappen. De regels zelf komen uit `RecentRows`, gedeeld met het volledige Recent-scherm
van de telefoon.

## De testomgeving is een telefoon

`jest-expo` geeft 750×1334 door aan `useWindowDimensions()`. Dat zijn de **pixels** van een iPhone 8, terwijl de hook op
een toestel **punten** teruggeeft (375×667 voor datzelfde toestel). Zonder correctie ligt elke test dus boven
`WIDE_BREAKPOINT` en draait ongemerkt in de tabletindeling. `jest.setup.ts` zet de afmetingen daarom op 390×844; een
test die juist de brede indeling wil, gebruikt `setWindowSize(TABLET_SIZE)` uit `src/test/render.tsx` en zet hem daarna
terug.

Dit kwam aan het licht doordat de kassatest omviel: die zocht de knop "Recent", en die hoort op een tablet niet te
bestaan. Het fotoraster rekende om dezelfde reden met tegels van 248 in plaats van 128 punten.

## Wat nog niet gebouwd is

- **Grotere aanraakdoelen op de schuiven**: op een tablet zit de jury verder van het scherm.

## Testen

Er is geen tablet in huis, dus dit moet op een echt toestel blijken. Let bij de eerste build op:

1. **iPad**: draait de app mee als je het toestel draait, en blijft de tekst in een kolom staan in plaats van over de
   volle breedte te rekken? Werkt Split View nog?
2. **iPhone**: staat die nog altijd vast in portret?
3. **Android-telefoon**: idem — dit is de kant die in code zit, dus hier kan het misgaan.
4. **Android-tablet**: draait die mee?
5. **Scannen met een tablet** (kassa en deurcontrole): de camera van een goedkope Android-tablet is het zwakke punt.
   Dat is bewust nog niet in een ontwerp opgelost omdat het op het toestel zelf moet blijken.
