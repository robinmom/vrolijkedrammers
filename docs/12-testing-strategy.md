# 12 – Teststrategie

> Status: concept v0.1 · 2026-09-24

## 1. Testpiramide en tooling

| Niveau | Doel | Tooling | Waar |
|---|---|---|---|
| Unit | Domeinlogica (statusmachines, validaties, nummering, QR-parsing, reconciliatie, jubileumberekening, audience-regels) | xUnit + FluentAssertions; Jest (app/portal) | `tests/Drammers.UnitTests`, `apps/*/__tests__` |
| Integratie | EF Core + echte SQL Server (constraints, locking, filtered unique indexes), Blob (Azurite), externe API's gemockt | Testcontainers (mcr.microsoft.com/mssql/server), Azurite, WireMock.Net, Respawn | `tests/Drammers.IntegrationTests` |
| API | Endpoints end-to-end in-process: auth, permissions, ProblemDetails, OpenAPI-contract | WebApplicationFactory + test-JWT-issuer | `tests/Drammers.ApiTests` |
| Contract | OpenAPI-diff (geen breaking changes), gegenereerde client compileert | oasdiff, tsc | CI |
| E2E portal | Kritieke beheerflows | Playwright | `apps/admin/e2e` |
| E2E app | Kritieke app-flows op emulator/simulator | Maestro | `apps/mobile/.maestro` |
| Security | SAST, dependencies, secrets, DAST-baseline | CodeQL (publieke repo) of Semgrep OSS, Dependabot, gitleaks, OWASP ZAP baseline (Acc) (B-03) | CI / pre-carnaval |
| Performance | Scanpiek, sync, exports | k6 | Acc, vóór carnaval |
| Handmatig | Toegankelijkheid, UX op echte devices, generale repetitie scannen | Checklists | Acc/Prod |

Streefwaarden: ≥ 80 % line coverage op domeinmodules (Parade, Ticketing, Identity-autorisatie, Import); geen coverage-doel op UI.

## 2. Testgebieden (§72)

### Authenticatie
- Token-validatie: verkeerde issuer/audience, verlopen token, ontbrekend token → 401.
- Accountverzoek (ADR-014): exacte match → Entra-account aangemaakt (Graph-mock) + welkomstmail; mismatch → wachtrij, identieke respons; 6e verzoek binnen een uur → 429; bestaand account → geen tweede account.
- Onbekende `oid` (account buiten de provisioning om) → 403; token zonder `environmentAccess` in Dev/Acc → 403.
- Provisioning-saga: fout na elke stap → retry hervat zonder duplicaten in e-Boekhouden of Entra.
- Geblokkeerd account → 403 op alle ingelogde endpoints.
- Device-registratie: challenge-signature geldig/ongeldig/verlopen.

### Permissions
- **Permission-matrix-test** (data-driven): voor elk endpoint × elke standaardrol het verwachte 200/403/401/404.
- Reflectietest: elk endpoint heeft `RequirePermission` of `AllowAnonymous`.
- Resource-scoping: groepsverantwoordelijke A kan inschrijving van B niet lezen/wijzigen (404); ouder kan alleen eigen kind zien.
- Audience-filter: gast/lid/rol/groep/individu/ouder × Public/Members/Restricted.
- Rolwijziging werkt door binnen de cache-TTL; `permissions_version` invalideert.

### QR
- Geldige QR → Valid; verlopen `exp` → Invalid/CodeExpired; verkeerde signature → Invalid/BadSignature; ander device-key → Invalid; oude `credential_version` → Invalid; ticket geblokkeerd/verlopen/geen lidmaatschap/onbekend → juiste reden.
- Zelfde QR twee keer op hetzelfde device → ValidRepeatSameDevice met de vorige tijd; op een ander device → WarnRepeatOtherDevice.
- Nieuw AccessWindow → weer Valid (eerste toegang per venster).
- Klokafwijking ±90 s wordt getolereerd, daarbuiten niet.
- QR bevat geen lidnummer/naam (payload-inspectie).
- Scan wordt altijd opgeslagen, ook bij Invalid; een dubbele scan overschrijft de eerste nooit.

