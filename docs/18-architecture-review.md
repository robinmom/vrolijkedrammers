# 18 – Architectuurreview (afronding architectuurfase)

> Status: v1.0 · 2026-09-24
> Scope: README.md, ARCHITECTURE.md, SECURITY.md, docs/00–17, docs/adr/ADR-001…013.
> Methode: (1) volledige lezing van alle documenten; (2) kruiscontrole van begrippen, statussen, velden, endpoints, permissions, fasen en getallen; (3) herverificatie van externe aannames bij de bron (Microsoft Learn, GitHub Docs, e-Boekhouden OpenAPI, Mollie Docs); (4) correcties doorgevoerd of als besluit vastgelegd in [10](10-open-questions.md).

## 1. Samenvatting

- **25 tegenstrijdigheden of onjuistheden** gevonden en opgelost (§2), waarvan 3 met grote impact:
  1. **Azure Functions Linux Consumption ondersteunt geen .NET 10** (en wordt uitgefaseerd) → ontwerp herzien, blocker **B-01**.
  2. **Verplichte prod-goedkeuring, branch protection, CodeQL en secret scanning zijn niet gratis voor private GitHub-repositories** → blocker **B-03**.
  3. **Kip-en-ei in het rechtenmodel** (`parade.register` alleen voor de rol die pas ná inschrijving wordt toegekend) → accountmodel met systeemrollen Gebruiker/Lid, blocker **B-05**.
- **Externe feiten geverifieerd**: e-Boekhouden-ledenendpoints en -velden, Mollie-webhookgedrag, Entra External ID (native auth, MFA-methoden, Conditional Access), Azure Functions-taalondersteuning, GitHub-planbeperkingen.
- **7 blockers** vastgesteld; de overige besluiten zijn geclassificeerd als IMPORTANT of LATER (10).
- **Documentnummering**: het design system is hernoemd van 15 naar **17**, omdat 15/16 zijn voorgeschreven voor het implementatieplan en de DoD.

## 2. Gevonden tegenstrijdigheden en correcties

