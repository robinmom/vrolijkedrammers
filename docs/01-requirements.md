# 01 – Requirements

> Status: v0.2 · 2026-09-24 · Architectuurfase afgerond. Releases R1–R5 zijn vertaald naar implementatiefasen in [15-implementation-plan.md](15-implementation-plan.md)
> Bron: opdrachtomschrijving "De Vrolijke Drammers App" (§1–§84) + Figma-ontwerp `8EzBpDFQ28pJTf5Ciq6XHh`.
> Paragraafverwijzingen (§nn) verwijzen naar de opdrachtomschrijving.

## 1. Doel en context

Carnavalsvereniging **De Vrolijke Drammers** (Loil, sinds 1958) wil één digitaal platform voor publiek en leden:

- een mobiele app (iOS + Android) met gelijke look & feel;
- een webgebaseerde beheeromgeving;
- een backend/API in Microsoft Azure;
- koppelingen met e-Boekhouden (ledenbron), Mollie (betalingen) en de website.

Schaal: een vereniging met naar schatting enkele honderden leden en enkele duizenden bezoekers tijdens carnaval (aanname A-01). Het ontwerp is **eenvoudig, betaalbaar en onderhoudbaar door een kleine (vrijwillige) groep**, zonder enterprise-complexiteit.

Belangrijke data:

| Moment | Datum | Relevantie |
|---|---|---|
| Elfde van de Elfde | wo 11-11-2026 | Start seizoen, eerste zichtbare release gewenst |
| Pronkzitting | za 16-01-2027 (Figma) | Ticketverkoop/reservering |
| Carnaval 2027 | za 6 t/m di 9-02-2027 | QR-toegang, scanners, bandjes |
| Optocht Loil | zo 7-02-2027 13:30 (Figma) | Optochtinschrijving, aanrijtijden, startnummers |

## 2. Samenvatting requirements

| Domein | Kern |
|---|---|
| Publiek (gast) | Agenda, programma, nieuws, foto's (alleen publiek gemarkeerde content) |
| Leden | Login, eigen gegevens, lidmaatschapsstatus, persoonlijke QR-toegangscode, push |
| Rollen | Carnavalist, Groepsverantwoordelijke, Kaderlid, Dansgarde (leiding/meisje/ouder), Raad van Elf, Bestuur; meerdere rollen per persoon; RBAC met permissions |
| Ledenbron | e-Boekhouden is leidend; lokale kopie; lidnummer = externe sleutel; idempotente sync |
| Lid worden | Aanvraag via app/website, altijd handmatige goedkeuring |
| Toegang | Veilige QR per lid per carnavalsjaar; scanmodus met groen/oranje/rood; volledige scanlog; offline scannen; bandjes |
| Ticketing | Generiek: dagkaarten (Mollie), pronkzitting, later andere evenementen |
| Optocht | Wizard-inschrijving, opgavenummer (volgorde), startnummer (commissie), categorieën, validaties, documenten, samenstellen, aanrijtijden-import, exports |
| Communicatie | Push (gericht op rollen/groepen/leden/ouders), nieuws, later nieuwsbrief |
| Beheer | Responsive beheerportal met ledenbeheer, content, optocht, tickets, rapportages, audit, configuratie |
| Rapportage | Bezoekers, scans, optocht, leden, jubilarissen, trends per carnavalsjaar |
| Kwaliteit | Security (OWASP), AVG, auditlog, observability, backups, CI/CD, IaC, tests, toegankelijkheid |

## 3. Functionele requirements

Notatie: `REQ-<domein>-<nr>`. Prioriteit: **M** = must (MVP of release waarin het domein landt), **S** = should, **C** = could. Release volgens [09-mvp-roadmap.md](09-mvp-roadmap.md).

### 3.1 Gast en publieke content (§2, §6, §44, §45)

| ID | Requirement | Prio | Release |
|---|---|---|---|
| REQ-PUB-01 | Niet-ingelogde gebruiker kan publieke agenda/programma bekijken | M | R1 |
| REQ-PUB-02 | Niet-ingelogde gebruiker kan publiek nieuws bekijken | M | R1 |
| REQ-PUB-03 | Niet-ingelogde gebruiker kan publieke fotoalbums bekijken | M | R1 (basis) |
| REQ-PUB-04 | Gast ziet nooit leden-, rol- of groepsgebonden content (server-side afgedwongen) | M | R1 |
| REQ-PUB-05 | Gast ziet een "Word ook een Drammer!"-CTA (Figma 05 Meer) | M | R1 |

