# Runbook – Omgeving opbouwen en opnieuw opbouwen (Dev/Acc)

> Status: v1.0 · 2026-09-25 · Hoort bij fase 1 ([15](../15-implementation-plan.md)), [08](../08-azure-infrastructure.md) en ADR-007.
> Bevat **geen** subscription-ID's, object-ID's of secrets: deze repository is openbaar. Die waarden staan als
> *variables* in de GitHub environments en worden door de scripts op het scherm getoond.

## 1. Wat er staat

| Resource group | Inhoud | Beheerd door |
|---|---|---|
| `rg-dvd-nonprod-shared` | App Service-plan `asp-dvd-nonprod` (B1, Linux, gedeeld door Dev en Acc); managed identities `id-dvd-github-dev`, `id-dvd-github-acc` (deploy) en `id-dvd-github-whatif` (alleen lezen, voor pull requests); budget | `infra/bootstrap` (beheerder, eenmalig) |
| `rg-dvd-dev`, `rg-dvd-acc` | API-app, SQL-server + database, Storage, Key Vault, Log Analytics + App Insights, Communication Services Email, budget. Het beheerportal draait in de API-app onder `/beheer` | `infra/main.bicep` (pipeline, bij elke uitrol) |
| `rg-dvd-identity` | External ID-tenant (zie [entra-external-id.md](entra-external-id.md)) | Handmatig |

Rechten (least privilege, docs/08 §4):
- **Deploy-identiteit per omgeving:** `Contributor` op de eigen resource group; `Role Based Access Control Administrator` op de eigen resource group, met een voorwaarde die alleen de vier rollen van de API-identiteit toestaat; `Website Contributor` op het gedeelde plan (om een app eraan te koppelen). Het federated credential accepteert alleen tokens van de GitHub environment met dezelfde naam. GitHub zet in het subject de eigenaar en repository **met hun ID's** (`repo:robinmom@37656265/vrolijkedrammers@1386066275:environment:dev`). Na het overdragen of hernoemen van de repository moet `DVD_GITHUB_REPOSITORY` worden aangepast en de bootstrap opnieuw worden gedraaid.
- **What-if-identiteit:** eigen rol *DVD Bicep What-If* (lezen + validate/what-if) op `rg-dvd-dev` en `rg-dvd-acc`; kan niets wijzigen.
- **SQL:** Entra-groep `sg-dvd-sql-admin-<env>` (beheerders + deploy-identiteit) is SQL-beheerder; SQL-logins zijn uitgeschakeld.
- **API (system-assigned managed identity):** `Key Vault Secrets User`, `Storage Blob Data Contributor`, `Storage Blob Delegator` en `Communication and Email Service Owner`, telkens alleen op de resource van de eigen omgeving.

**Regio:** `swedencentral`. West Europe accepteert geen nieuwe klanten voor deze subscription (`RequestDisallowedByAzure … not accepting new customers`, gecontroleerd op 2026-09-25). North Europe en Germany West Central hebben geen B1-quota. Het beheerportal wordt door de API-app geserveerd, zodat alles in de EU staat ([10-open-questions](../10-open-questions.md) OQ-75 en OQ-76).

## 2. Eenmalig: bootstrap (beheerder)

Vereisten: `Owner` op de subscription, Azure CLI, `jq`. Log in op de tenant van de vereniging, **niet** Giftnation:

```bash
az login --tenant <tenant-id-vereniging>
export AZURE_SUBSCRIPTION_ID=<subscription-id-vereniging>
export DVD_BUDGET_EMAIL=<e-mailadres penningmeester/beheer>   # optioneel, voor budgetmeldingen

infra/bootstrap/bootstrap-nonprod.sh --what-if   # eerst bekijken
infra/bootstrap/bootstrap-nonprod.sh             # daarna uitvoeren
```

Het script maakt de drie resource groups, het plan, de identiteiten met federatie, de rollen en de SQL-beheergroepen aan. Aan het eind toont het de waarden die in GitHub moeten worden ingevuld.