| # | Documenten | Tegenstrijdigheid / onjuistheid | Oplossing | Status |
|---|---|---|---|---|
| C-01 | ADR-007, 03, 08, ARCHITECTURE, 00, ADR-002/008/009/010 | Achtergrondtaken op Functions Linux Consumption met .NET 10: niet ondersteund (max .NET 9; plan wordt uitgefaseerd in 2028) | Hosted services in de API-app (B-01); alle diagrammen, kosten en identiteiten aangepast | ✅ aangepast (besluit B-01 open) |
| C-02 | 06 §11 ↔ 08 §5 / OQ-60 | 06: "Allow Azure services uit"; 08: "is nodig voor Functions" | Volgt uit B-01: alleen App Service-outbound-IP's, "Allow Azure services" uit | ✅ |
| C-03 | ADR-004 ↔ 08 §2 | "3 of 2 tenants" versus "dev/acc/prod-tenant (dev/acc gedeeld mogelijk)" | 2 tenants: non-prod (Dev+Acc) en prod (B-02) | ✅ |
| C-04 | ADR-004, 06 §2 | MFA met "authenticator-app" en Conditional Access "op een groep"; in external tenants niet ondersteund (alleen e-mail-OTP/sms/passkey; CA = alle gebruikers × apps) | CA-policy op de portal-app; passkey aanbevolen (B-02) | ✅ |
| C-05 | 07 ↔ 13 §2 / OQ-11 | `parade.register` alleen bij Groepsverantwoordelijke, terwijl die rol pas ná het eerste concept volgt | Systeemrol **Gebruiker** met `parade.register` (B-05) | ✅ |
| C-06 | 07 matrix | Groepsverantwoordelijke (kan een niet-lid zijn) kreeg `event.read/news.read/photo.read/member.read.own/ticket.read.own` | Verwijderd; leden krijgen deze rechten via de rol **Lid** | ✅ |
| C-07 | 02 §4 ↔ 07 §2 | Tegelvoorwaarde met permission `event.read.kader`, die niet in de catalogus staat | Kader via audience-regels (07 §4) | ✅ |
| C-08 | 02 §5.2, 05 §6 ↔ OQ-05 | "Knop Aanmaken in e-Boekhouden" gepresenteerd als besluit; OQ-05 beveelt handmatig aan in het MVP | Handmatig + `link-member`-endpoint; knop als optie na evaluatie | ✅ |
| C-09 | 02 §8 ↔ 04 | Lidmaatschapsstatus `Pending` bestaat niet in `Member.membership_status` | `Pending` is een status van de aanvraag, niet van een lid | ✅ |
| C-10 | 02 §5.4 / 05 §4 ↔ 04 §7 | Scanresultaat-enums verschillen (`Valid…` versus `FirstEntry…`) zonder uitleg | Weergave-enum (`local_result`) versus opslag-enum (`final_result`) + mapping in 04 | ✅ |
| C-11 | 02 §5.5 ↔ 04 §7 | Veldnamen in WristbandPolicy verschillen | Gelijkgetrokken naar 04 | ✅ |
| C-12 | 04 §9 ↔ 14 | `ParadeRegistrationContact` versus `ParadeRegistrationManager` | `ParadeRegistrationManager` | ✅ |
| C-13 | 05 §2 ↔ ADR-008 | Foto's via "publieke CDN" versus "geen publieke containers" | Altijd user-delegation-SAS | ✅ |
| C-14 | 05 §6 | Pad aanrijtijdenimport `/admin/parade/…` wijkt af van `/admin/parades/{id}/…` | Gelijkgetrokken | ✅ |
| C-15 | 06 §5 ↔ ADR-005 | QR-payload "~120 bytes, base45/base64url" versus "≈ 97 bytes, base45" | ≈ 97 bytes, base45 | ✅ |
| C-16 | 01 NFR-13 ↔ 08 §8 | RPO tijdens carnaval 5 versus 10 min | 10 min | ✅ |
| C-17 | 01 REQ-NOT-01 ↔ 09 | Groepen als doelgroep in R3 versus R2 | Fase 10 (push), ouders fase 17 | ✅ |
| C-18 | 03 §4 ↔ ADR-002 | "Controllers" versus "Controllers of Minimal APIs" | Controllers (OQ-63) | ✅ |
| C-19 | 04 §16 ↔ ADR-002 / OQ-62 | "Eén of meerdere DbContexts: keuze in de bouwfase" versus "één DbContext" | Eén DbContext (OQ-62) | ✅ |
| C-20 | 06 §14, 08 §7, SECURITY.md, 11 T20, 12 | Aanname dat GitHub-branch protection, required reviewers, CodeQL en secret scanning gratis zijn; bij private repositories niet (required reviewers alleen Enterprise) | Afhankelijk gemaakt van B-03; alternatieven beschreven | ✅ (besluit B-03 open) |
| C-21 | 04 `User.account_status` ↔ B-05 | `PendingActivation` impliceert dat een account zonder lid onvolledig is | Account zonder lid = `Active` met rol Gebruiker | ✅ |
| C-22 | 04 `Device.platform` | `web` als device-platform, terwijl device-keys en push alleen mobiel zijn | Alleen `ios`/`android` | ✅ |
| C-23 | ADR-005 ↔ ADR-006 | Wissen van de scanner-cache: "na AccessWindow/einde carnaval" versus "einde carnavalsjaar" | 24 uur na het laatste AccessWindow, of bij uitloggen | ✅ |
| C-24 | 14 §3 | `ParadeCategory.code` globaal uniek, terwijl per-optocht-overrides (`parade_id`) dezelfde code nodig hebben; `composition_version` (ADR-012) ontbrak in `Parade` | Uniek per (`parade_id`, `code`); kolom toegevoegd | ✅ |
| C-25 | 09 ↔ capaciteit / 15 | R1-scope op 11-11 niet haalbaar met 1–2 ontwikkelaars; volgorde QR vóór optocht in het oorspronkelijke voorstel | Roadmap herschreven naar fasen; B-07 | ✅ (besluit B-07 open) |

Niet gewijzigd, wel opgemerkt (laag risico):
- **07**: Beheerder (IT) heeft `member.read/update/block`, terwijl de toelichting "geen inhoudelijke rechten" zegt. Dat is bewust (technisch beheer van accounts), maar het bestuur kan dit inperken tot `member.block` (IMPORTANT, fase 4).
- **07 §5**: de permission-cache per instance (5 min) is bij autoscale (S1, carnaval) niet direct consistent over instanties. Acceptabel binnen REQ-RBAC-05 ("maximaal 5 minuten").

## 3. Specifieke controles

### 3.1 Mobiele technologie (ADR-001, 03, 17)
- ✅ Consistent: React Native + Expo, TypeScript, Expo Router, EAS, development builds.
- ⚠ Enige technische onzekerheid: de **hardware-sleutel** (Secure Enclave/StrongBox, ECDSA P-256) vraagt een eigen Expo native module. `expo-secure-store` genereert geen hardware-keys (al gecorrigeerd in 06/ADR-005). → **Spike in fase 9 (OQ-68)**, fallback ADR-005 optie 4.
- ✅ `expo-sqlite` met SQLCipher voor de scanner-cache; `expo-camera` voor barcodes; `expo-notifications` voor push.
- ✅ OIDC via `expo-auth-session` met de `ciamlogin.com`-authority (standaard OIDC-discovery); geen native-auth-SDK voor RN (geverifieerd). Verificatie van de redirect-flow in fase 3.

