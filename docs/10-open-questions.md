# 10 – Besluitenregister (open vragen en beslissingen)

> Status: v0.4 · 2026-09-25 · **Alle blockers besloten** in de sessie met de opdrachtgever (zie §0a); OQ-75/76 (regio) nieuw bij fase 1
> Bronnen: requirements (01), functioneel ontwerp (02), architectuur (03/08/ARCHITECTURE.md), security (06/11), datamodel (04/14), ADR-001…013, architectuurreview (18).
> Status: 🔴 open blocker · 🟡 open · 🟢 besloten (voorstel door architect, bevestiging door opdrachtgever tenzij anders vermeld).

## 0a. Genomen besluiten (sessie 2026-09-24)

| ID | Besluit | Gevolg |
|---|---|---|
| B-01 | **Achtergrondtaken in de API-app** (hosted services) | ADR-007 geaccepteerd |
| B-02 | **Eén Entra External ID-tenant** voor Dev, Acc en Prod, met aparte app-registraties per omgeving. Dev/Acc-apps: **"Require user assignment"** (alleen toegewezen testers) **én** de Dev/Acc-API weigert tokens zonder claim `environmentAccess` (custom attribuut). MFA voor het beheerportal via Conditional Access (passkey aanbevolen) | ADR-004 herzien; 08 aangepast |
| B-03 | **Publieke GitHub-repository** (open source) met branch protection, verplichte review op de `production`-environment, CodeQL, secret scanning + push protection, Dependabot | 06, 08, SECURITY.md aangepast |
| B-04 | **Alle accounts op naam van de vereniging**, betaald door de vereniging, ≥ 2 beheerders per account, gedeelde wachtwoordkluis; D-U-N-S en store-accounts direct aanvragen | — |
| B-05 | **Alleen leden krijgen een account**, met uitzondering van **ouders/verzorgers van minderjarige leden** (alleen hun eigen kinderen, geen ledencontent). **Niet-leden schrijven zich voor de optocht in via een openbaar webformulier zonder account** en worden per e-mail geïnformeerd. **Zelfregistratie in Entra staat uit**; accounts worden pas **na goedkeuring door het bestuur** aangemaakt, in de volgorde e-Boekhouden → lokaal lid → Entra (bestaande leden bij een exacte match lidnummer + e-mail direct) | Nieuwe **ADR-014**; 01, 02, 04, 05, 06, 07, 11, 12, 13, 14, 15 aangepast; OQ-05 en OQ-11 daarmee besloten |
| B-06 | **Vrije velden in e-Boekhouden** (geboortedatum, inschrijfjaar, status, categorie), eenmalig gevuld vanuit het huidige ledenbestand; vereist een actieve ledenmodule (OQ-03 controleren) | ADR-010 |
| B-07 | **Planning is niet relevant** voor de documentatie: geen datums of capaciteitsramingen; de fasevolgorde is technisch bepaald | 09 en 15 zonder kalender |
| Plan | **Implementatieplan (15) goedgekeurd** als basis voor de bouw | Start fase 0 |
| OQ-74 | **Geen open-sourcelicentie**: de code is publiek zichtbaar, maar alle rechten zijn voorbehouden (geen `LICENSE`-bestand; vermelding in README) | Hergebruik alleen met toestemming van de vereniging |
| OQ-66 | **Toegankelijke tokenvarianten** voor kleine tekst (merkkleuren ongewijzigd); de designer kan later bevestigen | Fase 0 design tokens |
| B-04 (tijdelijk) | Repository voorlopig op `github.com/robinmom/vrolijkedrammers`; later overdragen aan een GitHub-organisatie van de vereniging | Transfer behoudt historie |
| B-02 (uitgevoerd) | External ID-tenant `vrolijkedrammersapp` aangemaakt (2026-09-24, Europe) met twee noodaccounts | [runbook](runbooks/entra-external-id.md) |
| B-04 (open actie) | **Tweede persoonlijke beheerder** (Global Administrator in de app-tenant en Owner op de Azure-subscription) nog aan te wijzen | Tot die tijd één persoonlijke beheerder + twee noodaccounts |

## 0. Indeling

| Klasse | Betekenis | Uiterlijk besluiten |
|---|---|---|
| **BLOCKER** | Moet besloten zijn voordat de implementatie kan starten, of blokkeert een fundamentfase (0–3) of de eerste fase met echte persoonsgegevens (8) | Vóór de start van fase 0 (B-01 t/m B-05, B-07); B-06 vóór fase 8 |
| **IMPORTANT** | Moet tijdens het MVP besloten worden (MVP = alles vóór carnaval 2027, fase 0–18 in [15](15-implementation-plan.md)) | Vóór de start van de genoemde fase |
| **LATER** | Kan na het MVP (na carnaval 2027) | — |

## 1. Overzicht

