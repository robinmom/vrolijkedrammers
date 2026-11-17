# Runbook: Azure-kosten omlaag (2026-10-17)

**Aanleiding.** De SQL-databases waren serverless. Je betaalt per seconde dat ze wakker zijn.

- Prod is door de achtergrondtaken de hele dag wakker, wat neerkomt op ± € 200 per maand.
- Dev kostte in september/oktober € 68.
- Beide databases zijn klein: 80 MB en 64 MB.

**Na deze stappen kost het ongeveer:**

| Onderdeel | Was | Wordt |
| --- | --- | --- |
| Prod-database | serverless, ± € 200 per maand | Standard S1, ± € 25 per maand |
| Dev-database | serverless, ± € 55 per maand | Basic, ± € 4 per maand |
| Dev App Service | B1, gedeeld met Acc, ± € 12 per maand | F1, gratis |
| Acc | | opgeheven |

## 1. Databases (code, via de Deploy)

1. Merge de PR `kosten-omlaag`. De automatische deploy naar **Dev** zet de Dev-database op Basic.
2. Controleer dat Dev werkt: open het portal of de app op Dev.
3. Start 's avonds **Deploy → prod**, met "Alleen infrastructuur" uit. De Prod-database gaat naar S1. Het omzetten
   duurt een paar minuten en gaat online; de verbinding kan heel even wegvallen.

Wordt S1 ooit te krap (de app wordt traag bij drukte), dan zet je in `infra/env/prod.bicepparam`
`sqlSkuName = 'S2'` en deploy je opnieuw.

## 2. Acc opheffen (beheerder)

```
az login --tenant <tenant-id-vereniging>
AZURE_SUBSCRIPTION_ID=<id> infra/bootstrap/remove-acc.sh
az login --tenant 260db5a1-e5b6-4388-9f6c-d9b02cb5578b --allow-no-subscriptions
infra/bootstrap/remove-acc.sh --entra
```

Daarna in GitHub: **Settings → Environments → acc → verwijderen**.

## 3. Dev naar het gratis F1-plan — gedaan op 18-11-2026

F1 geeft 60 CPU-minuten per dag en laat de app slapen als er niemand is. De eerste keer openen duurt dan even. Dit is
de stand; `infra/bootstrap/nonprod.bicep` heeft `planSku = 'F1'` als standaard. Tijdelijk terug naar B1:

```
AZURE_SUBSCRIPTION_ID=<id> DVD_PLAN_SKU=B1 infra/bootstrap/bootstrap-nonprod.sh
```

Always On staat in Dev al uit: de workflow geeft `DVD_ALWAYS_ON` altijd mee en een lege waarde betekent uit.
`healthCheckPath` mag blijven staan — F1 accepteert de instelling en doet er niets mee.

### Valkuil: 1 GB schijf voor de uitrolpakketten

Het afschalen weigerde eerst met *"Storage usage quota exceeded. Your current usage is 1368 Mb and quota after the
operation will be 1024 Mb"*. App Service bewaart de laatste uitrolpakketten in `/home/data/SitePackages`, en die
waren **267 MB per stuk**. Vijf pakketten pasten niet in de 1 GB van F1.

Oorzaak van die 267 MB: `dotnet publish` zonder platform levert de native bestanden van SkiaSharp (via QuestPDF) voor
22 platforms mee, inclusief 285 MB Windows-debugsymbolen (`libSkiaSharp.pdb`). De Deploy publiceert daarom sinds
18-11-2026 met `-r linux-x64 --self-contained false`: **683 MB wordt 118 MB**, en het pakket op de schijf ongeveer
45 MB. Dezelfde winst geldt voor Prod en voor de doorlooptijd van elke uitrol.

Zo kijk je hoeveel er staat (en ruim je op; de SCM-basisauthenticatie staat uit, dus met een Entra-token):

```
az rest --method get --resource https://management.azure.com/ \
  --url "https://app-dvd-api-dev.scm.azurewebsites.net/api/vfs/data/SitePackages/"
az rest --method get --resource https://management.azure.com/ \
  --url "https://app-dvd-api-dev.scm.azurewebsites.net/api/vfs/data/SitePackages/packagename.txt"   # het actieve
az rest --method delete --resource https://management.azure.com/ --headers "If-Match=*" \
  --url "https://app-dvd-api-dev.scm.azurewebsites.net/api/vfs/data/SitePackages/<oud>.zip"
```

## 4. Minder uitrollen naar Dev (18-11-2026)

De Deploy liep bij elke merge op `main`. Dat kost geen geld (openbare repository: GitHub Actions is gratis), maar wel
een kwartier per merge. De `push`-trigger heeft nu een padfilter: alleen `src/`, `tools/`, `packages/`,
`apps/admin/`, `infra/`, `openapi/`, de build-bestanden in de hoofdmap en `deploy.yml` zelf rollen uit. Een merge met
alleen documentatie, de mobiele app of tests niet — die rol je desnoods handmatig uit met **Actions → Deploy →
omgeving dev**.

## Stand van de rekening (november 2026)

| Omgeving | Per maand |
| --- | --- |
| Prod (B1-plan, SQL S1, mail, opslag) | ± € 36 |
| Dev (F1-plan gratis, SQL Basic, opslag) | ± € 4 |
| **Totaal** | **± € 40** |

Oktober was nog € 152 in totaal (Dev alleen € 67). Het budgetalert staat op € 40 voor Prod en € 25 voor Dev.
