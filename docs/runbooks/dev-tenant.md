# Runbook – Eigen External ID-tenant voor Dev

> Status: v1.0 · 2026-10-17 · Oplossing voor bevinding H1 uit het beveiligingsonderzoek van oktober 2026.
> Bevat **geen** secrets of subscription-ID's: deze repository is openbaar.

## Waarom

Dev en prod gebruikten dezelfde External ID-tenant. De provisioning-app van Dev ("DVD Provisioning (dev)") heeft daar
de Graph-rechten `User.ReadWrite.All` en `GroupMember.ReadWrite.All`, en na `wachtwoord-inloggen.sh dev` ook
`User-PasswordProfile.ReadWrite.All` en `User.ManageIdentities.All`. Die rechten gelden voor de hele tenant, dus ook
voor de accounts van alle leden. Microsoft kan application permissions niet beperken tot een deel van de gebruikers.
Een gelekt certificaat uit `kv-dvd-dev` (een omgeving met lossere toegang) gaf daardoor macht over prod-accounts.

Met een eigen tenant voor Dev heeft niets van Dev nog rechten op echte accounts. Een tenant is gratis tot 50.000 MAU.

| | Prod | Dev |
|---|---|---|
| Tenant | `vrolijkedrammersapp.onmicrosoft.com` | `vrolijkedrammersdev.onmicrosoft.com` |
| Authority | `https://vrolijkedrammersapp.ciamlogin.com/<prod-id>/v2.0` | `https://vrolijkedrammersdev.ciamlogin.com/<dev-id>/v2.0` |
| Accounts | Leden en bestuur | Alleen testers (eigen inlog per tester) |

## Vooraf

- Dev bevat alleen testgegevens: `infra/dev/anonimiseer-dev.sh --apply` is gedraaid (branch `dev-testgegevens`).
  Accounts met een handmatige rol (bestuur, beheer, testers) houden hun e-mailadres, zodat de koppeling hieronder werkt.
- De app, het portal en de website hoeven niet opnieuw gebouwd te worden: ze halen de inlogconfiguratie bij de API.

## Stappen

**1. Tenant aanmaken** (tenant van de vereniging, rol Owner of Contributor op de subscription)
```bash
az login
AZURE_SUBSCRIPTION_ID=<sub> infra/entra/dev-tenant-aanmaken.sh
```
Het script toont het tenant-ID. Wie het script draait, wordt Global Administrator van de nieuwe tenant.

**2. Custom attribuut** (handmatig, eenmalig). Ga in het Entra-beheercentrum naar de nieuwe tenant → External
Identities → Custom user attributes → Add: naam `environmentAccess`, type String.

**3. App-registraties en provisioning**
```bash
export DVD_DEV_CIAM_TENANT_ID=<dev-tenant-id>
az login --tenant "$DVD_DEV_CIAM_TENANT_ID" --allow-no-subscriptions
DVD_PORTAL_URL=<dezelfde portal-URL('s) als nu voor Dev> infra/entra/register-apps.sh dev
infra/entra/register-provisioning-app.sh dev
infra/entra/wachtwoord-inloggen.sh dev        # alleen als Dev met wachtwoorden test
# tijdelijk: rol "Key Vault Certificates Officer" op kv-dvd-dev voor de beheerder
AZURE_SUBSCRIPTION_ID=<sub> infra/entra/provisioning-certificate.sh dev
```
Elk script meldt bovenaan welke tenant het gebruikt (`Tenant: vrolijkedrammersdev.onmicrosoft.com …`). Staat daar
`vrolijkedrammersapp`, dan is `DVD_DEV_CIAM_TENANT_ID` niet gezet: stop en zet de variabele eerst.
Haal de tijdelijke Key Vault-rol daarna weer weg.

**4. GitHub-variabelen** (Settings → Environments → `dev`)

| Variabele | Waarde |
|---|---|
| `DVD_EXTERNAL_ID_TENANT_ID` | tenant-ID uit stap 1 (nieuw) |
| `DVD_EXTERNAL_ID_TENANT_NAME` | `vrolijkedrammersdev` (nieuw) |
| `DVD_API_CLIENT_ID`, `DVD_PORTAL_CLIENT_ID`, `DVD_MOBILE_CLIENT_ID` | uitvoer van `register-apps.sh` |
| `DVD_GRAPH_CLIENT_ID`, `DVD_TESTERS_GROUP_ID`, `DVD_ENVIRONMENT_ACCESS_ATTRIBUTE` | uitvoer van `register-provisioning-app.sh` |

Draai daarna Deploy voor Dev.

**5. Testers toegang geven en testen.** Elke tester maakt één keer een inlog aan in de nieuwe tenant (inloggen op het
Dev-portal, dan "Geen account? Maak er een aan" met e-mail + code). De eerste poging wordt geweigerd omdat de tester
nog niet in de groep Testers zit. Heb je in stap 3 `wachtwoord-inloggen.sh dev` gedraaid, dan kan een tester zelf geen
inlog maken; maak die dan met `infra/entra/set-password-login.sh <e-mailadres> dev` (de tester kiest het wachtwoord via
"Wachtwoord vergeten?"). Daarna:
```bash
infra/entra/set-tester.sh <e-mailadres> dev
```
Bij de volgende aanmelding koppelt de API de nieuwe inlog op e-mailadres aan het bestaande Dev-account
(`AccountLinker`), inclusief rollen. Begin met je eigen account; test daarna:
- het portal (inloggen, een account aanmaken: dat komt nu in de Dev-tenant terecht);
- de app via een dev-build of Expo Go;
- inloggen op de Dev-website.

**6. Oude Dev-apps verwijderen uit de gedeelde tenant.** Pas na een geslaagde test. Dit is het moment waarop H1
echt is opgelost.
```bash
az login --tenant 260db5a1-e5b6-4388-9f6c-d9b02cb5578b --allow-no-subscriptions
DVD_DEV_CIAM_TENANT_ID=<dev-tenant-id> infra/entra/oude-dev-apps-opruimen.sh
```
Het script vraagt om bevestiging. Verwijderde apps zijn 30 dagen terug te zetten (Entra → App registrations →
Deleted applications).

**7. Tenant-ID vastleggen.** Zet het Dev-tenant-ID als standaardwaarde in `infra/entra/lib.sh`
(`DEV_CIAM_TENANT_ID`), zodat `DVD_DEV_CIAM_TENANT_ID` niet meer nodig is.

## Terugdraaien

Voor stap 6 kan het altijd: maak de GitHub-variabelen uit stap 4 weer leeg (de client-ID's terug naar de oude
waarden) en deploy. Dev gebruikt dan weer de gedeelde tenant. Na stap 6 eerst de apps terugzetten uit Deleted
applications.