| ID | Onderwerp | Klasse | Nodig vóór fase | Status |
|---|---|---|---|---|
| B-01 | Hosting achtergrondverwerking (.NET 10 wordt niet ondersteund op Functions Linux Consumption) | BLOCKER | 1 | 🟢 besloten |
| B-02 | Identiteitsinrichting: tenants, beheerders, MFA | BLOCKER | 1 / 3 | 🟢 besloten |
| B-03 | Repository-zichtbaarheid en CI/CD-platform (goedkeuring prod-deploys, securityscans) | BLOCKER | 0 | 🟢 besloten |
| B-04 | Eigenaarschap accounts, budget en beheerders | BLOCKER | 0 / 1 (stores: 7) | 🟢 besloten |
| B-05 | Accountmodel: wie mag een account hebben, basisrol, lidkoppeling | BLOCKER | 3 | 🟢 besloten |
| B-06 | Ledengegevens uit e-Boekhouden: ledenmodule, vrije velden, statusbron (OQ-01/02/03) | BLOCKER | 8 | 🟢 besloten |
| B-07 | Planning, capaciteit en MVP-scope per datum (11-11 / optocht / carnaval) | BLOCKER | 0 | 🟢 besloten |
| OQ-04 | Testadministratie e-Boekhouden | IMPORTANT | 8 | 🟡 |
| OQ-05 | Nieuwe leden automatisch in e-Boekhouden aanmaken | → B-05 | 9 | 🟢 besloten: automatisch na goedkeuring (ADR-014) |
| OQ-06 | Syncfrequentie | IMPORTANT | 8 | 🟢 voorstel |
| OQ-10 | Deelnemersregel versus categorie | IMPORTANT | 11 | 🟡 |
| OQ-11 | Wie mag inschrijven voor de optocht | → B-05 | 3 | 🟢 besloten (B-05) |
| OQ-12 | Grens 10 bij loopgroepen | IMPORTANT | 11 | 🟡 |
| OQ-13 | Onderwerp verplicht | IMPORTANT | 11 | 🟡 |
| OQ-14 | Verplichte documenten per categorie | IMPORTANT | 11 | 🟡 |
| OQ-15 | Minimumleeftijd eigen account | IMPORTANT | 9 | 🟡 |
| OQ-20 | Definitie carnavalstoegang (AccessWindows, gasten) | IMPORTANT | 13 | 🟡 |
| OQ-21 | Bandjesbeleid | IMPORTANT | 14 | 🟡 |
| OQ-22 | Pasfoto in scanner | LATER | — | 🟡 |
| OQ-23 | Toegang zonder smartphone (printkaart) | IMPORTANT | 13 | 🟢 voorstel |
| OQ-24 | Mollie next-gen webhooks met signatures | LATER | 19 | 🟡 |
| OQ-25 | Certificate pinning scanner | IMPORTANT | 14 | 🟢 voorstel: nee |
| OQ-26 | MFA voor gewone leden | IMPORTANT | 3 | 🟢 voorstel: nee |
| OQ-30 | Jubileumregels | LATER | 20 | 🟡 |
| OQ-40 | "Uitslagen"-tegel | IMPORTANT | 6 | 🟡 |
| OQ-41 | Route/kaart optocht | IMPORTANT | 11 | 🟢 voorstel |
| OQ-42 | Ontbrekende Figma-schermen | IMPORTANT | 6/9/11/13/14 | 🟡 |
| OQ-43 | Nieuwsbrief e-maildienst | LATER | — | 🟡 |
| OQ-44 | 4-ogenprincipe push | IMPORTANT | 10 | 🟢 voorstel |
| OQ-45 | Bot-bescherming openbare formulieren | IMPORTANT | 9 | 🟢 voorstel |
| OQ-50 | Privacyverklaring en verwerkersovereenkomsten | IMPORTANT | 7 (go-live) | 🟡 |
| OQ-52 | Onderhoud en support | IMPORTANT | 7 (go-live) | 🟡 |
| OQ-60 | SQL-firewall versus Functions-IP's | → opgelost door B-01 | — | 🟢 |
| OQ-61 | Push-provider | IMPORTANT | 10 | 🟢 Expo |
| OQ-62 | Eén EF Core DbContext | — | 2 | 🟢 besloten |
| OQ-63 | Controllers of Minimal APIs | — | 0 | 🟢 besloten: Controllers |
| OQ-64 | Package manager/monorepo-tooling | — | 0 | 🟢 besloten: pnpm workspaces + één .NET solution |
| OQ-65 | Malwarescan: Defender for Storage of fallback | IMPORTANT | 5 (eerste upload) | 🟡 |
| OQ-66 | Contrastaanpassingen design tokens (designer) | IMPORTANT | 0 / 6 | 🟢 toegankelijke varianten |
| OQ-67 | Custom domains (api./beheer./login.) | IMPORTANT | 7 | 🟡 |
| OQ-68 | Haalbaarheid hardware-sleutel (Expo native module) | IMPORTANT | 9 (spike) / 13 | 🟡 |
| OQ-69 | Kosten Conditional Access/MFA in external tenant | IMPORTANT | 3 | 🟡 |
| OQ-70 | Releasevolgorde | → B-07 | 0 | 🟢 n.v.t. (B-07) |
| OQ-71 | Pronkzitting 2027 via de app | IMPORTANT | 13 | 🟡 |
| OQ-72 | Private endpoints/VNet, Front Door WAF | LATER | — | 🟡 |
| OQ-73 | Attestation (App Attest/Play Integrity) verplicht voor scanners | IMPORTANT | 14 | 🟢 voorstel: ja, "should" |
| OQ-75 | Azure-regio: West Europe neemt geen nieuwe klanten aan | IMPORTANT | 1 (uitrol) | 🟡 voorstel: Sweden Central |
| OQ-76 | Regio van de Static Web App (niet beschikbaar in Sweden Central; West Europe gesloten) | IMPORTANT | 1 (uitrol) | 🟡 voorstel: East US 2 (alleen statische code) |

