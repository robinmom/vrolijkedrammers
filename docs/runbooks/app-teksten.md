# Runbook – App-teksten aanpassen zonder nieuwe app

> Status: v1.0 · 2026-11-02 · Hoort bij [05-api-design](../05-api-design.md) en [17-design-system](../17-design-system.md).

De teksten die in de app te zien zijn, zijn aan te passen in **Beheerportal → Beheer → App-teksten** (recht `config.manage`).
Een aanpassing staat binnen een halve minuut op de telefoons: er is geen nieuwe app in de App Store of Play Store nodig.

## 1. Voor het bestuur

| Wat | Hoe |
|---|---|
| Een tekst zoeken | Zoekveld bovenaan; het zoekt in de tekst zoals die in de app staat en in je eigen aanpassing. Zonder zoekopdracht staan de teksten per scherm gegroepeerd. |
| Een tekst aanpassen | Scherm openen, de tekst in het veld zetten en **Opslaan**. De telefoon haalt de teksten elke dertig seconden op, dus kijken kan meteen. |
| Terugzetten | **Terugzetten** zet de tekst weer op die uit de app zelf. |
| Wat je ziet | Elke aanpassing komt in het auditlog (`config.app-text.changed` en `config.app-text.reset`). |

Wat **niet** lukt zonder nieuwe app:

- Een tekst die de app ter plekke samenstelt, zoals "3 dansen" of "STAP 2 van 5". Het losse stuk ("STAP ") is wel aan te passen, het getal niet.
- Een tekst die nergens in de app staat: er is niets toe te voegen, alleen te vervangen.
- Teksten die van de server komen (nieuws, agenda, de pagina's van de website, de tekst van de prins). Die staan al elders in het portal.
- Teksten voor de schermlezer (`accessibilityLabel`) en de teksten in de knoppen van iOS/Android zelf.

Let op: de vervanging gaat op de tekst zelf, niet per scherm. Verander je "Opslaan", dan verandert hij op **elk** scherm waar
die knop staat. De lijst noemt bij elke tekst waar hij voorkomt.

## 2. Hoe het werkt

1. `apps/mobile/scripts/collect-texts.mjs` leest de app-bron en schrijft de catalogus
   `src/Drammers.Api/Content/app-texts.catalog.json`: welke zichtbare teksten er zijn en in welke bestanden.
   CI faalt als de catalogus niet bij de bron past, dus een nieuwe tekst in de app komt automatisch in het portal.
   Handmatig bijwerken: `pnpm --filter @drammers/mobile texts`.
2. De catalogus gaat als embedded resource mee in de API-build en vult **Beheer → App-teksten**
   (`GET/PUT/DELETE /api/v1/admin/config/app-texts`). Alleen de aanpassingen staan in de database
   (`config.AppTextOverride`), met de oorspronkelijke tekst erbij.
3. `GET /api/v1/app-texts` geeft alleen de aanpassingen: `{ "Lid worden": "Drammer worden" }`. De app haalt dat op
   als gewone query, dus hij staat ook in de offline cache.
4. `apps/mobile/src/content/texts.tsx` zet de tekst in de plaats. Dat gebeurt in `AppText` (waar alle zichtbare tekst
   langs komt), in de tekst in een invoerveld, in de tabbalk en in `lib/alert.ts` voor een melding van het toestel.

Daardoor kent geen enkel scherm een sleutel en blijft de tekst in de bron de tekst die de app zonder netwerk toont.

## 3. Bij een nieuwe app-release

De catalogus hoort bij de app-versie waarmee hij is uitgerold. Staat er een oudere app in de store dan de API, dan kan
het portal een tekst laten zien die op dat toestel nog niet bestaat; de aanpassing doet dan gewoon niets tot de nieuwe
app binnen is. Rol API en app dus in deze volgorde uit: eerst de API (prod-deploy), daarna de app-build.