### 3.2 Account, login en profiel (§47, §48, §49, §74)

| ID | Requirement | Prio | Release |
|---|---|---|---|
| REQ-ACC-01 | Alleen leden (en ouders/verzorgers van minderjarige leden) krijgen een account; geen vrije registratie. Bestaande leden vragen een account aan met lidnummer + e-mailadres; nieuwe leden krijgen een account na goedkeuring (B-05, ADR-014) | M | Fase 9 |
| REQ-ACC-02 | Inloggen met e-mail + wachtwoord of e-mail-code (OTP) | M | R1 |
| REQ-ACC-03 | "Wachtwoord opnieuw instellen" via tijdelijke link/code; wachtwoorden nooit zichtbaar of verzendbaar | M | R1 |
| REQ-ACC-04 | Lid ziet eigen gegevens en lidmaatschapsstatus (read-only voor velden die e-Boekhouden beheert) | M | R1 |
| REQ-ACC-05 | Lid kan notificatievoorkeuren instellen (categorieën, herinneringen) | M | R1 |
| REQ-ACC-06 | Lid ziet en beheert eigen apparaten (afmelden/verwijderen) | S | R1 |
| REQ-ACC-07 | Sessies en refresh-tokens kunnen centraal worden ingetrokken (account blokkeren, telefoon verloren) | M | R1 |
| REQ-ACC-08 | Ouder/verzorger kan gegevens en meldingen van gekoppelde kinderen zien | M | R3 |
| REQ-ACC-09 | Lid kan een export van de eigen persoonsgegevens aanvragen (AVG) | S | R3 |

### 3.3 RBAC (§4, §5)

| ID | Requirement | Prio | Release |
|---|---|---|---|
| REQ-RBAC-01 | Gebruiker kan meerdere rollen hebben; rol bevat meerdere permissions | M | R1 |
| REQ-RBAC-02 | Autorisatie op permissions, niet op rolnamen of schermen | M | R1 |
| REQ-RBAC-03 | Rollen en rol-permissie-koppelingen zijn beheerbaar in het portal (`role.manage`) | M | R1 |
| REQ-RBAC-04 | Scoping: een groepsverantwoordelijke ziet alleen eigen inschrijvingen; een ouder alleen eigen kinderen | M | R2/R3 |
| REQ-RBAC-05 | Rolwijzigingen zijn direct effectief (maximaal 5 minuten caching) en worden geaudit | M | R1 |

### 3.4 Programma en agenda (§6)

| ID | Requirement | Prio | Release |
|---|---|---|---|
| REQ-EVT-01 | Agenda-item met titel, omschrijving, start/eind (datum+tijd), locatie, categorie, doelgroep, afbeelding, zichtbaarheid, publicatiestatus, bijlagen | M | R1 |
| REQ-EVT-02 | Zichtbaarheid: iedereen / leden / rollen / groepen / individuele leden | M | R1 |
| REQ-EVT-03 | Filteren op categorie (Figma: Alle, Carnaval, Jeugd, Vereniging) | M | R1 |
| REQ-EVT-04 | "Toevoegen aan agenda" (kalenderbestand of deeplink) (Figma 06) | S | R1 |
| REQ-EVT-05 | Markering "Hoogtepunt" en badges zoals "Bijna uitverkocht" (Figma 02/06) | S | R1 / R4 |
| REQ-EVT-06 | Programmawijziging kan een pushmelding naar de betrokken doelgroep sturen | M | R1 |
| REQ-EVT-07 | Events zijn gekoppeld aan een CarnivalYear | M | R1 |

### 3.5 Nieuws, push en nieuwsbrief (§7, §45, §46)

