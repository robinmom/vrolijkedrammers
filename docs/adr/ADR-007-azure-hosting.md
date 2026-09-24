# ADR-007: Azure-hosting

- **Status**: Geaccepteerd · 2026-09-24 (achtergrondverwerking in de API-app; besluit **B-01** in [10](../10-open-questions.md#b-01--hosting-van-achtergrondverwerking))
- **Wijziging t.o.v. v0.1**: Azure Functions (Linux Consumption) vervangen door hosted background services in de API, omdat Linux Consumption geen .NET 10 ondersteunt en in 2028 wordt uitgefaseerd.

## Context

De backend draait volledig in Azure, bij voorkeur als PaaS zonder VM's. De kosten moeten laag blijven; de belasting is laag, met een piek rond carnaval. Onderdelen: API, achtergrondtaken (sync, planning, push, imports, beeldverwerking), beheerportal (SPA), database, bestanden, secrets en monitoring.

Geverifieerd (Microsoft Learn, "Supported languages in Azure Functions", bijgewerkt 2026-09-17): *".NET 9 is the last .NET version supported for Linux Consumption plan apps. Newer .NET versions aren't added to Linux Consumption."* en *"The option to host function apps on Linux in a Consumption plan is retiring on 30 September 2028."* .NET 10 (isolated) is GA op andere plannen, waaronder Flex Consumption.

## Options considered

| Onderdeel | Opties |
|---|---|
| API | **App Service (Linux)** · Azure Container Apps · AKS · VM |
| Achtergrond | **Hosted services in de API (App Service)** · Functions Flex Consumption · Functions Windows Consumption · WebJobs · Container Apps Jobs · ~~Functions Linux Consumption~~ (geen .NET 10) |
| Portal | **Static Web Apps** · App Service · Blob static website + CDN |
| Edge | Front Door (+WAF) · geen |
| API-gateway | API Management · geen |

Afweging API:

| | App Service B1 | Container Apps (consumption) |
|---|---|---|
| Kosten | ~€ 12/mnd vast | Scale-to-zero, bij continu gebruik vergelijkbaar of hoger; cold starts |
| Eenvoud | Code-deploy (zip), geen containers | Container registry + images |
| Features | Managed certs, custom domains, health checks, autoscale (vanaf S1), slots (vanaf S1) | Revisions, KEDA-scaling, Dapr |
| Always-on | Ja (B1+) | Min replicas 1 kost geld |

Afweging achtergrond: zie B-01 in het besluitenregister (hosted services, Flex Consumption, Windows Consumption, .NET 9).

## Decision

- **API**: Azure App Service (Linux, .NET 10), plan **B1** in Prod met "Always On"; tijdelijk opschalen naar S1/P0v3 in de carnavalsmaand (autoscale en slot-swaps beschikbaar). Dev en Acc delen één B1-plan.
- **Achtergrondtaken**: **hosted background services in hetzelfde App Service-proces** (projectbibliotheek `Drammers.Worker`):
  - **Queue**: DB-tabellen (`notification.Outbox`, `import.ImportJob`, `content.MediaJob`), gepolld door `BackgroundService`-workers (claimen met `UPDATE … OUTPUT` + `READPAST`), idempotent en hervatbaar.
  - **Scheduler**: tijdgestuurde jobs (e-Boekhouden-sync 03:00, geplande publicatie elke minuut, Expo-receipts, retentie) via een lichte scheduler (bijv. Coravel of een eigen `PeriodicTimer`) met **`sp_getapplock`** als distributed lock, zodat er bij scale-out maar één instantie draait.
  - **Workload-isolatie**: beeldverwerking met begrensde parallelliteit (1 tegelijk) en lage prioriteit, om API-latency te beschermen.
- **Beheerportal**: Azure Static Web Apps (Free, eventueel Standard).
- **Geen** Front Door, API Management, AKS, VM's of Functions in de MVP.

## Reasoning

- De eenvoudigste PaaS die aan alle eisen voldoet, met voorspelbare lage kosten.
- Eén deployable, één runtime en één identiteit is het best te onderhouden door vrijwilligers.
- App Service heeft vaste outbound-IP's, dus de SQL-firewall kan strikt blijven (geen "Allow Azure services").
- Het achtergrondvolume is klein (± 500 leden-sync per nacht, enkele honderden pushes per verzending, tientallen foto's per album).
- Front Door/APIM voegen kosten en complexiteit toe zonder duidelijke winst bij deze schaal: rate limiting, OpenAPI en auth zitten in de API.

## Consequences

- Deploy via GitHub Actions (zip deploy / `azure/webapps-deploy`), OIDC-federatie (of Azure DevOps, zie B-03).
- Geen deployment slots op B1 → korte herstart bij een deploy (acceptabel buiten carnaval; tijdens carnaval change freeze). Bij S1 wel slot-swaps. Jobs die door een herstart worden onderbroken, worden bij de volgende start hervat (idempotent).
- `Drammers.Worker` heeft geen afhankelijkheid van ASP.NET-specifieke code; het kan later als los proces (tweede App Service/WebJob of Functions Flex) draaien als de belasting dat vraagt.
- Health endpoint `/health/ready` rapporteert ook de achtergrondstatus (laatste succesvolle sync, lengte van de outbox).
- Heroverwegen van Container Apps of Functions Flex als er meerdere services of zware workloads ontstaan.

## Security implications

- HTTPS-only, TLS 1.2+, FTP/basic-auth-publishing uit, managed identity, Key Vault-references in App Settings.
- Eén managed identity voor API en worker: het e-Boekhouden-token en de Expo-token zijn door hetzelfde proces leesbaar. Mitigatie: aparte secrets, code-review, en optioneel een user-assigned MI voor de sync-DB-verbinding.
- Minder aanvalsoppervlak dan met Functions (geen extra storage-account, geen "Allow Azure services" op SQL).
- Geen WAF in de MVP; basisbescherming via Azure-platform-DDoS, rate limiting en auth. Front Door WAF is een optie bij misbruik.
- Static Web Apps: CSP en security headers via `staticwebapp.config.json`.

## Cost implications

- Prod: App Service B1 ~€ 12 + tijdelijke opschaling ~€ 45 in februari; achtergrondverwerking € 0 extra; SWA € 0–9.
- Afgewezen opties: Functions Flex (~€ 0 compute, maar + storage-account + eventueel VNet/NAT voor vaste IP's); Front Door Standard ~€ 30+/mnd; APIM Developer ~€ 45/mnd (en niet voor productie), Basic ~€ 130/mnd.
