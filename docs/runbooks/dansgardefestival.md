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

## Takenlijst

Het festival heeft een eigen takenlijst, onder **Dansgarde → Dansgardefestival → Takenlijst**. De organisatie (rol
Dansgardefestival) ziet alleen deze lijst; de takenlijst van carnaval blijft voor het bestuur.

- **Draaiboek importeren:** kies het Excel-draaiboek.
  - Het tabblad **Actiepunten** wordt ingelezen.
  - Kopjes worden categorieën, zonder jaartal (bijv. "Op de dag zelf").
  - "Wie" komt bij **Overige personen** en de opmerkingen in de omschrijving.
  - Een plandatum in het verleden wordt overgeslagen.
  - Taken die er al zijn, worden overgeslagen.
- Uitnodigingen aan verenigingen gaan via een mailing. De deelnemers zijn de inschrijvingen via de website.
- **Kopiëren naar volgend jaar** werkt zoals bij carnaval.

## Programma

Onder **Programma**:

- **Categorieën:** een programmacategorie is leeftijdscategorie + soort dans (bijv. "Junioren Duo Karakter"). Ze komen
  vanzelf uit de goedgekeurde dansen.
- **Volgorde:** de standaardvolgorde is die van 2026. Eerst de gardedansen (groepen, solo, duo), daarna de showdansen.
- **Tijden:**
  - Vul de **Start** (opening) in.
  - Per categorie de **minuten per dans**.
  - Een starttijd per categorie sluit vanzelf aan op het vorige onderdeel. Vul een tijd in voor een gat of uitloop;
    **Aansluiten** zet hem terug.
- **Blokken:** voeg een **Pauze** (of ander blok) toe met **+ Blok**. Opening en Prijsuitreiking staan er standaard in.
- **Schuiven:** categorieën en dansen schuiven met ↑↓.
- **Loten:** **Loten** maakt de volgorde binnen elke categorie willekeurig. Daarna kun je nog schuiven.
- **Afdrukken:** **Afdrukken (A4)** geeft een PDF in de huisstijl. Het programma komt niet op de website.

## Jury

1. Ga naar **Jury → + Jurylid uitnodigen**: naam, e-mailadres en eventueel **Hoofdjury**. Het jurylid krijgt een
   e-mail en logt in de app in met dat adres (code per e-mail).
2. Elk jurylid krijgt een vast nummer (Jurylid 1, 2, 3 …). In het rapport staat alleen dat nummer, geen naam.
3. In de app staat onder **Meer** de knop **Jureren dansgardefestival**.
   - Het jurylid ziet alleen de categorie die aan de beurt is: de eerste die het nog niet heeft ingediend.
   - Swipen tussen de dansen van die categorie.
   - Per dans vier onderdelen van 0 tot 10 (bij solo heet het eerste Ritme), met het totaal en een opmerking voor de
     vereniging.
   - Punten staan direct op de telefoon, ook zonder internet.
   - **Categorie indienen** kan pas als alle dansen punten hebben. Daarna komt de volgende categorie.

## Uitslag en prijsuitreiking (op de dag zelf)

1. **Uitslag:**
   - Zolang nog niet alles binnen is, vraagt het portal eerst of je de uitslag toch wilt bekijken.
   - Per categorie zie je hoeveel juryleden hebben ingediend en de ranglijst.
   - Het totaal is de som van alle onderdelen van alle juryleden. Bij een gelijk totaal wint de hoogste Uitvoering;
     is die ook gelijk, dan is het een gedeelde plaats.
2. **Exporteren (PDF):** de uitslag per categorie in huisstijl, per deel van de prijsuitreiking een pagina.
3. **Publiceer naar hoofdjury:**
   - Is nog niet alles binnen, dan vraagt het portal of je toch wilt publiceren.
   - De hoofdjury ziet in de app per deel (1 minioren, 2 junioren, 3 jeugd en hoofdklasse) per categorie de uitslag,
     van de laatste plaats naar de winnaar.
4. **Prijsuitreiking afronden:**
   - Na de laatste categorie kiest de hoofdjury **Prijsuitreiking afronden** → Ja.
   - In het portal kun je dit overrulen of terugdraaien.

## Website en rapporten (na de prijsuitreiking)

- **Publiceer op website:** de uitslag (plaatsen en punten per categorie) komt als openbaar nieuwsbericht op de website
  en in de app. Opnieuw klikken werkt hetzelfde bericht bij.
- **Rapporten versturen:**
  - Elke vereniging met goedgekeurde dansen krijgt een e-mail met haar juryrapport (PDF).
  - Het rapport bevat per eigen dans de plaats, de punten per jurylid en de opmerkingen (alleen van de eigen dansen),
    en de volledige uitslag met de eigen dansen gemarkeerd.
  - Wie er al een heeft gekregen, krijgt geen tweede.
- **Rapport (PDF)** per vereniging: een voorbeeld vooraf.

### E-mail van dansgardefestival@

De rapporten komen van **dansgardefestival@vrolijkedrammers.nl** (een gedeelde mailbox in Microsoft 365).

1. Deploy (prod): Bicep zet `dansgardefestival` als afzender in Azure Communication Services.
2. Wil je de rapporten via de mailbox versturen (beter tegen spam; staan bij Verzonden items):
   - draai `pwsh infra/m365/mailbox-verzenden.ps1` opnieuw (de mailbox staat nu in de standaardlijst);
   - zet `DVD_EMAIL_MAILBOX_SENDERS` op `voorzitter,secretaris,dansgardefestival`;
   - deploy opnieuw.

   Zie runbook mailing-via-mailbox.

## Open punten (te bespreken met de organisatie)

- **Senioren of Hoofdklasse:** het inschrijfformulier zegt Senioren (16+), de overzichten Hoofdklasse. Nu staan beide
  (de categorie heet in het programma "Senioren …", deel 3 heet "Jeugd en hoofdklasse").
- **Ritme bij solo:** de app gebruikt bij solo Ritme in plaats van Synchroniteit, zoals de uitslag-Excel van 2026.
  Klopt dat?
- **Gelijke stand:** nu wint de hoogste Uitvoering, anders een gedeelde plaats.
- **Inhoud van het rapport:** de verenigingen zien de punten per jurylid. Is dat gewenst, of alleen plaatsen en
  opmerkingen?
- **Halve punten:** nu alleen hele punten van 0 tot 10.