| ID | Requirement | Prio | Release |
|---|---|---|---|
| REQ-NWS-01 | Nieuws: titel, samenvatting, inhoud, afbeelding, auteur, publicatie-/einddatum, doelgroep, push ja/nee, status (Draft/Scheduled/Published/Archived) | M | R1 |
| REQ-NWS-02 | Geplande publicatie (Scheduled → Published op tijdstip) | M | R1 |
| REQ-NOT-01 | Push naar iedereen / alle leden / rol(len) / specifieke leden / groepen / ouders van geselecteerde kinderen | M | Fase 10 (iedereen/leden/rollen/groepen/individuele leden), fase 17 (ouders) — zie [15](15-implementation-plan.md) |
| REQ-NOT-02 | Opslag van titel, inhoud, doelgroep, verzendtijd, afzender, status, delivery status en read status | M | R1 |
| REQ-NOT-03 | In-app meldingeninbox (ook zonder push-toestemming) | M | R1 |
| REQ-NOT-04 | Automatische meldingen bij optochtgebeurtenissen (open, deadline, ontbrekende documenten, goedgekeurd, aanvulling, startnummer, aanrijtijd, planningswijziging) | M | R2/R3 |
| REQ-NOT-05 | Notificatievoorkeuren per categorie; "dringend" gaat altijd door | M | R1 |
| REQ-NL-01 | Nieuwsbrief als apart concept (in-app + e-mail), datamodel voorbereid | C | R5 |

### 3.6 Foto's (§44)

| ID | Requirement | Prio | Release |
|---|---|---|---|
| REQ-PHO-01 | Albums met titel, evenement, datum, omschrijving, zichtbaarheid (publiek/leden/rollen) | M | R1 (basis) |
| REQ-PHO-02 | Foto met origineel, thumbnail, volgorde, metadata; EXIF-locatie wordt verwijderd | M | R1 |
| REQ-PHO-03 | Verwijderverzoek portretrecht: foto snel te verbergen/verwijderen | M | R1 |

### 3.7 Leden en e-Boekhouden (§8–§11, §50, §51)

| ID | Requirement | Prio | Release |
|---|---|---|---|
| REQ-MEM-01 | Lokale kopie van ledengegevens uit e-Boekhouden; `memberNumber` (lidnummer) is de externe unieke sleutel | M | R1 |
| REQ-MEM-02 | Idempotente synchronisatie; bestaande leden nooit dubbel aanmaken | M | R1 |
| REQ-MEM-03 | Sync overschrijft nooit app-specifieke gegevens (rollen, devices, tickets, relaties, voorkeuren, audit) | M | R1 |
| REQ-MEM-04 | SyncJob-registratie: start, eind, status, gelezen/nieuw/gewijzigd/ongewijzigd, fouten, conflicten | M | R1 |
| REQ-MEM-05 | Verdwenen leden worden gedeactiveerd, niet verwijderd | M | R1 |
| REQ-MEM-06 | Beheer: zoeken, bekijken, filters, export, rollen, status, laatste login, syncstatus, scanhistorie, blokkeren | M | R1 (basis), R3 (scanhistorie) |
| REQ-MEM-07 | Ledenlijsten: actief, inactief, per rol, leeftijd, inschrijfjaar, carnavalsjaar, jubilarissen; export Excel/CSV | M | R1 (basis), R5 (jubilarissen) |

### 3.8 Lid worden (§12)

| ID | Requirement | Prio | Release |
|---|---|---|---|
| REQ-APP-01 | Aanmelden via app en website | M | R1 |
| REQ-APP-02 | Workflow Gestart → Ingediend → In afwachting → Handmatige beoordeling → Goedgekeurd/Afgewezen → Geactiveerd; de aanvraag staat in een tijdelijke wachtrij en komt pas na goedkeuring in e-Boekhouden en Entra ID (ADR-014) | M | Fase 9 |
| REQ-APP-03 | Nooit automatisch een definitief lidmaatschap | M | R1 |
| REQ-APP-04 | Opslag: aanvraagdatum, status, behandelaar, goedkeuringsdatum, opmerkingen, afwijzingsreden | M | R1 |
| REQ-APP-05 | Na goedkeuring maakt het systeem het lid automatisch aan in e-Boekhouden (`POST /v1/member`), daarna lokaal en in Entra ID, en stuurt een welkomstmail (ADR-014) | M | Fase 9 |

### 3.9 Toegang, QR en scannen (§13–§17, §64)

