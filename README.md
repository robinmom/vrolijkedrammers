# De Vrolijke Drammers App

Platform voor carnavalsvereniging **CV De Vrolijke Drammers** (Loil, sinds 1958): een mobiele app voor iOS en Android, een webgebaseerde beheeromgeving en een API in Microsoft Azure.

> **Status: architectuurfase afgerond; alle blockers besloten (2026-09-24) – nog geen applicatiecode.** De bouw start zodra het implementatieplan ([docs/15-implementation-plan.md](docs/15-implementation-plan.md)) is goedgekeurd.

## Wat doet het platform?

- **Publiek**: programma/agenda, nieuws, foto's, optochtinformatie, lid worden.
- **Leden**: persoonlijke QR-toegang tijdens carnaval, meldingen, eigen gegevens en lidmaatschap.
- **Rollen**: groepsverantwoordelijke (optochtinschrijving), kader, dansgarde (incl. ouders/verzorgers), Raad van Elf, bestuur.
- **Beheer**: leden (gesynchroniseerd met e-Boekhouden), content, pushmeldingen, optocht (opgave-/startnummers, samenstellen, aanrijtijden, exports), tickets/scans, rapportages, auditlog.
- **Integraties**: e-Boekhouden (leden), Mollie (betalingen), Entra External ID (inloggen), Expo Push (meldingen).

## Documentatie

Begin bij **[docs/00-overzicht.md](docs/00-overzicht.md)**.

| Onderwerp | Document |
|---|---|
| Requirements | [docs/01-requirements.md](docs/01-requirements.md) |
| Functioneel ontwerp | [docs/02-functional-design.md](docs/02-functional-design.md) |
| Architectuur | [ARCHITECTURE.md](ARCHITECTURE.md) · [docs/03-architecture.md](docs/03-architecture.md) |
| Datamodel | [docs/04-data-model.md](docs/04-data-model.md) · [docs/14-parade-data-model.md](docs/14-parade-data-model.md) |
| API | [docs/05-api-design.md](docs/05-api-design.md) |
| Security | [SECURITY.md](SECURITY.md) · [docs/06-security.md](docs/06-security.md) · [docs/11-threat-model.md](docs/11-threat-model.md) |
| Rollen en rechten | [docs/07-rbac.md](docs/07-rbac.md) |
| Azure en kosten | [docs/08-azure-infrastructure.md](docs/08-azure-infrastructure.md) |
| MVP en roadmap | [docs/09-mvp-roadmap.md](docs/09-mvp-roadmap.md) |
| Besluitenregister (open vragen) | [docs/10-open-questions.md](docs/10-open-questions.md) |
| Teststrategie | [docs/12-testing-strategy.md](docs/12-testing-strategy.md) |
| Optochtproces | [docs/13-parade-process.md](docs/13-parade-process.md) |
| Design system (Figma) | [docs/17-design-system.md](docs/17-design-system.md) |
| Implementatieplan | [docs/15-implementation-plan.md](docs/15-implementation-plan.md) |
| Definition of Done | [docs/16-definition-of-done.md](docs/16-definition-of-done.md) |
| Architectuurreview | [docs/18-architecture-review.md](docs/18-architecture-review.md) |
| Architecture Decision Records | [docs/adr/](docs/adr/) |

## Technologie (voorgesteld)

| Laag | Keuze |
|---|---|
| Mobiel | React Native + Expo (TypeScript) |
| Beheerportal | React + Vite (TypeScript), Azure Static Web Apps |
| API | ASP.NET Core (.NET 10 LTS, Controllers), EF Core, Azure App Service |
| Achtergrond | Hosted services in de API-app (geen Functions in het MVP, zie B-01) |
| Data | Azure SQL Database, Azure Blob Storage |
| Identiteit | Microsoft Entra External ID (één tenant, geen zelfregistratie, ADR-014) + eigen RBAC |
| Infra | Bicep, GitHub Actions (publieke repository, B-03) |

## Repositorystructuur

```
docs/                    ontwerpdocumentatie, ADR's, design-exports
src/                     .NET backend: Drammers.Api (host), Drammers.Worker, SharedKernel, Infrastructure, Modules.*
tests/                   UnitTests (incl. architectuurtests), IntegrationTests (vanaf fase 2), ApiTests
openapi/v1.json          OpenAPI-contract, gegenereerd bij elke build van de API
apps/mobile              Expo-app (SDK 57, Expo Router, routes in src/app)
apps/admin               beheerportal (React + Vite)
packages/design-tokens   kleuren, typografie en maten uit Figma (met contrasttests)
packages/api-client      getypte API-client, gegenereerd uit openapi/v1.json
infra/                   Bicep (main + modules + env), bootstrap (eenmalig) en Entra-scripts; zie docs/runbooks/omgeving-opbouwen.md
```

## Lokaal starten

Benodigd: .NET 10 SDK, Node.js 22, Xcode (iOS-simulator) en/of Android Studio. Docker is vanaf fase 2 nodig voor integratietests.

```bash
corepack enable pnpm          # eenmalig: pnpm-versie uit package.json
pnpm install                  # alle JS/TS-pakketten (hoisted, zie pnpm-workspace.yaml)

dotnet build Drammers.sln     # API + modules; schrijft ook openapi/v1.json
dotnet test Drammers.sln
dotnet run --project src/Drammers.Api        # http://localhost:5162/health/live

pnpm --filter @drammers/admin dev            # beheerportal op http://localhost:5173
cd apps/mobile && npx expo start             # app; druk op i (iOS) of a (Android)
```

Kwaliteitscontroles (ook in CI): `pnpm lint`, `pnpm typecheck`, `pnpm test`, `dotnet format Drammers.sln --verify-no-changes`.

Afspraken in de monorepo:
- **Supply-chain-beveiliging (pnpm)**: pakketten jonger dan 24 uur worden niet geïnstalleerd, en installatiescripts van pakketten staan uit, behalve wat expliciet in `pnpm-workspace.yaml` (`allowBuilds`) is beoordeeld.
- **Eén React-versie** voor app en portal (`overrides` in `pnpm-workspace.yaml`).
- **Iconen** komen ongewijzigd uit Figma (`apps/mobile/assets/icons`); `pnpm --filter @drammers/mobile icons` genereert daaruit `icons.generated.ts`. De app kleurt ze in via het thema.
- De componentenpagina (Meer → Componenten) is alleen zichtbaar in development-builds.

## Licentie

Deze repository is openbaar zichtbaar, maar er is **geen open-sourcelicentie** verleend: alle rechten zijn voorbehouden aan CV De Vrolijke Drammers. Hergebruik van code of documentatie alleen met schriftelijke toestemming van de vereniging.

## Bijdragen

- Werk via branches en pull requests; `main` is beschermd.
- Nooit secrets in Git (zie [SECURITY.md](SECURITY.md)).
- Leg ontwerpwijzigingen vast in een nieuwe of bijgewerkte ADR.
