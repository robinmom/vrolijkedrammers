# Runbook: Dansgardefestival

Het Dansgardefestival is gebouwd op 2026-10-12. Verenigingen schrijven zich in op de website, onder **Doe mee → Dansgardefestival**. De organisatie beoordeelt elke dans in het portaal, onder **Dansgarde → Dansgardefestival**. Jureren en het programma volgen later.

## Inrichten

1. **Account voor de organisatie.** Ga in het portal naar **Gebruikers** en maak een account met de rol **Dansgardefestival**. Met die rol ziet de organisator in het portal alleen het festival.
2. **Instellingen.** Ga naar **Dansgarde → Dansgardefestival → Instellingen** en vul in:
   - naam, datum en locatie;
   - de inschrijfperiode ("vanaf" en "t/m");
   - het aantal plekken (één plek per goedgekeurde dans);
   - de tarieven;
   - het e-mailadres voor meldingen. Voorlopig is dat secretaris@vrolijkedrammers.nl; je kunt het later wijzigen.

   Klik op **Opslaan**.
3. **Documenten.** Upload het reglement en de privacyverklaring als PDF. De website toont ze als link bij stap 3. De tekst van de privacyverklaring staat ook op de pagina zelf.
4. **Pagina aan.** Vink **Pagina op de website** aan.
   - Vóór de startdatum toont de pagina "De inschrijving opent op …".
   - Na de einddatum toont de pagina "De inschrijving is gesloten".
5. **iDEAL** (pas als je wilt laten betalen). Vink **Betalen via iDEAL** aan. Daarvoor is de Mollie-sleutel van de kaartverkoop nodig; die staat al in prod.

## Beoordelen

- **Je beslist per dans.** Vink in het beoordelingspaneel een of meer dansen aan en kies **Goedkeuren**, **Meer info vragen**, **Wachtlijst** of **Afwijzen**. De vereniging krijgt over elke beslissing één e-mail.
- **Plekken.** Goedkeuren vult een plek. Zijn alle plekken bezet, dan weigert het portaal. Je zet dan eerst een dans op de wachtlijst of je verhoogt het aantal plekken.
- **Wachtlijst.**
  - Met ↑ en ↓ verander je de volgorde.
  - Komt er een plek vrij, dan keur je zelf de volgende dans goed. Dat gaat niet automatisch.
- **Meer info vragen.** De vraag gaat per e-mail naar de vereniging, met een link waarmee ze de inschrijving kan aanpassen. Dat kan ook na de sluitingsdatum.
- **Wijzigingen door de vereniging.**
  - Een vereniging kan haar inschrijving via de link in de bevestigingsmail wijzigen zolang de inschrijving open is.
  - Een dans die al was beoordeeld en daarna is gewijzigd, krijgt de status **Gewijzigd – opnieuw beoordelen**. Een goedgekeurde dans houdt dan zijn plek.
  - De vorige versie blijft bewaard: klik op "versie 1" om die te bekijken.
- **Betalen.**
  - Een betaallink kan pas als minstens één dans is goedgekeurd.
  - Het portaal stelt het bedrag voor op basis van de tarieven. Je kunt het aanpassen en dan **Betaallink versturen**.
  - De status komt vanzelf binnen via Mollie. **Status vernieuwen** haalt die direct op. Een verkeerd bedrag trek je in met **Intrekken**.
- **Exporteren.** Hiermee download je alle dansen als CSV-bestand voor Excel.

## Tarieven

| Wat | Hoe berekend |
|---|---|
| Solo of duo | Het solo/duo-tarief per dans |
| Groep met één dans | Het groepstarief voor 1 dans |
| Groep met een gardedans én een showdans | Het tarief voor 2 dansen. Het systeem herkent dit aan dezelfde groepsnaam (hoofdletters tellen niet mee) |