| ID | Requirement | Prio | Release |
|---|---|---|---|
| REQ-QR-01 | Ieder geldig lid heeft per carnavalsjaar een persoonlijke digitale toegangscode (QR) | M | R3 |
| REQ-QR-02 | QR bevat geen leesbare persoonsgegevens en nooit (alleen) het lidnummer | M | R3 |
| REQ-QR-03 | QR is bestand tegen screenshots, doorsturen en replay (dynamisch, kortlevend, gesigneerd) | M | R3 |
| REQ-QR-04 | Scanmodus alleen voor gebruikers met `ticket.scan` op een vertrouwd device | M | R3 |
| REQ-QR-05 | Resultaten: GROEN geldig / GROEN herhaald op dit device (met vorige datum/tijd) / ORANJE eerder op ander device / ROOD ongeldig (met reden) | M | R3 |
| REQ-QR-06 | Kleur altijd gecombineerd met icoon, tekst, haptiek en geluid (toegankelijkheid) | M | R3 |
| REQ-QR-07 | Iedere scan is een apart, onveranderbaar record (ticket, lid, event, datum/tijd, scanner, device, resultaat, vorige scan) | M | R3 |
| REQ-QR-08 | Rapportage onderscheidt scans, unieke bezoekers, eerste toegang en herhaalde scans | M | R3 |
| REQ-QR-09 | Offline scannen met lokale queue en reconciliatie na synchronisatie | M | R3 |
| REQ-QR-10 | Bandjes registreren (verstrekt, tijd, door, type, geldig van/tot); configureerbare regels | M | R3 |
| REQ-QR-11 | QR intrekken en opnieuw uitgeven; ticket blokkeren | M | R3 |
| REQ-QR-12 | Alternatieve toegang voor leden zonder smartphone (geprinte kaart) | M | R3 |

### 3.10 Ticketing en betalingen (§18, §19)

| ID | Requirement | Prio | Release |
|---|---|---|---|
| REQ-TIC-01 | Generiek ticketmodel (TicketType, validiteit, prijs, capaciteit) voor lidmaatschapstoegang, dagkaarten en pronkzitting | M | R3 (model), R4 (verkoop) |
| REQ-TIC-02 | Dagkaart kopen door niet-leden (Mollie), ticket + QR + geldigheidsdatum | M | R4 |
| REQ-TIC-03 | Pronkzitting: melding verkoop geopend, bestellen/reserveren, betaling indien nodig, digitaal ticket, status | M | R4 |
| REQ-PAY-01 | Server-side payment creation; nooit vertrouwen op redirect | M | R4 |
| REQ-PAY-02 | Webhook idempotent; status altijd ophalen bij Mollie; ondersteuning voor mislukt, geannuleerd, verlopen en refund | M | R4 |
| REQ-PAY-03 | Beschikbaarheid/capaciteit bewaakt (geen overselling) | M | R4 |

### 3.11 Optocht (§20–§43)

Zie [13-parade-process.md](13-parade-process.md) en [14-parade-data-model.md](14-parade-data-model.md) voor alle details.

| ID | Requirement | Prio | Release |
|---|---|---|---|
| REQ-PAR-01 | Inschrijving als 8-staps wizard: leden via de app (concept opslaan, push); niet-leden via een openbaar webformulier zonder account met e-mailverificatie en e-mailcommunicatie (B-05a) | M | Fase 11 |
| REQ-PAR-02 | Opgavenummer automatisch bij definitief indienen, uniek per optocht, oplopend, nooit hergebruikt, niet wijzigbaar | M | R2 |
| REQ-PAR-03 | Startnummer handmatig door bevoegde, uniek per optocht, wijzigbaar, gelogd | M | R2 |
| REQ-PAR-04 | Configureerbare categorieën (ParadeCategory) met min/max deelnemers, voertuig, leeftijdsgroep | M | R2 |
| REQ-PAR-05 | Validatie deelnemers versus categorie: blokkeren of waarschuwen (configureerbaar) | M | R2 |
| REQ-PAR-06 | Bouwadres + juryadres ("gelijk aan bouwadres" standaard aangevinkt) | M | R2 |
| REQ-PAR-07 | Geschatte lengte (decimaal, m) en later gemeten lengte, beide bewaard | M | R2 |
| REQ-PAR-08 | Statusworkflow met Nederlandse labels; configureerbare wijzigbaarheid per status | M | R2 |
| REQ-PAR-09 | Volledige wijzigingshistorie (wie, wat, oud, nieuw, wanneer) | M | R2 |
| REQ-PAR-10 | Beheeroverzicht met sorteren, filteren, zoeken en exporteren (Excel/CSV) | M | R2 |
| REQ-PAR-11 | Samenstellen optocht: `parade_order` (drag-and-drop), expliciet startnummers genereren | M | R2 |
| REQ-PAR-12 | Lengteberekeningen (totaal geschat/gemeten, per categorie, gemiddeld, incl. `spacing_after_meters`) | M | R2 |
| REQ-PAR-13 | Aanrijtijden-import uit Excel: upload → preview → validatie → bevestigen → import | M | R3 |
| REQ-PAR-14 | Groepsverantwoordelijke ziet datum, aanrijtijd, locatie, opmerkingen, startnummer; push "aanrijtijd bekend" | M | R3 |
| REQ-PAR-15 | Veilige documentupload (private opslag, typen en grootte begrensd, malwarescan) | M | R2 |
| REQ-PAR-16 | Publieke optochtinformatie in app (Figma 04: start, deelnemers, route, tijdlijn) | M | R2 |

