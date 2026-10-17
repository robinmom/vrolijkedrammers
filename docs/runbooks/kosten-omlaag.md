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

## 3. Dev naar het gratis F1-plan (na stap 2)

1. Zet in GitHub de environment-variabele `DVD_ALWAYS_ON=false` voor **dev** (F1 kent geen Always On). Staat die al,
   dan is dit klaar.
2. Schaal het plan af:

   ```
   AZURE_SUBSCRIPTION_ID=<id> DVD_PLAN_SKU=F1 infra/bootstrap/bootstrap-nonprod.sh
   ```

3. Start **Deploy → dev**.

F1 geeft 60 CPU-minuten per dag en laat de app slapen als er niemand is. De eerste keer openen duurt dan even. Terug
naar B1: `DVD_PLAN_SKU=B1`.