---

## 2. BLOCKERS (volledig uitgewerkt)

### B-01 🟢 Hosting van achtergrondverwerking

> **Besluit:** optie A (achtergrondtaken in de API-app).

**Probleem.** ADR-007 koos Azure Functions (Consumption, .NET isolated) met .NET 10. Bij verificatie (Microsoft Learn, "Supported languages in Azure Functions", bijgewerkt 2026-09-17) blijkt: *".NET 9 is the last .NET version supported for Linux Consumption plan apps. Newer .NET versions aren't added to Linux Consumption."* Het Linux Consumption-plan wordt bovendien op 30-09-2028 uitgefaseerd. .NET 8 en 9 zijn nog maar ondersteund tot 10-11-2026. Het ontwerp is in deze vorm dus niet bouwbaar. Daarnaast veroorzaakten de wisselende IP-adressen van Functions het SQL-firewallprobleem (OQ-60).

Achtergrondtaken in scope: nachtelijke e-Boekhouden-sync, geplande publicatie, push-fan-out en receipts, beeldverwerking (thumbnails, EXIF strippen), Excel-importverwerking, retentie-opschoning en scan-reconciliatie (loopt al synchroon in de API).

| Optie | Voordelen | Nadelen |
|---|---|---|
| **A. Hosted background services in de API (App Service)**: `BackgroundService`-workers + DB-queue (outbox) + `sp_getapplock` voor singleton-jobs | Eén deployable en één identiteit; geen extra storage-account; vaste outbound-IP's van App Service, dus een eenvoudige SQL-firewall zonder "Allow Azure services"; lokaal eenvoudig te debuggen; geen cold starts | Werk deelt CPU met de API (B1: 1 core). Beeldverwerking kan pieken, maar is beperkt bij dit volume. Bij scale-out is een lock nodig (`sp_getapplock`). Jobs stoppen bij een herstart of deploy, dus ze moeten idempotent en hervatbaar zijn (dat zijn ze al) |
| B. Azure Functions **Flex Consumption** (.NET 10 isolated) | Gescheiden van de API; schaalt apart; nieuw plan met VNet-optie | Extra deployable, extra storage-account; outbound-IP's wisselen (zonder VNet/NAT) → "Allow Azure services" op SQL; cold starts; meer IaC en meer beheer |
| C. Functions op **Windows** Consumption | Consumption-model behouden | Windows-plan naast een Linux API; onduidelijke toekomst van Consumption; zelfde IP- en beheerbezwaren als B |
| D. Functions Linux Consumption met .NET 9 | Geen herontwerp | .NET 9 is einde ondersteuning op 10-11-2026 → **onacceptabel** |

**Aanbeveling: A.** Achtergrondverwerking draait als hosted services in de API-app (App Service B1, "Always On"). Werk wordt in een DB-tabel (`notification.Outbox` / `import.Job`) gezet en door workers opgepakt. Tijdgestuurde jobs lopen via een eenvoudige scheduler met `sp_getapplock`. Upgradepad: de workers staan in een aparte projectbibliotheek (`Drammers.Worker`) en kunnen later zonder codewijziging in een apart App Service-/WebJob- of Flex-proces draaien.