### 3.2 Backend-technologie (ADR-002, 03)
- ✅ ASP.NET Core .NET 10 LTS (ondersteund tot november 2028), Controllers, EF Core, één DbContext.
- ⚠ Achtergrondverwerking: zie §3.4.
- ✅ Modulegrenzen met een architectuurtest; OpenAPI → TS-client.

### 3.3 Authenticatie (ADR-004, 06 §2)
- ✅ Entra External ID, OIDC + PKCE, gratis tot 50k MAU, e-mail+wachtwoord en e-mail-OTP, self-service password reset.
- ⚠ Gecorrigeerd: MFA-methoden en CA-mogelijkheden in external tenants (C-04); tenantindeling (C-03). **Open**: B-02 (bevestiging), OQ-69 (kosten CA/MFA).
- ✅ Wachtwoorden nooit in onze systemen; "Wachtwoord opnieuw instellen" via Entra.
- ✅ Accountmodel na B-05 consistent in 01/02/04/05/07/13.

### 3.4 Azure-hosting (ADR-007, 08)
- 🔴 Was niet bouwbaar (C-01). Herzien naar App Service met hosted workers; besluit **B-01**.
- ✅ Na herziening: geen extra storage-account, vaste outbound-IP's, SQL-firewall strikt, kosten ongewijzigd of lager.
- ✅ Static Web Apps voor het portal; geen Front Door/APIM in het MVP (heroverwegen later, OQ-72).

### 3.5 Databasearchitectuur (ADR-003, 04, 14)
- ✅ Eén Azure SQL-database met schema's; least-privilege DB-users; audit append-only via rechten.
- ✅ De cross-schema-FK `identity.User.member_id → membership.Member` wordt gefaseerd toegevoegd (kolom in fase 3, FK in fase 8) — vastgelegd in 04.
- ✅ Na B-01 vervalt de aparte `app_sync`-identiteit (optioneel via een user-assigned MI).
- ✅ Constraints voor opgave- en startnummer consistent in 14, ADR-011, ADR-012 en 12.

### 3.6 e-Boekhouden-integratie (ADR-010, 04 §4)
- ✅ Geverifieerd tegen de officiële OpenAPI-spec (2026-09-24): sessie-token, `/v1/member` (lijst/detail), velden, filters, paginering (max 2000).
- ⚠ Bevestigde beperkingen: geen geboortedatum, inschrijfjaar, status of wijzigingsdatum; één naam- en adresveld; het item-schema van de lijst is onvolledig gedefinieerd (daarom een detailcall per lid); rate limits niet gedocumenteerd. → **B-06**.
- ✅ Veldeigenaarschap, idempotentie, massadeletie-guard en dry-run consistent in 04, 06, 11, 12 en ADR-010.

### 3.7 QR-security (ADR-005, 06 §5, 11)
- ✅ Dynamische, device-gebonden, ondertekende QR; geen PII; replaydetectie; printkaart; gast-QR (server-signed) na het MVP.
- ✅ Gecorrigeerd: payloadgrootte (C-15), P-256 in plaats van Ed25519 (fase 0), moment waarop de cache gewist wordt (C-23).
- ⚠ Afhankelijk van spike OQ-68.

### 3.8 Offline scanning (ADR-006, 12)
- ✅ Bootstrap-cache, online-first met timeout, idempotente batch, deterministische reconciliatie, conflictvlaggen, tests voor het A/B-scenario.
- ✅ Scanresultaat-enums nu eenduidig (C-10).
- ✅ Stored procedure als enige schrijver van `final_result`.

### 3.9 Blob Storage (ADR-008, 06 §8)
- ✅ Private containers, user-delegation-SAS, quarantaine-flow, re-encoding, versioning/soft delete.
- ✅ Gecorrigeerd: "publieke CDN" (C-13); beeldverwerking als worker (C-01).
- ✅ Scanmodus per omgeving vastgelegd (Defender in Prod, `None` in Dev/Acc met identieke flow). **Open**: OQ-65 (budget Defender).

### 3.10 Pushnotificaties (ADR-009, 02 §5.3, 04 §6)
- ✅ Expo Push achter `IPushSender`, doelgroep-expansie in de eigen DB, inbox als bron van waarheid, receipts, enhanced security.
- ✅ Fase vervroegd (10) zodat optocht, aanrijtijden en dansgarde er gebruik van kunnen maken.
- ✅ `on_behalf_of_member_id` is al in fase 10 aanwezig (nullable) en wordt in fase 17 gebruikt.

