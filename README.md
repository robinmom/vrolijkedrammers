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

## Geplande repositorystructuur

```
docs/          ontwerpdocumentatie, ADR's, design-exports
src/           .NET backend (API, worker, modules)
tests/         unit-, integratie- en API-tests
apps/mobile    Expo-app
apps/admin     beheerportal
packages/      design tokens, gegenereerde API-client
infra/         Bicep
```

(Alleen `docs/` bestaat in deze fase.)

## Licentie

Deze repository is openbaar zichtbaar, maar er is **geen open-sourcelicentie** verleend: alle rechten zijn voorbehouden aan CV De Vrolijke Drammers. Hergebruik van code of documentatie alleen met schriftelijke toestemming van de vereniging.

## Bijdragen

- Werk via branches en pull requests; `main` is beschermd.
- Nooit secrets in Git (zie [SECURITY.md](SECURITY.md)).
- Leg ontwerpwijzigingen vast in een nieuwe of bijgewerkte ADR.