### 3.12 Rapportage, statistiek en jubilarissen (§51–§54)

| ID | Requirement | Prio | Release |
|---|---|---|---|
| REQ-REP-01 | Dashboard bezoekers: per dag, uniek, per uur, eerste toegang, dubbele scans (zelfde/ander device), dagkaarten, verhouding leden/dagkaarten, drukste dag/uur, trends | M | R3 (basis), R5 (trends) |
| REQ-REP-02 | Optochtstatistieken: aantallen, per categorie, jeugd/volwassen, deelnemers, lengtes, inschrijvingen per dag/jaar | M | R2 |
| REQ-REP-03 | Jubilarissen 11/22/33/44/55/66/77 jaar, configureerbare regels, rapport met vaste kolommen | M | R5 |
| REQ-REP-04 | CarnivalYear als dimensie voor events, optocht, tickets, toegang, statistieken en jubilarissen | M | R1 |

### 3.13 Beheer, audit en configuratie (§55, §67, §78)

| ID | Requirement | Prio | Release |
|---|---|---|---|
| REQ-ADM-01 | Responsive beheerportal met het menu uit §67 (gefaseerd per release) | M | R1+ |
| REQ-ADM-02 | Auditlog van gevoelige acties; append-only; niet wijzigbaar via UI | M | R1 |
| REQ-ADM-03 | Configuratie: feature flags, maintenance mode, minimum appversie / forced update | M | R1 |
| REQ-ADM-04 | Import/sync-overzicht met fouten en conflicten | M | R1 |

## 4. Niet-functionele requirements