### 3.11 Mollie (06 §6, 02 §5.6, 05, 12)
- ✅ Geverifieerd: webhook met alleen `id`, status ophalen, `200 OK` verwacht, 10 retries in 26 uur, IP-allowlisting afgeraden.
- ✅ Na het MVP (fase 19); consistent in 01, 09 en 15.
- ◐ Next-gen webhooks met signatures (beta) → LATER (OQ-24).

### 3.12 Rollen en rechten (07, 02, 05, 13)
- ✅ Consistent na C-06/C-07 en besluit B-05: alleen leden (rol Lid) en ouders (rol Ouder/verzorger) hebben een account; resource-scoping; audiences los van permissions (ADR-014).
- ✅ Elk endpoint in 05 noemt een permission of is publiek; de reflectietest en matrix-test zijn onderdeel van de DoD (16).
- ◐ Opmerking over de Beheerder (IT) (§2).

### 3.13 Optocht-datamodel (14, 13, ADR-011/012, 12 §3)
- ✅ Alle velden uit de opdracht (§23–§34, §62) aanwezig; adressen als owned types; `total_participants` computed; gemeten ≠ geschatte lengte; `extra_fields` voor toekomstige velden.
- ✅ Opgavenummer (teller + lock + unique index, geen hergebruik) en startnummer (filtered unique, expliciete generatie, historie) consistent.
- ✅ Gecorrigeerd: uniciteit van categoriecodes met overrides en `composition_version` (C-24); `ParadeRegistrationManager` (C-12).
- ◐ De businessregel voor deelnemers is nog een voorstel (OQ-10, IMPORTANT vóór fase 11).

## 4. Review-checklist (terugkerend, zie DoD niveau 2)

Bij elke fase-afronding voor de geraakte onderwerpen:

1. Komen entiteits-, veld- en enumnamen overeen tussen 04/14, 05 en de code?
2. Staat elk nieuw endpoint in 05, met permission en foutcodes, en in de permission-matrix-test?
3. Staan nieuwe permissions of rollen in 07 (catalogus + matrix + seed)?
4. Zijn getallen (limieten, termijnen, RPO/RTO, kosten) gelijk in alle documenten waar ze voorkomen?
5. Zijn externe aannames (API's, plannen, prijzen) nog actueel (maximaal 6 maanden oud of opnieuw geverifieerd)?
6. Zijn besluiten die tijdens de fase vielen, vastgelegd in 10 (en zo nodig in een ADR)?
7. Klopt de fasevolgorde in 15 nog met de werkelijkheid (afhankelijkheden, geen half werk)?

## 5. Besluiten van de opdrachtgever verwerkt (2026-09-24)

Na de review zijn alle blockers besloten ([10 §0a](10-open-questions.md#0a-genomen-besluiten-sessie-2026-09-24)). Dat had de volgende gevolgen voor de documentatie:
- **B-05 wijkt af van de aanbeveling** ("alleen leden"). De eerder in C-05 ingevoerde systeemrol **Gebruiker is vervallen**. De kip-en-ei-situatie is nu opgelost doordat `parade.register` bij de rol **Lid** hoort, terwijl niet-leden het openbare optochtformulier gebruiken. Nieuwe **ADR-014** beschrijft de accountprovisioning (zelfregistratie uit; wachtrij → goedkeuring → e-Boekhouden → lokaal → Entra; bestaande leden via een exacte match). Geverifieerd: zelfregistratie kan uit (`isSignUpAllowed = false`); accounts kunnen via Microsoft Graph worden aangemaakt.
- **B-02 wijkt af van de aanbeveling** (één tenant): Dev/Acc afgeschermd met *Require user assignment* + claim `environmentAccess`; restrisico (tenantbrede wijzigingen) opgenomen als T24.
- **B-07**: planning, datums en capaciteitsramingen verwijderd uit 09 en 15.
- OQ-05 (automatisch aanmaken in e-Boekhouden) en OQ-11 (wie mag inschrijven) zijn daarmee besloten; het e-Boekhouden-token heeft schrijfrechten nodig (OQ-03 blijft open: controle van de rechten).

## 6. Gewijzigde documenten in deze review

README.md, ARCHITECTURE.md, SECURITY.md, 00, 01, 02, 03, 04, 05, 06, 07, 08, 09, 10 (herschreven als besluitenregister), 11, 12, 13, 14, 17 (hernoemd van 15), ADR-002, ADR-003, ADR-004, ADR-005, ADR-006, ADR-007 (herzien), ADR-008, ADR-009, ADR-010, ADR-013; nieuw: 15-implementation-plan, 16-definition-of-done, 18-architecture-review.
