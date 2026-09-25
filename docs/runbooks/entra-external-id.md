# Runbook – Entra External ID-tenant

> Status: v1.0 · 2026-09-25 · Hoort bij B-02 (één tenant) en B-04 (eigenaarschap), ADR-004 en ADR-014.
> Bevat **geen** wachtwoorden, secrets of subscription-ID's: deze repository is openbaar.

## 1. Gegevens

| | |
|---|---|
| Weergavenaam | De Vrolijke Drammers App |
| Type | External (CIAM) tenant |
| Domein | `vrolijkedrammersapp.onmicrosoft.com` |
| Inlogadres (authority) | `https://vrolijkedrammersapp.ciamlogin.com/` |
| Tenant-ID | `260db5a1-e5b6-4388-9f6c-d9b02cb5578b` (openbaar via OIDC-metadata) |
| Gegevenslocatie | Europe (land NL) |
| Facturering | MAU, gekoppeld aan de Azure-subscription van de vereniging (gratis tot 50.000 MAU) |
| Azure-resource | `Microsoft.AzureActiveDirectory/ciamDirectories/vrolijkedrammersapp` in resource group `rg-dvd-identity` (West Europe) |
| Aangemaakt | 2026-09-24, door robin.mom@vrolijkedrammers.nl |

De gewone (workforce) tenant van de vereniging, "Vrolijke Drammers" (`vrolijkedrammers.nl`), blijft los hiervan bestaan voor e-mail en interne zaken.

## 2. Beheerders

| Account | Rol | Doel |
|---|---|---|
| robin.mom@vrolijkedrammers.nl (gekoppeld vanuit de workforce-tenant) | Global Administrator | Dagelijks beheer |
| `bg-admin-01@vrolijkedrammersapp.onmicrosoft.com` | Global Administrator | Noodaccount 1 (break-glass) |
| `bg-admin-02@vrolijkedrammersapp.onmicrosoft.com` | Global Administrator | Noodaccount 2 (break-glass) |
| *tweede persoonlijke beheerder* | — | **Open actie (B-04):** nog aan te wijzen |

### Afspraken noodaccounts
- Alleen gebruiken als het gewone beheerdersaccount of de workforce-tenant niet beschikbaar is.
- Wachtwoorden: lang en uniek, door de beheerder zelf gezet (2026-09-24). Ze staan **nooit** in deze repository, in chatgesprekken of in tickets. Bewaring: gedeelde wachtwoordkluis van de vereniging, of verzegeld bij de voorzitter (account 1) en de secretaris (account 2).
- Worden uitgesloten van de Conditional Access-policy voor het beheerportal (fase 3), zodat een fout in die policy niet iedereen buitensluit.
- Elk gebruik geeft een melding (alert op aanmeldingen van `bg-admin-*`, in te richten in fase 7) en wordt achteraf gedocumenteerd.
- Controle elk kwartaal: kan er nog met beide accounts worden ingelogd? Zijn ze niet per ongeluk uitgeschakeld?

## 3. Hoe de tenant is aangemaakt (reproduceerbaar)

```bash
az account set --subscription <subscription-id-vereniging>
az provider register -n Microsoft.AzureActiveDirectory --wait
az group create -n rg-dvd-identity -l westeurope --tags project=vrolijkedrammers component=identity

# naam controleren
az rest --method post \
  --url "https://management.azure.com/subscriptions/<sub>/providers/Microsoft.AzureActiveDirectory/checkNameAvailability?api-version=2023-05-17-preview" \
  --body '{"name":"vrolijkedrammersapp","countryCode":"NL"}'

# tenant aanmaken (vereist een ingelogde gebruiker; een managed identity kan dit niet)
az rest --method put \
  --url "https://management.azure.com/subscriptions/<sub>/resourceGroups/rg-dvd-identity/providers/Microsoft.AzureActiveDirectory/ciamDirectories/vrolijkedrammersapp?api-version=2023-05-17-preview" \
  --body '{"location":"Europe","sku":{"name":"Standard","tier":"A0"},"properties":{"createTenantProperties":{"displayName":"De Vrolijke Drammers App","countryCode":"NL"}}}'
```

