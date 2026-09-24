# 16 – Definition of Done

> Status: v1.0 · 2026-09-24
> Geldt voor **elke pull request** (niveau 1) en **elke fase** uit [15-implementation-plan.md](15-implementation-plan.md) (niveau 2). Fase-specifieke aanvullingen en acceptatiecriteria staan per fase in 15.
> De checklist van niveau 1 komt in het PR-template (`.github/pull_request_template.md`). Een punt dat niet van toepassing is, wordt gemarkeerd met "n.v.t. + reden", nooit leeg gelaten.

## Niveau 1 – Pull request

### 1. Build en codekwaliteit
- [ ] **Code compileert** zonder fouten; nieuwe compiler-warnings zijn opgelost of expliciet onderbouwd (`TreatWarningsAsErrors` voor nieuwe projecten; nullable reference types aan).
- [ ] **Linting slaagt**: `dotnet format --verify-no-changes`, ESLint + Prettier (app, portal, packages), TypeScript `strict` zonder fouten.
- [ ] Code volgt de projectconventies (03 §5–7): modulegrenzen (architectuurtest groen), strongly-typed IDs, geen businesslogica in controllers of UI-componenten, geen `TODO` zonder issue-nummer.
- [ ] Geen ongebruikte code, geen uitgecommentarieerde code, geen debug-output.

### 2. Tests
- [ ] **Unit tests slagen** en er zijn nieuwe tests voor nieuwe of gewijzigde logica (domeinregels, validaties, statusmachines, berekeningen). Richtlijn: ≥ 80 % line coverage op domeinmodules; geen daling van de coverage.
- [ ] **Integratietests slagen waar relevant**: altijd bij wijzigingen aan de database (migraties, constraints, locking, queries), externe integraties (e-Boekhouden, Mollie, Expo; via WireMock-fixtures) en de achtergrondverwerking. Integratietests draaien tegen een echte SQL Server (Testcontainers).
- [ ] **API-tests**: nieuwe endpoints zijn opgenomen in de **permission-matrix-test** (verwacht 200/401/403/404 per standaardrol) en de reflectietest "elk endpoint heeft een autorisatie-annotatie" is groen.
- [ ] UI: component- of schermtests voor nieuwe schermen (Jest/RTL); bij wijzigingen aan een kritieke flow een bijgewerkte E2E-test (Maestro/Playwright).
- [ ] Geen tests uitgeschakeld (`Skip`, `.only`, `xit`) zonder issue-nummer.