**GitHub** (repository → Settings):
1. *Environments* → maak `dev` en `acc` aan. Zet bij allebei *Deployment branches and tags* op **Selected: `main`**. Zet bij `acc` ook *Required reviewers* aan.
2. Vul per environment de *Environment variables* in die het script toont (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `DVD_APP_SERVICE_PLAN_ID`, `DVD_SQL_ADMIN_GROUP_OBJECT_ID`), eventueel ook `DVD_BUDGET_EMAIL`.
3. *Secrets and variables → Actions → Variables*: vul de repository-variabelen voor what-if in pull requests in.
4. Vanaf fase 3 per environment ook `DVD_PORTAL_CLIENT_ID`, `DVD_GRAPH_CLIENT_ID` (provisioning-app) en eenmalig `DVD_BOOTSTRAP_ADMIN` (zie [entra-external-id §3b](entra-external-id.md#3b-fase-3-provisioning-eerste-beheerder-en-mfa)).
5. Zet daarna de repository-variabele `DVD_DEPLOY_ENABLED` = `true`. Zonder die variabele slaat de workflow *Deploy* zichzelf over.

Er komen **geen** GitHub-secrets aan te pas: Azure-toegang loopt via OIDC.

## 3. Eenmalig per omgeving: Entra External ID

1. **Handmatig (één keer voor de hele tenant):** Entra-beheercentrum (tenant *De Vrolijke Drammers App*) → External Identities → *Custom user attributes* → *Add*: naam `environmentAccess`, type *String*. De Azure CLI heeft niet de Graph-rechten om dit zelf te doen.
2. Registreer de apps, na de eerste uitrol:
   ```bash
   az login --tenant 260db5a1-e5b6-4388-9f6c-d9b02cb5578b --allow-no-subscriptions
   DVD_PORTAL_URL=https://app-dvd-api-dev.azurewebsites.net/beheer/ infra/entra/register-apps.sh dev
   ```
   Dit maakt `DVD API (dev)`, `DVD Beheerportal (dev)` en `DVD App (dev)` aan, met *Require user assignment* en de groep `Testers`. Ook zet het de claims-mapping (`environmentAccess`) en de user flow *DVD aanmelden* klaar: e-mail met eenmalige code, zelfregistratie uit.
3. Neem `DVD_API_CLIENT_ID` en `DVD_ENVIRONMENT_ACCESS_CLAIM` over in de GitHub environment en start de uitrol opnieuw.
4. Een tester toegang geven of intrekken:
   ```bash
   infra/entra/set-tester.sh <object-id-gebruiker> dev,acc   # toegang
   infra/entra/set-tester.sh <object-id-gebruiker>           # intrekken
   ```

## 4. Uitrol

- **Dev:** automatisch bij elke merge naar `main` (workflow *Deploy*). Volgorde: what-if, infra, **databasemigraties** (idempotent script; een fout stopt de deploy) en de databasegebruiker van de API (rol `app_runtime`), dan API + portal (één pakket) en smoke-tests: portal op `/beheer/`, `/health/ready` = 200, anonieme blob-toegang geweigerd, SQL alleen met Entra-authenticatie.
- **Acc:** Actions → *Deploy* → *Run workflow* → `acc` (na goedkeuring door een reviewer).
- **Pull requests:** Bicep-lint en build altijd; what-if tegen Dev als de what-if-identiteit is ingericht.

## 5. Dev opnieuw opbouwen vanuit Bicep

> Getest op 2026-09-25: Dev verwijderd en met deze stappen + *Deploy* (`dev`) volledig hersteld. De pipeline maakt de databasegebruiker voor de nieuwe API-identiteit automatisch aan (`tools/Drammers.DbSetup`).

```bash
az group delete --name rg-dvd-dev --yes
# Key Vault blijft 90 dagen "soft deleted"; Dev heeft geen purge protection, dus definitief verwijderen kan:
az keyvault purge --name kv-dvd-dev --location swedencentral
infra/bootstrap/bootstrap-nonprod.sh      # maakt rg-dvd-dev en de pipelinerechten opnieuw aan
```
Start daarna *Deploy* (Run workflow → `dev`). De API krijgt een nieuwe managed identity; de deploystap *Database – API-identiteit als gebruiker* maakt de databasegebruiker automatisch opnieuw aan. Handmatig: Key Vault-secrets opnieuw zetten (vanaf fase 3, lijst in §6).

**Acc** heeft purge protection. Na verwijderen kan `kv-dvd-acc` niet worden gepurged, maar wel hersteld. Doe dat vóór de bootstrap: `az keyvault recover --name kv-dvd-acc`.

### Migraties handmatig bekijken

Het script dat de pipeline uitvoert, staat in het artefact `db` van elke Deploy-run (`migrations.sql`). Lokaal genereren:
`dotnet tool restore && dotnet ef migrations script --idempotent -p src/Drammers.Infrastructure -s src/Drammers.Infrastructure`.

## 6. Secrets

Secrets worden **nooit** via Bicep of pipeline-variabelen gezet. Een beheerder zet ze direct in Key Vault. Daarvoor heeft de beheerder tijdelijk de rol `Key Vault Secrets Officer` op de vault nodig:
```bash
az keyvault secret set --vault-name kv-dvd-dev --name <naam> --file <bestand>   # geen waarde op de command line
```
De lijst met secrets groeit per fase (e-Boekhouden-token, Expo-token, Mollie-key) en staat in [06-security](../06-security.md).

## 7. Tijdelijke SQL-toegang voor beheer

De SQL-firewall laat alleen de uitgaande IP's van de API-app toe. Voor onderhoud:
```bash
az sql server firewall-rule create -g rg-dvd-dev -s sql-dvd-dev -n tijdelijk-$(whoami) \
  --start-ip-address <eigen-ip> --end-ip-address <eigen-ip>
# … werk (inloggen met Entra, lid van sg-dvd-sql-admin-dev) …
az sql server firewall-rule delete -g rg-dvd-dev -s sql-dvd-dev -n tijdelijk-$(whoami)
```
Let op: de volgende uitrol laat handmatige regels staan, omdat ze niet door Bicep worden beheerd. Verwijder ze dus altijd zelf.

## 8. Kosten

Budgetten (met e-mail op 80 % werkelijk en 100 % verwacht): `rg-dvd-nonprod-shared` € 20, Dev € 25, Acc € 35 per maand. Het overzicht staat in Azure-portal → Cost Management → Budgets. De grootste post is het gedeelde B1-plan. SQL (free offer, pauzeert bij opgebruikte limiet), en Log Analytics (1 GB/dag cap) zijn in Dev/Acc nagenoeg gratis.
