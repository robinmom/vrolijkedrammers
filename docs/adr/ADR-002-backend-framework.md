# ADR-002: Backend-framework

- **Status**: Geaccepteerd (voorkeur opdrachtgever) · 2026-09-24 · aangevuld: Controllers (OQ-63), workers in-process (B-01)

## Context

Een API is nodig voor de app, het beheerportal en de website, met transacties (opgavenummer), integraties (e-Boekhouden, Mollie, push), achtergrondtaken en hosting volledig in Azure. De organisatie is klein; onderhoudbaarheid en lange ondersteuning wegen zwaar. De opdrachtgever heeft voorkeur voor ASP.NET Core aangegeven.

## Options considered

1. **ASP.NET Core (.NET 10 LTS)**: modulaire monoliet met EF Core.
2. **Node.js/TypeScript** (NestJS/Fastify) met Prisma/Drizzle.
3. Microservices (per domein een service).
4. Low-code/BaaS (Supabase/Firebase).

| Criterium | ASP.NET Core | Node/TS | Microservices | BaaS |
|---|---|---|---|---|
| Azure-integratie (MI, Key Vault, App Insights, Entra) | Uitstekend, first-party SDK's | Goed | Idem, maar ×N | Beperkt/extern |
| Transacties/locking op SQL Server | EF Core + SQL Server volwassen | Prima, minder "native" | Complex (distributed) | Beperkt |
| Typeveiligheid/onderhoud | Sterk, LTS 3 jaar | Sterk (TS), snellere ecosysteemchurn | Hoge overhead | Vendor lock-in |
| Hergebruik met frontend | Via OpenAPI-codegen | Direct (gedeelde TS) | — | — |
| Complexiteit | Laag (monoliet) | Laag | Hoog | Laag, maar custom logica lastig |

## Decision

**ASP.NET Core (.NET 10 LTS) als modulaire monoliet**, met:
- **Controllers** per module (besluit OQ-63), service-klassen per use case (geen MediatR/CQRS-framework);
- EF Core (SQL Server-provider), één DbContext met schema's per module;
- FluentValidation, Serilog → Application Insights, `Microsoft.Identity.Web`, ingebouwde RateLimiter, ProblemDetails, `Microsoft.AspNetCore.OpenApi`;
- Achtergrondtaken als hosted services (`Drammers.Worker`) in dezelfde App Service (ADR-007, B-01); ontkoppeld van ASP.NET zodat ze later een eigen proces kunnen krijgen.

## Reasoning

- Eén deployable is eenvoudig te begrijpen, te testen en goedkoop te hosten; de modulegrenzen houden de code overzichtelijk en maken latere splitsing mogelijk.
- Beste match met Azure SQL (transacties, `UPDLOCK`, filtered unique indexes) en Azure-diensten via managed identity.
- De LTS-cyclus past bij het onderhoudsritme van een vereniging.
- Het typeveiligheidsvoordeel richting de frontend wordt gehaald via OpenAPI-codegen.

## Consequences

- Twee talen in het project (C# backend, TypeScript frontend); OpenAPI is het contract.
- Architectuurtests (NetArchTest) bewaken modulegrenzen.
- Upgrade naar de volgende LTS (.NET 12) plannen vóór het einde van de ondersteuning van .NET 10 (nov 2028).

## Security implications

- Volwassen security-middleware (auth, CORS, rate limiting, data protection, antiforgery).
- EF Core-parametrisering voorkomt SQL-injectie; CodeQL ondersteunt C#.
- Managed identity en Key Vault-configuratieprovider standaard beschikbaar.

## Cost implications

- Draait op App Service Linux B1 (~€ 12/mnd), inclusief achtergrondverwerking.
- Geen licentiekosten (open source).