### Offline-synchronisatie
- **Scenario A/B**: scanner A en B offline, beide scannen dezelfde QR (A om 20:14:03, B om 20:14:40 device-tijd) → na sync: A = FirstEntry, B = RepeatOtherDevice + `conflict_detected_after_sync = true`; beide records bestaan; het dashboard toont 1 unieke bezoeker, 2 scans, 1 conflict.
- Omgekeerde syncvolgorde (B synct eerst) → zelfde eindresultaat (deterministisch op device-tijd, tiebreak op `client_scan_id`).
- Dezelfde batch twee keer uploaden → idempotent (`client_scan_id` unique).
- Scanner met afwijkende klok (+10 min) → correctie via `clock_skew_ms`.
- Ticket online geblokkeerd terwijl de scanner offline is → offline Valid, na sync gemarkeerd als "geblokkeerd ticket toegelaten (offline)".
- Revocatielijst-delta wordt toegepast na reconnect.

### Mollie
- Order → payment create (WireMock-Mollie) met juiste amount/metadata/webhookUrl.
- Webhook met bekend id + status `paid` → tickets exact één keer uitgegeven, ook bij 3× dezelfde webhook en bij gelijktijdige webhooks.
- Webhook met een onbekend id → 200, niets gewijzigd, gelogd.
- Vervalste webhook-body met "status=paid" → genegeerd (status komt van GET).
- Statussen failed/canceled/expired → capaciteit vrijgegeven.
- Redirect-return zonder betaalde status → toont "in behandeling".
- Refund → ticket geblokkeerd, audit.
- Capaciteit: 50 gelijktijdige orders voor de laatste 10 plaatsen → exact 10 geslaagd.
- Mollie-sandbox (testmodus) handmatig in Acc met iDEAL-test.

### Import (e-Boekhouden en Excel)
- Eerste sync: N nieuwe leden; tweede sync zonder wijzigingen: 0 nieuw, N ongewijzigd (idempotent).
- Gewijzigd e-mailadres → Updated + SyncConflict bij een actief account.
- Lid verdwenen → Missing/Inactive; lokale gegevens (rollen, devices, tickets) blijven intact.
- > 10 % verdwenen → run gemarkeerd als Conflict, geen deactivaties.
- Ongeldige geboortedatum in een vrij veld → waarschuwing, oude waarde blijft.
- Session-token verlopen tijdens de run → nieuwe sessie; API-fout 5xx → retry met backoff, uiteindelijk Failed met een duidelijke melding.
- Aanrijtijden-Excel: onbekende registratie, dubbele groepen, onbekend startnummer, ontbrekende/ongeldige tijden, naam-mismatch (warning), `13.05`/`13:05`/Excel-tijd, alles-of-niets, herhaalde import zonder wijzigingen.

## 3. Specifieke optochttests (§73)