| ID | Categorie | Requirement |
|---|---|---|
| NFR-01 | Platform | iOS (laatste 2 major versies) en Android (API 26+); één cross-platform codebase |
| NFR-02 | Look & feel | Figma-ontwerp leidend; licht + donker thema; platformconventies iOS/Android |
| NFR-03 | Performance | API p95 < 500 ms bij normale belasting; scanvalidatie online p95 < 700 ms end-to-end; offline < 200 ms |
| NFR-04 | Schaal | Ontwerp voor ± 1.000 accounts, ± 5.000 tickets per carnavalsjaar, piek ± 20 scans/min per ingang, ± 10 scanners gelijktijdig (A-01) |
| NFR-05 | Beschikbaarheid | 99,5 % buiten carnaval; tijdens carnaval extra bewaking; scannen werkt offline door |
| NFR-06 | Security | OWASP ASVS L2 / MASVS-L1 (+ relevante L2-controls voor scanner), zie [06-security.md](06-security.md) |
| NFR-07 | Privacy | AVG by design; dataminimalisatie; bewaartermijnen; inzage/export/verwijderen |
| NFR-08 | Toegankelijkheid | WCAG 2.2 AA-principes; VoiceOver/TalkBack; Dynamic Type; touch targets ≥ 44 pt / 48 dp; nooit alleen kleur |
| NFR-09 | Taal | Nederlands (UI); techniek in het Engels; later eventueel Limburgs/Engels niet uitgesloten |
| NFR-10 | Onderhoud | Modulaire monoliet, CI/CD, IaC, geautomatiseerde tests, heldere documentatie en ADR's |
| NFR-11 | Kosten | Productie indicatief < € 100 per maand exclusief app store-kosten (zie [08](08-azure-infrastructure.md)) |
| NFR-12 | Observability | Application Insights, alerts op kritieke fouten, geen PII/secrets in logs |
| NFR-13 | Herstel | RPO ≤ 1 uur (≤ 10 min tijdens carnaval via PITR), RTO ≤ 8 uur (≤ 2 uur tijdens carnaval) — gelijk aan [08 §8](08-azure-infrastructure.md#8-backup-en-disaster-recovery-70) |
| NFR-14 | Omgevingen | Development, Acceptance, Production technisch gescheiden |

## 5. Aannames

| ID | Aanname | Impact als onjuist |
|---|---|---|
| A-01 | Omvang: 300–800 leden, 2–5k bezoekers per carnaval, 30–80 optochtdeelnemers | Hogere tiers nodig; architectuur blijft gelijk |
| A-02 | De vereniging gebruikt de e-Boekhouden **ledenmodule** (`/v1/member` is beschikbaar voor verenigingen) | Anders fallback naar `/v1/relation` of CSV-import |
| A-03 | Geboortedatum, inschrijfjaar, lidstatus en categorie worden in e-Boekhouden vastgelegd in **vrije velden** (`freeText1..10`) volgens een afgesproken mapping; e-Boekhouden kent hier geen standaardvelden voor | Zonder mapping: velden lokaal beheren (zie OQ-01) |
| A-04 | E-mailadres is voor de meeste leden bekend in e-Boekhouden; zonder (juist) e-mailadres laat het bestuur dit eerst in e-Boekhouden vastleggen, daarna volgt het account (ADR-014) | Extra beheerproces |
| A-05 | Toegang tijdens carnaval gaat per persoon per carnavalsjaar (één "lidmaatschapstoegangsticket" per lid) | Ander ticketmodel |
| A-06 | Er is een vaste kleine groep scanners (Raad van Elf, bestuur, vrijwilligers) met hun eigen telefoon | Eventueel verenigingstelefoons |
| A-07 | Eén optocht per carnavalsjaar | Het datamodel ondersteunt er meer via `Parade` |
| A-08 | De website wordt later (her)bouwd en gebruikt dezelfde API; tot die tijd kan een eenvoudige web-inschrijfpagina het beheerportal/SPA-framework delen | Extra frontend |
| A-09 | Er zijn één of twee technische vrijwilligers of een externe partij voor onderhoud; kennis van C# en TypeScript is aanwezig of verwerfbaar | Stackkeuze heroverwegen |
| A-10 | Het Figma-ontwerp is leidend voor de app-UI; voor ontbrekende schermen volgen we dezelfde designtaal | Extra designronde |

## 6. Ontbrekende of onduidelijke requirements

Uitgewerkt als besluitpunten in [10-open-questions.md](10-open-questions.md). Samengevat:

1. **Velden in e-Boekhouden**: de API levert geen geboortedatum, inschrijfdatum, lidstatus of gesplitste naam (alleen `name`). Waar staan deze gegevens nu? (OQ-01, OQ-02)
2. **Interpretatie deelnemersaantal** bij categorieën (kinderen tellen mee in volwassenencategorie?) (OQ-10)
3. **Wie is "groepsverantwoordelijke"?** Moet deze persoon lid zijn, of kan een externe groep zich inschrijven? (OQ-11)
4. **Toegangsregels carnaval**: welke evenementen/dagen zijn "carnavalstoegang"; mogen leden gasten meenemen; prijs dagkaart; bandjeskleuren per dag? (OQ-20, OQ-21)
5. **Jubileumregels**: telt het inschrijfjaar als jaar 1; tellen onderbrekingen? (OQ-30)
6. **Minderjarigen**: vanaf welke leeftijd een eigen account (voorstel: 16 jaar, conform AVG/UAVG) (OQ-15)
7. **Uitslagen** (Figma: Uitslagen-tegel): wat zijn uitslagen (optochtprijzen?) en wie voert ze in? Niet in de opdracht opgenomen (OQ-40)
8. **Route/kaart optocht** (Figma: Route-knop, "3,2 km"): statische kaart of interactieve route? (OQ-41)
9. **Ontbrekende Figma-schermen**: login, activatie, Mijn QR, scanner, optochtwizard, lid worden, meldingeninbox, profiel, beheerportal (OQ-42)
10. **Nieuwsbrief e-mail**: welke dienst (bestaande mailinglijst?) (OQ-43)
11. **Juridisch**: privacyverklaring, verwerkersovereenkomsten (Microsoft, Mollie, e-Boekhouden, Expo), foto-/portretrechtbeleid (OQ-50)

## 7. Bijzondere situaties (§78)

| Situatie | Oplossing (samengevat) | Uitwerking |
|---|---|---|
| Telefoon verloren | Lid of bestuur trekt device in → device-sleutel ongeldig, refresh-tokens ingetrokken, QR opnieuw te binden aan nieuw device | 06, ADR-005 |
| E-mailadres gewijzigd | Wijziging in e-Boekhouden → sync; login-e-mail in Entra wordt pas gewijzigd na verificatie via de app (conflict gemarkeerd) | 04, ADR-010 |
| Lid overleden | Bestuur zet status "Overleden": account geblokkeerd, geen communicatie, gegevens bewaard volgens bewaartermijn, uitgesloten van jubilarissen/ledenlijsten | 02 |
| Lidmaatschap beëindigd | Status inactief → geen ledencontent, ticket ongeldig, account in "beperkt" (alleen publieke info) | 02 |
| Tijdelijk lid | `membership_valid_from/to` lokaal; ticket volgt deze geldigheid | 04 |
| Lid zonder smartphone | Geprinte toegangskaart met statische, intrekbare code + bandje | ADR-005 |
| Meerdere leden in gezin | Elk lid een eigen account; gezinsrelatie optioneel voor weergave; gedeeld e-mailadres toegestaan voor minderjarigen via guardian | 04 |
| Ouder met meerdere kinderen | GuardianMemberRelation N:M | 04 |
| Meerdere rollen / rol verandert | UserRole N:M met geldigheid; permissions cache ≤ 5 min; audit | 07 |
| QR intrekken/opnieuw uitgeven | Ticket blokkeren of `credential_version` ophogen; oude codes direct ongeldig (online) of via revocatielijst (offline) | ADR-005 |
| Oude device verwijderen | Via app of portal; push-token verwijderd; device-key ingetrokken | 06 |
| Notificatievoorkeuren | Per categorie aan/uit; "dringend" niet uit te zetten | 02 |
| Importfouten / duplicate leden | ImportRow/SyncJobItem met foutstatus; duplicaten (zelfde e-mail/naam+geboortedatum) als conflict ter beoordeling | ADR-010 |
| Leden zonder e-mail | Geen app-account tot er een e-mailadres in e-Boekhouden staat; toegang via printkaart (ADR-005) | 02, ADR-014 |
| Fysieke alternatieve toegang | Printkaart, bandje, handmatige lijstcontrole door bestuur (noodprocedure) | 13/ADR-006 |
| Admin account recovery | Minimaal 2 break-glass beheerders met MFA; herstel via Entra-beheerder; procedure gedocumenteerd | 06 |
| Maintenance mode / min. appversie / forced update / feature flags | `AppConfiguration`-endpoint (`GET /api/v1/app-config`) + tabel `FeatureFlag` | 05 |
| AVG-verwijderverzoek / export | Verzoekworkflow in portal; anonimiseren i.p.v. hard delete waar bewaarplicht geldt | 06 |

## 8. Traceerbaarheid opdracht → documenten

| § | Onderwerp | Document |
|---|---|---|
| 1–3 | Werkwijze, gasten, platform | 01, ADR-001 |
| 4–5 | Rollen, permissions | 07 |
| 6–7 | Agenda, push | 02, 04, ADR-009 |
| 8–11 | e-Boekhouden, sync | 04, ADR-010 |
| 12 | Lid worden | 02 |
| 13–17 | QR, scan, bandjes | 02, 06, ADR-005, ADR-006 |
| 18–19 | Dagkaarten, pronkzitting, Mollie | 02, 05, 06 |
| 20–43 | Optocht | 13, 14, ADR-011, ADR-012 |
| 44–46 | Foto's, nieuws, nieuwsbrief | 02, 04 |
| 47–49 | Auth, wachtwoorden, devices | 06, ADR-004 |
| 50–54 | Ledenbeheer, rapportage, jubilarissen, CarnivalYear | 02, 04 |
| 55–56 | Audit, AVG | 06 |
| 57–60 | Azure, omgevingen, IaC, database | 08, ADR-003, ADR-007 |
| 61–63 | Datamodel, API | 04, 05, 14 |
| 64 | Offline | ADR-006 |
| 65–66 | Security, threat model | 06, 11 |
| 67–68 | Beheerportal, website | 02, 03 |
| 69–72 | Observability, DR, CI/CD, testing | 08, 12 |
| 73 | Optochttests | 12 |
| 74–77 | UX, toegankelijkheid | 02, 15 |
| 78 | Bijzondere situaties | 01 §7 |
| 79 | MVP | 09 |
| 81 | ADR's | docs/adr |
| 83 | Kosten | 08 |