| Impact | |
|---|---|
| **Security** | + Minder aanvalsoppervlak: geen extra storage-account met keys, geen "Allow Azure services" op SQL (firewall alleen de App Service-outbound-IP's). − Eén managed identity voor API en worker: het e-Boekhouden-token en de Expo-token zijn leesbaar voor het API-proces. Mitigatie: aparte Key Vault-secrets, code-review op het gebruik, en een optionele *user-assigned* MI voor de sync-DB-verbinding |
| **Kosten** | Besparing: geen Functions-storage (~€ 1/mnd) en geen Defender voor een tweede account. Tijdens carnaval wordt de API toch al naar S1 opgeschaald. Netto ≈ € 0–2/mnd goedkoper |
| **Onderhoud** | Eenvoudiger: één pipeline, één runtime, één set logs. Iets meer eigen code (scheduler en queue-polling, ± 200 regels, of Quartz.NET/Coravel als bibliotheek) |

---

### B-02 🟢 Identiteitsinrichting: tenants, beheerders en MFA

> **Besluit:** afwijkend van de aanbeveling: **één tenant** voor alle omgevingen. Dev/Acc worden afgeschermd met *Require user assignment* op de Dev/Acc-app-registraties én een verplichte claim `environmentAccess` (custom attribuut) die de Dev/Acc-API controleert. Keerzijde: tenantbrede wijzigingen (user flow, branding, CA-policy) kunnen niet eerst in een aparte tenant worden getest → wijzigingen via een checklist en buiten drukke periodes; testaccounts herkenbaar (`test+…@`) en in de groep `Testers`.

**Probleem.** De documentatie noemt "drie tenants of twee" (ADR-004 en 08 verschillen) en MFA voor beheerders "met authenticator/Conditional Access op een groep". Uit verificatie (Microsoft Learn, "External tenant features", 2026-05) blijkt:
- Conditional Access in external tenants kan alleen *alle gebruikers* insluiten, met uitsluiting van gebruikers en groepen. Het doel kan wel beperkt worden tot *geselecteerde apps*.
- MFA-methoden voor klantaccounts zijn **e-mail-OTP, sms en passkey (FIDO2)**. Een authenticator-app wordt niet ondersteund.
- Beheerders (bestuur) zijn tegelijk leden, dus gebruikers van de app.

| Optie | Voordelen | Nadelen |
|---|---|---|
| **A. 2 external tenants** (non-prod voor Dev+Acc, prod). Bestuursleden gebruiken hun gewone account; **CA-policy "MFA vereist" op de app-registratie van het beheerportal** ; methode **passkey** aanbevolen, e-mail-OTP als terugval | Eén account per persoon; MFA precies waar nodig; laag beheer | E-mail-OTP als tweede factor na e-mail+wachtwoord is zwakker (dezelfde mailbox); passkey vraagt uitleg |
| B. 3 external tenants (dev/acc/prod) | Maximale scheiding | Extra beheer van app-registraties en testaccounts, zonder echte winst (geen productiedata buiten prod) |
| C. Beheerders in een aparte **workforce**-tenant (bijv. een M365-tenant van de vereniging) | Volledige Entra-MFA (authenticator), Conditional Access per groep | Bestuursleden hebben twee identiteiten; de API moet twee issuers accepteren en de gebruikers koppelen; hogere complexiteit; eventueel licentiekosten |

**Aanbeveling: A.** De MFA-policy geldt voor het beheerportal. Rechten blijven in onze eigen RBAC, dus iemand zonder admin-permissions ziet in het portal niets, maar heeft wel een MFA-stap. Daarnaast: *break-glass* tenantbeheerders als interne accounts met passkey. Sms-MFA staat uit (kosten, SIM-swap). Controleer vóór fase 3 de prijs van MFA/CA in external tenants (OQ-69).

| Impact | |
|---|---|
| **Security** | Admin-takeover-risico sterk verkleind (passkey = phishingbestendig); geen productiedata in non-prod; één issuer per omgeving |
| **Kosten** | € 0 tot 50.000 MAU; sms uit. CA/MFA-add-on-kosten nog te bevestigen (OQ-69) |
| **Onderhoud** | 2 tenants, 3 app-registraties per tenant (API, app, portal); gescript via Graph/`az` en gedocumenteerd |

---

### B-03 🟢 Repository-zichtbaarheid en CI/CD-platform

> **Besluit:** optie A (publieke GitHub-repository).

**Probleem.** De documenten gaan uit van GitHub met branch protection, verplichte PR-review, CodeQL, secret scanning met push protection en een **handmatige goedkeuring** voor productie-deploys. Uit verificatie (GitHub Docs, 2026-09) blijkt voor **private** repositories:
- *"If you are on a GitHub Free, GitHub Pro, or GitHub Team plan, required reviewers are only available for public repositories."* Een verplichte goedkeuring op de productie-environment vraagt dus GitHub Enterprise.
- Branch protection op private repositories vraagt minimaal GitHub Team.
- CodeQL en secret scanning (push protection) zijn voor private repositories betaalde add-ons (GitHub Advanced Security).

| Optie | Voordelen | Nadelen |
|---|---|---|
| **A. Publieke GitHub-repository** (open source, bijv. MIT of AGPL) | Alles gratis: branch protection, required reviewers op `production`, CodeQL, secret scanning + push protection, Dependabot, onbeperkte Actions-minuten | Broncode en documentatie zijn openbaar (geen secrets of PII, maar wel het securityontwerp: "security by design, niet by obscurity"); de vereniging moet dit willen; zorgvuldigheid met testdata |
| B. **Private GitHub, plan Team** (~$4/gebruiker/mnd) | Code privé; branch protection; environments met deployment-branch-policy | Geen required reviewers → prod-gate via beschermde release-tags (alleen maintainers) + een OIDC-federatie die alleen geldt voor `environment:production`; geen CodeQL of secret scanning → gitleaks + Semgrep OSS in de pipeline |
| C. **Azure DevOps** (Repos + Pipelines; gratis voor 5 gebruikers) | Private; branch policies en environment approvals gratis; goede Azure-integratie | Geen Dependabot/CodeQL/secret scanning zonder betaalde GHAS for Azure DevOps; gratis parallelisme moet worden aangevraagd; minder gangbaar voor vrijwilligers; mobiele EAS-integratie iets omslachtiger |
| D. Private GitHub Free | Gratis | Geen branch protection, geen goedkeuringen → voldoet niet aan §71 ("gecontroleerd") |

**Aanbeveling: A (publiek)** als het bestuur daarmee akkoord gaat. Dat geeft de sterkste gratis security-tooling en maakt overdracht aan andere vrijwilligers makkelijk. **Anders B** (private + Team) met de beschreven gecompenseerde maatregelen. Afgeraden: D.

| Impact | |
|---|---|
| **Security** | A: CodeQL, secret scanning, push protection en verplichte review voor prod gratis. Code is openbaar, maar het ontwerp gaat uit van publieke kennis (Kerckhoffs). B: gelijkwaardig niveau met open-source-scanners, maar zonder platform-afgedwongen prod-goedkeuring (proces-gate). C: goede gates, zwakkere scanning |
| **Kosten** | A € 0 · B ~€ 8–12/mnd (2–3 gebruikers) · C € 0 (tot 5 gebruikers) |
| **Onderhoud** | A/B: één platform (GitHub) voor code, issues, CI en Dependabot. C: twee werelden (EAS/GitHub-community versus DevOps) |

---

### B-04 🟢 Eigenaarschap van accounts, budget en beheerders

> **Besluit:** optie A (alles op naam van de vereniging, ≥ 2 beheerders).

**Probleem.** Voor fase 0–1 zijn nodig: een Azure-subscription (betaalwijze), een GitHub-organisatie (of DevOps), toegang tot DNS van het domein, Apple Developer- en Google Play-accounts (verificatie van de organisatie duurt weken; Apple vereist een D-U-N-S-nummer), een Expo-account, later een Mollie-account en een e-Boekhouden-API-token. Wie is eigenaar, wie betaalt, wie zijn de (minstens twee) beheerders? Dit bepaalt ook wie PR's kan reviewen (OQ-51/52).

| Optie | Voordelen | Nadelen |
|---|---|---|
| **A. Alles op naam van de vereniging**, betaald door de vereniging, ≥ 2 beheerders per account (bijv. voorzitter/secretaris + technisch vrijwilliger), credentials in een gedeelde wachtwoordkluis (Bitwarden Organizations/1Password Teams) | Continuïteit bij het wisselen van vrijwilligers; heldere verantwoording; vereist voor de app stores (organisatieaccount) | Administratieve opstart (KvK, D-U-N-S), kleine kosten voor de kluis |
| B. Op naam van een ontwikkelaar/leverancier, later overdragen | Snel starten | Overdracht van app store-accounts/bundle-ID's is lastig; risico op lock-in en verlies van toegang |

**Aanbeveling: A**, plus een **budgetbesluit**: ± € 60–100/mnd Azure + ± € 100/jaar app stores (+ eventueel GitHub Team, zie B-03), met budgetalerts op 80 % en 100 %. Direct starten met de D-U-N-S-aanvraag en de Apple/Google-organisatieaccounts (doorlooptijd 2–6 weken).

| Impact | |
|---|---|
| **Security** | Geen persoonsgebonden single points of failure; MFA op alle beheeraccounts; toegang intrekbaar bij vertrek |
| **Kosten** | Zie [08 §10](08-azure-infrastructure.md#10-kostenindicatie-83); wachtwoordkluis ± € 3–5/mnd |
| **Onderhoud** | Duidelijke eigenaar per account; overdraagbaar |

---

### B-05 🟢 Accountmodel: wie mag een account hebben?

> **Besluit:** optie B (**alleen leden**), aangevuld met: (a) niet-leden schrijven in voor de optocht via een openbaar webformulier met e-mailcommunicatie; (b) ouders/verzorgers van minderjarige leden krijgen een account met de rol Ouder; (c) zelfregistratie uit, accounts pas na goedkeuring, volgorde e-Boekhouden → lokaal → Entra. Uitgewerkt in **[ADR-014](adr/ADR-014-account-provisioning.md)**. De hieronder beschreven aanbeveling A (rol Gebruiker) is **vervallen**.

**Probleem.** De documenten spreken elkaar tegen:
- OQ-11 en 13 §2 zeggen: *ieder geverifieerd account (ook niet-leden) kan een optochtinschrijving starten; de rol groepsverantwoordelijke wordt bij het eerste concept toegekend.*
- 07-rbac geeft `parade.register` alleen aan de rol Groepsverantwoordelijke. Dat is een **kip-en-ei-probleem**: zonder die rol kun je geen concept maken, en de rol volgt pas uit het concept.
- Ouders/verzorgers en dagkaartkopers zijn ook vaak geen lid.

Dit bepaalt het `User`-model, de standaardrollen en het activatieproces (fase 3).

| Optie | Voordelen | Nadelen |
|---|---|---|
| **A. Twee niveaus**: iedere geverifieerde gebruiker krijgt automatisch de systeemrol **"Gebruiker"** (o.a. `parade.register`, `notification.read.own`, `ticket.read.own` voor gekochte tickets). Na activatie als lid komt daar de rol **"Lid"** (ex-"Carnavalist": ledencontent, QR-toegang, `member.read.own`) bij. Overige rollen (groepsverantwoordelijke, ouder, …) worden automatisch of handmatig toegekend | Past bij optocht (externe groepen), ouders en dagkaarten; geen kip-en-ei; leden-content blijft afgeschermd via audience "Members" | Meer accounts van niet-leden → AVG-bewaartermijn (2 jaar inactief, zie 06) en spam-risico (Entra-verificatie + rate limits) |
| B. Alleen leden krijgen een account; externe groepen schrijven zich in via een webformulier zonder account | Kleiner gebruikersbestand | Geen status of push voor externe groepen; wijzigen alleen via beheer; ouders die geen lid zijn vallen buiten push |
| C. Iedereen een account, zonder onderscheid tussen gebruiker en lid | Eenvoudig | Risico dat ledencontent openstaat voor niet-leden; tegen de requirements in |

**Aanbeveling: A.** Rol "Carnavalist" (§4.1) wordt technisch **"Lid"**, met als label "Carnavalist". `parade.register` hoort bij "Gebruiker". De rol "Groepsverantwoordelijke" is geen voorwaarde om in te schrijven, maar een automatisch toegekende rol na het eerste concept (voor doelgroepen en push). Leeftijdsgrens voor een eigen account: zie OQ-15.

| Impact | |
|---|---|
| **Security** | Duidelijke scheiding tussen geauthenticeerd en lid. Ledencontent vereist `membership_status = Active` (audience "Members"), niet alleen login. Bot/spam: Entra-e-mailverificatie + rate limits + handmatige goedkeuring van lidmaatschap |
| **Kosten** | Geen (MAU ruim onder 50k) |
| **Onderhoud** | Twee systeemrollen extra; retentiejob voor inactieve niet-ledenaccounts |

---

### B-06 🟢 Ledengegevens uit e-Boekhouden (vóór fase 8)

> **Besluit:** optie A (vrije velden in e-Boekhouden, eenmalig gevuld).

**Probleem.** Geverifieerd (OpenAPI e-Boekhouden, 2026-09-24): `/v1/member` levert één naamveld, één adresregel en `freeText1..10`, maar **geen** geboortedatum, inschrijfjaar, lidstatus of categorie, en geen wijzigingsdatum. Daarnaast is `/v1/member` alleen beschikbaar als de administratie ledenfunctionaliteit heeft. Zonder besluit is de sync niet te bouwen of te testen, en ontbreken gegevens voor leeftijd (dansgarde, minderjarigen), jubilarissen en toegang (actief/inactief).

| Optie | Voordelen | Nadelen |
|---|---|---|
| **A. Vrije velden in e-Boekhouden inrichten** (bijv. `freeText1` = geboortedatum `JJJJ-MM-DD`, `freeText2` = inschrijfjaar `JJJJ`, `freeText3` = status `actief/opgezegd/overleden`, `freeText4` = categorie) en bijhouden door de secretaris; mapping configureerbaar in de app | Eén bron van waarheid; geen dubbel beheer | Eenmalig vullen (± 300–800 leden); discipline bij de secretaris; vrij tekstveld → parsefouten (worden gerapporteerd) |
| B. Alleen NAW uit e-Boekhouden; geboortedatum/inschrijfjaar/status **lokaal** in het portal | Geen afhankelijkheid van de invulling in e-Boekhouden | Twee plekken om bij te houden; risico op divergentie |
| C. Eenmalige Excel-import van het huidige ledenbestand + daarna B | Snel startklaar | Idem B |
| D. Relaties (`/v1/relation`) als de ledenmodule ontbreekt | Werkt zonder ledenmodule | Minder passend datamodel; lidnummer = relatiecode (afspraak nodig) |

**Aanbeveling: A**, met **C als eenmalige vulling** van de vrije velden (script of handmatig), mits de ledenmodule actief is (OQ-03 controleren). Is de ledenmodule niet actief, dan D. De datamodellen (04) ondersteunen alle varianten. Het besluit bepaalt alleen de mappingconfiguratie en de testdata.

| Impact | |
|---|---|
| **Security** | Token met minimale rechten (alleen lezen); IBAN/mandaat worden genegeerd; bij optie B/C extra PII-invoer in het portal (audit) |
| **Kosten** | Geen; eenmalige inzet van de secretaris |
| **Onderhoud** | A: laag (één bron). B/C: dubbel beheer |

---

### B-07 🟢 Planning, capaciteit en MVP-scope per datum

> **Besluit:** planning is niet relevant voor de documentatie. Datums, capaciteitsramingen en de kalender zijn uit 09 en 15 verwijderd; de fasevolgorde blijft technisch bepaald.

**Probleem.** 09-roadmap gaat uit van R1 live op 11-11-2026, optochtinschrijving half december en QR/scanner half januari. Vandaag is het 24-09-2026. Het implementatieplan ([15](15-implementation-plan.md)) schat fase 0–7 (publieke lancering) op **± 12,5 ontwikkelweken** en fase 0–10 (leden + push) op **± 18 ontwikkelweken**; tot carnaval (fase 0–18) is het **± 33 ontwikkelweken**, terwijl er met 2 ontwikkelaars tot 30-01 ± 30 beschikbaar zijn. **11-11-2026 is met de volledige R1-scope niet haalbaar** en de totale planning heeft geen buffer, tenzij er meer capaciteit komt of de scope wordt beperkt.

| Optie | Voordelen | Nadelen |
|---|---|---|
| **A. 11-11 als "publieke lancering"** (fase 0–7): publieke app (programma, nieuws, foto's, countdown, optochtinfo) + contentbeheer live; ledenlogin + push ± 8-12 (fase 8–10); optochtinschrijving open ± 5-01 (fase 11–12); QR/scanner eind januari met generale repetitie (fase 13–18); carnaval 6–9 feb | Zichtbaar resultaat op 11-11; realistisch; risico's gespreid | Leden-features en push later dan gehoopt; de inschrijfperiode voor de optocht wordt korter (± 4–5 weken) |
| B. Volledige R1 op 11-11 met extra ontwikkelcapaciteit (3+ ontwikkelaars of een leverancier) | Oorspronkelijke planning | Kosten, coördinatie; hoog risico op kwaliteitsverlies |
| C. Geen productie vóór januari; alles in één release | Minder releasewerk | Grootste risico vlak voor carnaval; geen leerervaring |
| D. QR/scanner niet voor carnaval 2027 (papieren/bandjes-toegang), wel optocht | Minder druk | Belangrijk requirement schuift een jaar op |

**Aanbeveling: A**, met een harde **scope-freeze per fase** en de change freeze vanaf 30-01-2027. Als de capaciteit tegenvalt, schuift eerst fase 17 (dansgarde/ouders) naar na carnaval, daarna fase 16 (aanrijtijden via Excel/mail als noodvariant). QR (fase 13–15) heeft voorrang boven beide.

| Impact | |
|---|---|
| **Security** | Minder tijdsdruk = minder shortcuts; tijd voor een securitytest vóór carnaval (fase 18) |
| **Kosten** | A: binnen het vrijwilligersmodel. B: extra ontwikkelkosten |
| **Onderhoud** | Kleinere releases = beter te reviewen en terug te draaien |

---

## 3. IMPORTANT (besluiten tijdens het MVP)

| ID | Vraag | Aanbeveling | Vóór fase |
|---|---|---|---|
| OQ-04 | Testadministratie e-Boekhouden voor Acc | Gratis proefadministratie met fictieve leden; anders WireMock in Dev en een dry-run tegen prod in Acc | 8 |
| OQ-05 | Nieuwe leden via API aanmaken in e-Boekhouden | **Besloten (B-05/ADR-014)**: automatisch na goedkeuring via `POST /v1/member`; handmatige terugvaloptie per aanvraag | 9 |
| OQ-06 | Syncfrequentie | Nachtelijk 03:00 + handmatig; 2× per dag tijdens carnaval | 8 |
| OQ-10 | Deelnemersregel | Totaal = kinderen + volwassenen; `validation_mode` per categorie; waarschuwing bij jeugd met meer volwassenen | 11 |
| OQ-12 | Grens 10 | Beide categorieën toestaan bij 10 | 11 |
| OQ-13 | Onderwerp verplicht | Ja (configureerbaar) | 11 |
| OQ-14 | Verplichte documenten | Geen verplichting in 2027; configureerbaar later | 11 |
| OQ-15 | Minimumleeftijd account | 16 jaar; jonger via het account van de ouder/verzorger (rol Ouder, ADR-014) | 9 |
| OQ-20 | Carnavalstoegang | AccessWindow per carnavalsdag; één ledenticket per lid per jaar; gasten via dagkaart (na MVP) of papieren toegang | 13 |
| OQ-21 | Bandjes | Kleur per dag; geen herscan bij geldig bandje; configureerbaar | 14 |
| OQ-23 | Zonder smartphone | Printkaart + naamcontrole + bandje | 13 |
| OQ-25 | Pinning scanner | Niet in MVP | 14 |
| OQ-26 | MFA leden | Nee; wel beheerders (B-02, CA-policy op het portal). Scanners worden beschermd door het trusted device + device-key + biometrie/PIN bij het openen van de scanmodus (CA kan niet op een scope of rol binnen de app worden gericht) | 3 |
| OQ-40 | "Uitslagen"-tegel | Tegel tonen als link naar nieuwscategorie "Uitslagen" (geen aparte module) | 6 |
| OQ-41 | Route optocht | Statische routekaart + "Open in Kaarten" | 11 |
| OQ-42 | Ontbrekende Figma-schermen | Designer levert per fase aan: login/activatie + Mijn gegevens (vóór 9), wizard (vóór 11), QR + scanner (vóór 13/14); anders bouwen met bestaande componenten + review | 6/9/11/13/14 |
| OQ-44 | 4-ogen push | Nee; bevestigingsdialoog + `notification.send.urgent` + audit | 10 |
| OQ-45 | Bot-bescherming | Cloudflare Turnstile op openbare formulieren (lid worden, web-inschrijving) | 9 |
| OQ-50 | Privacyverklaring/DPA's | Bijwerken vóór de eerste productie-go-live met persoonsgegevens | 7 (go-live) |
| OQ-52 | Onderhoud/support | Aanwijzen: technisch eigenaar, back-up, supportkanaal, bereikbaarheid tijdens carnaval | 7 (go-live) |
| OQ-61 | Push-provider | Expo Push achter `IPushSender` | 10 |
| OQ-65 | Malwarescan | Defender for Storage in Prod (± € 10/mnd); Dev/Acc zonder, via dezelfde quarantaine-flow | 5 (eerste upload) |
| OQ-66 | Contrast-tokens | Voorstellen uit [17 §7](17-design-system.md#7-toegankelijkheid--bevindingen-en-voorstellen) laten bevestigen door de designer | 0 / 6 |
| OQ-67 | Custom domains | `api.`, `beheer.`, (optioneel) `login.` via de custom URL domain van External ID; DNS-toegang via B-04 | 7 |
| OQ-68 | Hardware-sleutel | Technische spike (1–2 dagen) in fase 9 om een ECDSA-P-256-sleutel te genereren en te laten ondertekenen in Secure Enclave/StrongBox via een Expo-module; fallback: server-signed kortlevende QR (ADR-005 optie 4) | 9 (spike) / 13 |
| OQ-69 | Kosten MFA/CA | Prijspagina External ID controleren; sms uit | 3 |
| OQ-71 | Pronkzitting 2027 | Via het bestaande kanaal; geen app-ticketing vóór carnaval | 13 |
| OQ-73 | Attestation scanners | "Should": App Attest/Play Integrity bij registratie van scanners; zonder attestation alleen met expliciete goedkeuring door het bestuur | 14 |
| OQ-75 | Azure-regio | West Europe weigert nieuwe resources voor deze subscription (`RequestDisallowedByAzure: not accepting new customers`, 2026-09-25); North Europe en Germany West Central hebben 0 B1-quota. **Aanbeveling: Sweden Central** (EU, AVG, ~25 ms vanaf NL, alle diensten incl. SQL free offer beschikbaar). Alternatief: France Central. Eén parameter (`DVD_LOCATION`) | 1 (uitrol) |
| OQ-76 | Regio Static Web App | SWA kan alleen in centralus, eastus2, westus2, westeurope, eastasia. **Aanbeveling: East US 2** — de SWA bevat alleen de gebouwde portalcode (geen persoonsgegevens; die staan in de API/SQL in de EU) en wordt wereldwijd via CDN geserveerd. Alternatief: het portal als statische bestanden vanuit de API-app serveren (EU, maar koppelt de uitrol van API en portal) | 1 (uitrol) |

## 4. LATER (na het MVP)

| ID | Vraag | Opmerking |
|---|---|---|
| OQ-22 | Pasfoto in scanner | Opt-in, na evaluatie van carnaval 2027 |
| OQ-24 | Mollie next-gen webhooks | Bij fase 19; klassiek + server-side verificatie volstaat |
| OQ-30 | Jubileumregels | Fase 20; `JubileeRule` is configureerbaar |
| OQ-43 | Nieuwsbrief-dienst | Na MVP |
| OQ-72 | VNet/private endpoints, Front Door WAF | Heroverwegen na carnaval 2027 op basis van incidenten/risico |
| — | Website-integratie (widgets), PDF-exports, trends, Notification Hubs-migratie | Na MVP |

## 5. Besloten (technisch, door architectuur; geen opdrachtgeverbesluit nodig)

| ID | Besluit | Motivatie |
|---|---|---|
| OQ-62 | Eén EF Core `DbContext` met schema-mapping per module | Eenvoud; transacties over modules; architectuurtests bewaken grenzen |
| OQ-63 | ASP.NET Core **Controllers** (geen Minimal APIs) | Attribuut-gebaseerde `[RequirePermission]`, eenvoudige reflectietest "elk endpoint geannoteerd", bekende structuur voor vrijwilligers |
| OQ-64 | Monorepo: één .NET-solution (`Drammers.sln`) + **pnpm workspaces** voor `apps/*` en `packages/*` | Gedeelde TS-packages (tokens, API-client) zonder publicatie |
| OQ-60 | SQL-firewall: alleen de App Service-outbound-IP's (+ tijdelijke beheer-IP's) | Volgt uit B-01 optie A |
| — | Scanresultaten: weergave-enum (`Valid`, `ValidRepeatSameDevice`, `WarnRepeatOtherDevice`, `Invalid`) versus opgeslagen `final_result` (`FirstEntry`, `RepeatSameDevice`, `RepeatOtherDevice`, `Invalid`) | Zie mapping in [04 §7](04-data-model.md#7-schema-ticketing) |