| # | Test | Niveau |
|---|---|---|
| P1 | **Twee gelijktijdige inschrijvingen krijgen verschillende opgavenummers**: 50 parallelle submits (Task.WhenAll, aparte DbContexts) → nummers 1..50 uniek en zonder gaten; herhaald 20× | Integratie (echte SQL) |
| P2 | Opgavenummer kan niet door een gebruiker worden gewijzigd: `registrationNumber` in een PUT-body (groep én admin) → 422/genegeerd, DB-waarde ongewijzigd | API |
| P3 | Opgavenummer wordt niet hergebruikt: submit #1, #2, #3; #2 intrekken; nieuwe submit → #4 | Integratie |
| P4 | Concept heeft geen opgavenummer; submit vult `registration_number` en `submitted_at` | Unit + integratie |
| P5 | Dubbele submit met dezelfde Idempotency-Key → hetzelfde nummer, geen tweede nummer | API |
| P6 | Startnummer mag aanvankelijk leeg zijn | Integratie |
| P7 | Dubbele startnummers worden geweigerd (409 `PARADE_START_NUMBER_TAKEN`), ook bij 2 gelijktijdige requests (DB-index) | API + integratie |
| P8 | Startnummer kan door een bevoegde (`parade.assign-start-number`) worden gewijzigd; groepsverantwoordelijke → 403 | API |
| P9 | Startnummerwijziging komt in AuditLog én ParadeRegistrationHistory (oud/nieuw/wie/wanneer) | Integratie |
| P10 | Generatie uit `parade_order`: FillEmpty raakt bestaande nummers niet aan; Renumber alleen na expliciete apply; slepen verandert geen startnummers | Unit + API |
| P11 | Loopgroep groot met < 10 deelnemers → validatiemelding (Block) | Unit + API |
| P12 | Loopgroep klein met 20 deelnemers → validatiemelding | Unit + API |
| P13 | Individueel/duo accepteert 1 en 2, weigert 3 | Unit |
| P14 | Negatieve aantallen niet toegestaan (validator + DB CHECK) | Unit + integratie |
| P15 | Lengte accepteert decimalen (4, 6.5, 12.8, "6,5" met komma in de UI) en weigert 0, negatief, > 100, 3 decimalen | Unit |
| P16 | Gemeten lengte overschrijft geschatte lengte niet (beide aanwezig na PUT measured) | Integratie |
| P17 | Juryadres gelijk aan bouwadres (same_as=true) → export toont het bouwadres; wijziging bouwadres werkt door | Unit |
| P18 | Juryadres afwijkend (same_as=false) → verplicht en apart opgeslagen | Unit + API |
| P19 | Goedgekeurde/finale inschrijving kan niet zonder de juiste rechten worden aangepast (groep: 403 `PARADE_FIELD_LOCKED`; commissie in Final: 403; `parade.manage-final`: 200) | API |
| P20 | Configuratie `validation_mode = Warn` → indienen lukt, waarschuwing opgeslagen | Unit |
| P21 | Categoriewijziging via ParadeCategory-tabel werkt zonder migratie (nieuwe categorie direct kiesbaar) | Integratie |
| P22 | Export Excel: kolomkoppen exact volgens §40, Excel-injectie geneutraliseerd | Unit |
| P23 | Withdrawn → startnummer vrijgegeven, opgavenummer blijft | Unit |
| P24 | Statusovergangen: alleen toegestane transities; ongeldige → 409 | Unit |

## 4. Testdata

- **Seed-builder** (C#): leden, rollen, CarnivalYear, categorieën, inschrijvingen in elke status, tickets, scans.
- Geen productiedata in Dev/Acc; indien nodig een geanonimiseerde export (script dat PII vervangt door Bogus-data).
- Vaste testaccounts per rol in de Dev/Acc Entra-tenant (wachtwoorden in de gedeelde wachtwoordkluis, niet in Git).

## 5. Pipeline-integratie

| Stage | Tests |
|---|---|
| PR | Unit, integratie (Testcontainers), API, lint, SAST (CodeQL of Semgrep), gitleaks, OpenAPI-diff, Jest; zie ook [16-definition-of-done.md](16-definition-of-done.md) |
| Merge → Dev | + smoke (health, publieke endpoints, login testaccount) |
| Tag → Acc | + Playwright E2E portal, Maestro E2E (EAS preview-build), ZAP-baseline (wekelijks) |
| Pre-carnaval (januari) | k6-belastingtest scan-endpoints (20 scans/min × 10 scanners × 3 = marge), generale repetitie met echte devices offline/online, DR-restore-test |

## 6. Toegankelijkheidstests

- Handmatig per release: VoiceOver (iOS) en TalkBack (Android) op Home, Programma, QR, Scanner, Optochtwizard.
- Dynamic Type / lettergrootte 200 %: geen afgekapte kritieke informatie.
- Kleurcontrast (tokens gecontroleerd, zie 17); scanner-resultaten herkenbaar zonder kleur (icoon + tekst + haptiek).
- Portal: axe-core in de Playwright-tests (geen serious/critical violations).