Bron: [CIAM Tenants – Create (Microsoft Learn)](https://learn.microsoft.com/en-us/rest/api/activedirectory/ciam-tenants/create?view=rest-activedirectory-2023-05-17-preview).

## 3a. Testaccounts (fase 1)

| Account | Toegang | Doel |
|---|---|---|
| "Test Tester" | Lid van `Testers`, `environmentAccess = dev,acc` | Moet kunnen inloggen op Dev/Acc |
| "Test Geen Toegang" | Geen toewijzing | Moet geweigerd worden (AADSTS50105) |

Beide accounts gebruiken e-mail met eenmalige code, op persoonlijke adressen van de beheerder (niet in deze repository). Logintest geslaagd op 2026-09-25. Toegang wijzigen gaat met `infra/entra/set-tester.sh`.

## 3b. Fase 3: provisioning, eerste beheerder en MFA

**Provisioning-app (per omgeving).** Via deze app maakt en blokkeert de API accounts via Graph (ADR-014). Er is geen secret nodig: de app vertrouwt via workload identity federation de managed identity van de API.
```bash
az login --tenant 260db5a1-e5b6-4388-9f6c-d9b02cb5578b --allow-no-subscriptions
MI=$(az webapp identity show -g rg-dvd-dev -n app-dvd-api-dev --query principalId -o tsv --subscription <sub>)
infra/entra/register-provisioning-app.sh dev "$MI" <tenant-id-vereniging>
```
Zet daarna `DVD_GRAPH_CLIENT_ID` in de GitHub environment. Na het opnieuw opbouwen van de API-app (nieuwe managed identity) draai je het script opnieuw. Werkt de federatie niet in de external tenant, dan is de terugvaloptie een certificaat in Key Vault (`Graph__CertificateName`).

**Eerste beheerder.** Beheerders worden normaal door een andere beheerder aangemaakt (`POST /api/v1/admin/users`). Voor de allereerste zet je in de GitHub environment de variabele `DVD_BOOTSTRAP_ADMIN` = `<oid>;<e-mail>;<naam>` van een bestaand account. Bij de volgende uitrol krijgt dat account de rol `beheerder-it`. De stap is idempotent en wordt geaudit. Haal de variabele daarna weer weg.

**MFA voor het beheerportal (handmatig, OQ-69).** Entra-beheercentrum → Protection → Conditional Access → New policy:
- Users: *All users*; exclude `bg-admin-01` en `bg-admin-02`;
- Target resources: de apps *DVD Beheerportal (dev/acc/prod)*;
- Grant: *Require multifactor authentication*;
- zet de policy eerst op *Report-only* en daarna op *On*.

Controleer vooraf in de prijsinformatie van External ID of hier kosten aan zitten (OQ-69). Als een echt token van het portal `amr = mfa` bevat, zet dan in Bicep `requirePortalMfa = true` (tweede slot in de API).

## 4. Nog in te richten (volgende fasen)

| Wat | Fase |
|---|---|
| App-registraties per omgeving (API, app, portal) + Dev/Acc op *Require user assignment*, groep `Testers`, custom attribuut `environmentAccess` — gescript in `infra/entra/` (zie [omgeving-opbouwen §3](omgeving-opbouwen.md#3-eenmalig-per-omgeving-entra-external-id)); het custom attribuut zelf is een handmatige stap | 1 |
| User flow met e-mail-OTP en zelfregistratie uit: basis in fase 1 (`register-apps.sh`); huisstijl en afronding | 3 |
| Conditional Access: MFA voor de portal-app, noodaccounts uitgesloten; kosten controleren (OQ-69) — zie §3b | 3 |
| Provisioning-app-registratie (Graph `User.ReadWrite.All`, federatie met de managed identity) — `register-provisioning-app.sh`, zie §3b | 3 |
| Aanmeldmeldingen voor `bg-admin-*` en auditlogs naar Log Analytics | 7 |
| Eigen inlogdomein (bijv. `login.vrolijkedrammers.nl`, OQ-67) | 7 |
