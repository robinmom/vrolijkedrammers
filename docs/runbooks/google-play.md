# Runbook — Google Play

Organisatieaccount (geen verplichte gesloten testperiode). Pakketnaam `nl.vrolijkedrammers.app`. Doelgroep 13+.
Assets: Figma-pagina **🤖 Play Store** (app-icoon 512 × 512, feature graphic 1024 × 500, zes telefoon-screenshots
1080 × 1920). Exporteren als PNG op 1x.

## 1. App maken en instellen

| Onderdeel | Invullen |
|---|---|
| App-naam | De Vrolijke Drammers |
| Standaardtaal | Nederlands – nl-NL |
| App of game / gratis of betaald | App, gratis |
| Privacybeleid | `https://www.vrolijkedrammers.nl/privacy` |
| App-toegang | Bepaalde functionaliteit is beperkt. Instructies: "Log in met e-mailadres en wachtwoord." Gebruikersnaam `app@vrolijkedrammers.nl`, wachtwoord: het reviewwachtwoord (zelfde als bij Apple). |
| Advertenties | Nee |
| Doelgroep | 13–15, 16–17 en 18+ (niet jonger dan 13). Niet aantrekkelijk voor kinderen. |
| Nieuwsapp | Nee |
| Overheidsapp / financiële functies / gezondheid | Nee |
| Categorie | Evenementen |
| Contactgegevens | secretaris@vrolijkedrammers.nl, website https://www.vrolijkedrammers.nl |

**Inhoudsclassificatie** (IARC-vragenlijst, categorie *Alle andere app-types*): geen geweld, seksualiteit, grof
taalgebruik, drugs of gokken. Gebruikers communiceren niet met elkaar in de app. De app deelt geen locatie. Er kunnen
digitale producten worden gekocht (kaarten en munten). Verwachte uitkomst: PEGI 3 / Iedereen.

## 2. Store-vermelding

**Korte beschrijving** (78 tekens):

> Programma, nieuws, optocht, je ledenticket en munten van De Vrolijke Drammers.

**Volledige beschrijving:**

> De officiële app van carnavalsvereniging De Vrolijke Drammers uit Loil. Alles over carnaval in Loil in één app.
>
> **Programma en nieuws**
> Bekijk alle activiteiten van het seizoen, van de Elfde van de Elfde tot de optocht, en zet ze met één tik in je
> agenda. Lees het laatste nieuws en blader door de foto's.
>
> **De optocht**
> Volg de optocht van Loil: tijden, route en deelnemers. Groepen schrijven zich via de app in voor de optocht.
>
> **Voor leden**
> - Je ledenticket altijd bij de hand: een persoonlijke QR-code voor de ingang tijdens carnaval.
> - Kaarten voor de pronkzitting bestellen, ook voor je groep.
> - Consumptiemunten vooraf kopen en snel ophalen bij de kassa.
> - Meldingen over activiteiten en nieuws van de vereniging.
>
> Nog geen lid? Meld je aan via de app. Leden loggen in met hun e-mailadres.
>
> Alaaf!

## 3. Gegevensveiligheid

Algemeen: gegevens worden **versleuteld verzonden** (ja). Gebruikers kunnen **vragen om verwijdering** (ja): in de app
via Account → Account verwijderen, of via `https://www.vrolijkedrammers.nl/contact`. Geen gegevens worden **gedeeld**
met derden (verwerkers zoals de betaaldienst handelen namens de vereniging en tellen volgens Google niet als delen).
De app verzamelt zonder account geen persoonsgegevens.

| Gegevenstype | Verzameld | Verplicht | Doel |
|---|---|---|---|
| Naam | Ja | Ja (met account) | App-functionaliteit, accountbeheer |
| E-mailadres | Ja | Ja (met account) | App-functionaliteit, accountbeheer, communicatie |
| Telefoonnummer | Ja | Nee | App-functionaliteit (lid worden, optocht) |
| Adres | Ja | Nee | App-functionaliteit (lid worden, optocht) |
| Overige info (geboortedatum) | Ja | Nee | App-functionaliteit (lid worden) |
| Overige financiële info (IBAN voor automatische incasso) | Ja | Nee | App-functionaliteit (lid worden) |
| Aankoopgeschiedenis (kaarten en munten) | Ja | Nee | App-functionaliteit |
| Bestanden en documenten (optocht-inschrijving) | Ja | Nee | App-functionaliteit |
| Apparaat- of andere ID's (toestel-ID, pushtoken) | Ja | Ja (met account) | App-functionaliteit, fraudepreventie (QR gekoppeld aan toestel) |

Niet verzameld: locatie, contacten, foto's/video's (de camera scant alleen QR-codes en slaat niets op), audio, agenda
(de app schrijft alleen een afspraak als je daarom vraagt), app-activiteit, web browsen, crashlogs en diagnostiek in de
app, gezondheid. Betaalgegevens (bankrekening bij iDEAL) worden door de betaaldienst verwerkt, niet door de app.

## 4. Eerste upload en daarna

1. Na de livegang (Productie op www.vrolijkedrammers.nl) maakt Claude de build:
   `npx eas-cli build --platform android --profile production` (Android App Bundle).
2. Jij: **Testen → Interne tests → Nieuwe release**, de `.aab` uploaden, testers toevoegen (e-mailadressen) en
   uitrollen. Google beheert de app-ondertekening (*Play App Signing*, standaard aan).
3. Automatisch insturen van volgende versies: in Google Cloud een serviceaccount met JSON-sleutel maken, in Play
   Console → **Gebruikers en rechten** uitnodigen met rechten voor releases, en de sleutel in EAS zetten
   (expo.dev → Project → Credentials → Android → Google Service Account Key). De sleutel is geheim en gaat niet via
   Claude.
4. Productie: na de interne test **Productie → Nieuwe release** (of de release uit Interne tests promoveren) en
   ter beoordeling indienen.