### 3. Security
- [ ] **Geen secrets in de repository**: gitleaks (pre-commit + CI) groen; geen connection strings, keys, tokens of wachtwoorden in code, config, tests of documentatie; nieuwe secrets staan in Key Vault (Azure) of user-secrets (lokaal).
- [ ] **Securitycontrole uitgevoerd**, en in de PR-beschrijving beantwoord:
  - Autorisatie: welke permission of resource-scope beschermt de wijziging? Is BOLA getest (andermans object → 404)?
  - Input: alle invoer gevalideerd (FluentValidation/zod), maximale lengtes, geen mass assignment (expliciete DTO's)?
  - Output: geen gevoelige velden in responses die de rol niet mag zien?
  - Bestanden/uploads, SQL (alleen geparametriseerd), HTML (gesanitized), Excel-export (injectie-escape) waar van toepassing?
  - Audit: is een gevoelige actie (06 §12) vastgelegd in de AuditLog?
- [ ] SAST (CodeQL of Semgrep, B-03) en de dependency-scan geven geen nieuwe high/critical-bevindingen; nieuwe dependencies zijn gemotiveerd (licentie, onderhoud) in de PR.
- [ ] Privacy: nieuwe persoonsgegevens zijn gemarkeerd (🔒 in 04), hebben een bewaartermijn (06 §13) en komen niet in logs.

### 4. Database
- [ ] **Database-migratie aanwezig** voor elke modelwijziging (EF Core), met een beschrijvende naam, backwards-compatible (expand/contract) en getest: van de vorige versie naar de nieuwe, en het idempotente script draait twee keer foutloos.
- [ ] Constraints en indexen uit het datamodel zijn in de migratie opgenomen (niet alleen in code).
- [ ] Seeds (permissions, rollen, categorieën) zijn idempotent.
- [ ] DB-rechten: nieuwe tabellen vallen onder het juiste schema en de juiste rol (`app_runtime` zonder DDL; audit alleen INSERT/SELECT).

### 5. API en contract
- [ ] **API-documentatie bijgewerkt**: OpenAPI (gegenereerd) bevat de nieuwe/gewijzigde endpoints met beschrijving, voorbeelden, foutcodes (`x-error-codes`) en de benodigde permission.
- [ ] OpenAPI-diff: geen breaking change binnen `/v1` (of expliciet goedgekeurd, met migratiepad).
- [ ] De TS-client (`packages/api-client`) is opnieuw gegenereerd en app/portal compileren.
- [ ] Idempotency-Key / If-Match waar het API-ontwerp dat voorschrijft (05 §1).

### 6. Logging, observability en foutafhandeling
- [ ] **Logging toegevoegd**: structured logs (Serilog) voor belangrijke gebeurtenissen en fouten, met correlation-ID; custom events/metrics voor integraties en workers (03 §9). **Geen** wachtwoorden, tokens, push-tokens, QR-payloads, volledige e-mailadressen/telefoonnummers of andere onnodige PII in logs.
- [ ] **Foutafhandeling aanwezig**: fouten via RFC 9457 ProblemDetails met een machineleesbare `code` en een Nederlandse `detail`; geen stacktraces naar clients; verwachte fouten (validatie, conflict, niet gevonden) geven de juiste 4xx; externe calls met timeout, retry/backoff en duidelijke foutstatus; workers idempotent en hervatbaar.
- [ ] UI: foutmeldingen zijn begrijpelijk in het Nederlands, bij het veld en bovenaan; lege, laad- en foutstatussen zijn ontworpen (EmptyState/ErrorState); offline-gedrag gedefinieerd waar relevant.
- [ ] Nieuwe kritieke foutpaden hebben een alert of zijn opgenomen in een bestaande alert (08 §9).

### 7. UX en toegankelijkheid (bij UI-wijzigingen)
- [ ] Conform Figma of een gereviewde componentvariant; licht en donker.
- [ ] Toegankelijkheid: labels/`accessibilityLabel`, contrast volgens de tokens (17 §7), touch targets ≥ 44 pt/48 dp, Dynamic Type zonder afgekapte kritieke tekst, nooit alleen kleur als informatiedrager; portal zonder serious/critical axe-meldingen.
- [ ] Teksten in het Nederlands, zonder hardcoded strings buiten het tekstbestand (voorbereid op vertaling).

### 8. Documentatie en review
- [ ] **Documentatie bijgewerkt**: relevante docs (datamodel, API, RBAC-matrix, runbooks, README) en een ADR bij een architectuurwijziging; het besluitenregister (10) bijgewerkt als er een besluit valt.
- [ ] PR-beschrijving: wat en waarom, gekoppeld issue/fase, testbewijs (screenshots/recording bij UI), securityantwoorden (§3).
- [ ] Review door minimaal één andere ontwikkelaar (of volgens de afspraak uit B-03/B-04 als er maar één ontwikkelaar is: review door de technisch eigenaar binnen 2 werkdagen).
- [ ] CI volledig groen.

## Niveau 2 – Fase

Een fase is **Done** als:

1. Alle PR's van de fase voldoen aan niveau 1 en zijn gemerged naar `main`.
2. Alle **acceptatiecriteria** van de fase ([15](15-implementation-plan.md)) zijn aantoonbaar gehaald (checklist in de fase-issue, met bewijs: testrapport, screenshots, demo).
3. De fase is **gedeployed naar Dev en Acc** via de pipeline; smoke-tests groen; vanaf fase 7 ook naar Prod volgens de releaseafspraak (goedkeuring, buiten de change freeze).
4. **Demo en acceptatie** door de product owner (bestuurslid) op Acc.
5. Fase-specifieke DoD-punten uit 15 zijn afgevinkt.
6. **Geen open high/critical bugs of securitybevindingen** in de scope van de fase; medium/low staan als issue met prioriteit.
7. **Documentatie consistent**: de architectuurreview-checklist (18 §4) opnieuw gelopen voor de geraakte onderwerpen; afwijkingen van het ontwerp zijn vastgelegd (ADR of documentupdate).
8. Operationeel: nieuwe jobs en integraties staan in de monitoring (health, alerts); runbooks bijgewerkt waar nodig.
9. Git-tag `phase-NN-done` gezet; release notes bijgewerkt (CHANGELOG).
10. Een volgende fase hangt niet af van iets uit deze fase dat niet af is (bij twijfel: expliciet vastleggen wat bewust buiten scope is gelaten).

## Niveau 3 – Productierelease (vanaf fase 7)

- [ ] Go-live-checklist: backups/PITR aan, alerts actief, feature flags correct, minimum-appversie gezet, privacyverklaring actueel.
- [ ] Rollbackplan beschreven (vorige build + migratie is backwards-compatible).
- [ ] App-builds via EAS met de juiste environment; OTA-kanaal correct; store-review-notities bijgewerkt.
- [ ] Communicatie aan gebruikers/beheerders (release notes, instructie bij nieuwe functies).
