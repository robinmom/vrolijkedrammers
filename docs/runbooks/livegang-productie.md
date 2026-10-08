# Runbook — Livegang Productie (fase 7)

Besluiten (product owner, 2026-10-06):

- Productie krijgt een **volledige kopie van Dev**: database (website, nieuws, agenda, leden, accounts, adverteerders,
  mailing, verkoop) en bestanden (foto's, documenten). Iedereen kan direct inloggen.
- Alles op **www.vrolijkedrammers.nl**: website op `/`, beheerportal op `/beheer`, app-API op `/api`.
  `vrolijkedrammers.nl` stuurt door naar www (301). Certificaten zijn gratis (App Service Managed Certificates).
- Voor Apple komt er een **nieuwe productie-build**; de huidige review (op Dev) wordt ingetrokken.
- Na goedkeuring van die build gaat **Dev naar het gratis F1-plan**.
- **e-Boekhouden**: vlak vóór de kopie nog één ledensync in Dev; daarna is **onze eigen database leidend**. Productie
  heeft geen koppeling meer (`EBoekhouden:Enabled=false`, geen token, nachtelijke sync uit). Nieuwe leden via "lid
  worden" krijgen het volgende eigen lidnummer.

Wie: **Jij** = product owner/beheerder (Owner op de subscription, DNS, Apple, geheimen). **Claude** = voorbereiding,
controles, EAS-builds en hulp bij elke stap. Geheimen gaan nooit via Claude.

## 0. Vandaag al doen (DNS heeft tijd nodig)

1. **TTL verlagen** bij je DNS-beheerder voor `vrolijkedrammers.nl` en `www` naar 300 seconden (5 minuten), zodat de
   omzetting straks snel doorkomt en terugdraaien kan.
2. **MX-, SPF- en andere mailrecords niet aanraken.** De mailboxen (secretaris@, voorzitter@ …) blijven werken; alleen
   de website-records veranderen.
3. **Content-freeze afspreken**: vanaf het moment van kopiëren (stap 3) worden wijzigingen in het Dev-portal niet meer
   meegenomen. Laat redacteuren dan niets meer in Dev doen.

## 1. Voorbereiding (eenmalig, vóór de livegangdag)

1. PR `fase-07-productie` mergen (Dev wordt daarmee ook uitgerold; geen functionele wijziging behalve `noindex` buiten
   Productie).
2. **GitHub → Settings → Environments → New environment `prod`** met *Required reviewers* = jij. Zo kan Productie
   alleen worden uitgerold na jouw goedkeuring.
3. **Bootstrap** (Owner, tenant van de subscription, niet Giftnation):

   ```bash
   az login --tenant <tenant-id-vereniging>
   AZURE_SUBSCRIPTION_ID=<id> infra/bootstrap/bootstrap-prod.sh --what-if   # controleren
   AZURE_SUBSCRIPTION_ID=<id> infra/bootstrap/bootstrap-prod.sh
   ```

   Zet de getoonde waarden als *Environment variables* in environment `prod`.
4. **Entra-app-registraties Prod** (External ID-tenant):

   ```bash
   az login --tenant 260db5a1-e5b6-4388-9f6c-d9b02cb5578b --allow-no-subscriptions
   DVD_PORTAL_URL=https://www.vrolijkedrammers.nl/beheer/,https://app-dvd-api-prod.azurewebsites.net/beheer/ \
     infra/entra/register-apps.sh prod
   infra/entra/register-provisioning-app.sh prod
   infra/entra/provisioning-certificate.sh prod
   ```

   Zet `DVD_API_CLIENT_ID`, `DVD_PORTAL_CLIENT_ID`, `DVD_MOBILE_CLIENT_ID`, `DVD_GRAPH_CLIENT_ID` en
   `DVD_GRAPH_CERTIFICATE_NAME` in environment `prod`. Het certificaat komt in `kv-dvd-prod` (die bestaat na stap 2.1;
   draai `provisioning-certificate.sh prod` dus na de eerste infra-uitrol als hij nog niet bestaat).
5. Overige variabelen in `prod`: `DVD_PUSH_PROVIDER=Expo`, `DVD_TURNSTILE_SITE_KEY` (zelfde widget; voeg in Cloudflare
   de hostnamen `www.vrolijkedrammers.nl` en `vrolijkedrammers.nl` toe), `DVD_BUDGET_EMAIL`. `DVD_BOOTSTRAP_ADMIN` is
   niet nodig: de beheerders komen mee in de kopie. **`DVD_CUSTOM_HOSTNAMES` nog niet zetten** (pas in stap 5).
6. **Variabele `DVD_DEPLOY_ENABLED=true`** staat al op repository-niveau; niets doen.

## 2. Livegangdag — infrastructuur

1. **Actions → Deploy → Run workflow**: omgeving `prod`, **Alleen infrastructuur aan**. Keur de uitrol goed.
   De samenvatting van de run toont het TXT-record (`asuid`) voor stap 5.
2. Als het provisioning-certificaat nog niet bestond: nu `infra/entra/provisioning-certificate.sh prod`.

## 3. Livegangdag — gegevens kopiëren (content-freeze begint)

1. **Laatste ledensync in Dev**: portal → Ledensync → eerst een proefrun bekijken, dan de echte run. Conflicten
   afhandelen. Daarna niets meer in e-Boekhouden wijzigen voor de vereniging: vanaf nu is het portal leidend.
2. Kopiëren:

```bash
az login --tenant <tenant-id-vereniging>
AZURE_SUBSCRIPTION_ID=<id> infra/prod/migrate-dev-to-prod.sh
infra/prod/set-secret.sh prod mollie-api-key      # live_… (Mollie-dashboard → Developers → API keys)
```

Het script: database (BACPAC Dev → Prod), opruimen (Dev-databasegebruiker, openstaande outbox-berichten, nachtelijke
ledensync uit), bestanden, sleutelring (opnieuw versleuteld voor Prod) en geheimen (behalve Mollie en e-Boekhouden). Het geeft jou tijdelijk de benodigde rollen en
firewalltoegang en ruimt die daarna op. Duur: enkele minuten.

Gaat het halverwege mis nadat de database al is gekopieerd: `migrate-dev-to-prod.sh --from <2-5>` gaat verder bij die
stap.

3. **Testgegevens weghalen** (besluit 2026-10-09): adverteerders (alles), alle bestellingen (dagkaarten, pronkzitting,
   activiteiten, munten; de producten blijven), ledentickets en alle optochtinschrijvingen (de optocht en categorieën
   blijven). Eerst een proefrun met aantallen, dan vastleggen:

   ```bash
   AZURE_SUBSCRIPTION_ID=<id> infra/prod/reset-testdata.sh            # proefrun: niets gewijzigd
   AZURE_SUBSCRIPTION_ID=<id> infra/prod/reset-testdata.sh --apply    # vastleggen (vraagt om bevestiging)
   ```

## 4. Livegangdag — app uitrollen en testen op het azurewebsites-adres

1. **Deploy → Run workflow**: `prod`, *Alleen infrastructuur* **uit**. Keur goed. Migraties zijn een no-op, de
   API-identiteit van Prod krijgt databasetoegang, smoke-tests draaien.
2. Testen op `https://app-dvd-api-prod.azurewebsites.net`:
   - website: home, nieuws, agenda, prinsengalerie, foto's, pagina's uit het menu — gelijk aan Dev;
   - `/beheer`: inloggen, leden, adverteerders (IBAN zichtbaar bij een adverteerder = sleutelring werkt);
   - `/robots.txt` toont de sitemap (Productie mag in zoekmachines).

## 5. Livegangdag — domein koppelen

1. Bij de DNS-beheerder (waarden uit de samenvatting van stap 2.1 en het IP-adres hieronder):

   | Type  | Naam                | Waarde                                        |
   |-------|---------------------|-----------------------------------------------|
   | TXT   | `asuid.www`         | customDomainVerificationId                    |
   | TXT   | `asuid`             | customDomainVerificationId                    |
   | CNAME | `www`               | `app-dvd-api-prod.azurewebsites.net`          |
   | A     | `@` (kale domein)   | inkomend IP van de app (zie hieronder)        |

   Inkomend IP: `az webapp show -g rg-dvd-prod -n app-dvd-api-prod --query inboundIpAddress -o tsv`
   (of `nslookup app-dvd-api-prod.azurewebsites.net`). Verwijder het oude A-/CNAME-record van WordPress;
   MX/SPF blijven staan.
2. Wacht tot `nslookup www.vrolijkedrammers.nl` naar Azure wijst (meestal < 15 min bij TTL 300).
3. Variabele in `prod`: `DVD_CUSTOM_HOSTNAMES=www.vrolijkedrammers.nl,vrolijkedrammers.nl` en opnieuw **Deploy** `prod`.
   Bicep koppelt beide namen, vraagt de certificaten aan en zet `Sales:PublicBaseUrl` (links in e-mails, afmeldlinks)
   en de doorverwijzing naar www.
4. Controleren: `https://www.vrolijkedrammers.nl` (slot in de browser), `https://vrolijkedrammers.nl/nieuws` → 301 naar
   www, inloggen op `/beheer`, contactformulier (Turnstile), optocht-inschrijven (inloggen).

## 6. App-stores

Claude doet, na jouw akkoord:

```bash
cd apps/mobile
npx eas-cli env:create production --name EXPO_PUBLIC_API_URL --value https://www.vrolijkedrammers.nl --visibility plaintext
npx eas-cli build --platform ios --profile production --auto-submit-with-profile production
npx eas-cli build --platform android --profile production
```

Jij: `GOOGLE_SERVICES_JSON` ook in EAS-omgeving *production* zetten (expo.dev → Project → Environment variables;
geheim, gaat niet via Claude). In App Store Connect de huidige review **intrekken** en de nieuwe build indienen;
reviewaccount `app@vrolijkedrammers.nl` werkt in Prod zonder Testers-groep. Google Play: zodra het account is
geverifieerd (aparte stappen).

## 7. Na goedkeuring van de Prod-build — Dev afschalen

1. GitHub-variabele in environment `dev`: `DVD_ALWAYS_ON=false`.
2. `AZURE_SUBSCRIPTION_ID=<id> DVD_PLAN_SKU=F1 infra/bootstrap/bootstrap-nonprod.sh` (plan naar het gratis F1).
3. **Deploy** `dev` (zet Always On uit; F1 kent dat niet).
4. In het Dev-portal de nachtelijke ledensync uitzetten (Dev blijft een testkopie).

Dev blijft bereikbaar (trager, dagelijkse rekenlimiet); de database pauzeert zelf. Terug naar B1: `DVD_PLAN_SKU=B1` en
`DVD_ALWAYS_ON=true`.

## Acc opheffen (besluit 2026-10-07)

Er zijn alleen nog Dev en Prod. Dev blijft de ontwikkelomgeving op het gratis F1-plan, zonder eigen domein (F1
ondersteunt geen eigen domeinen), bereikbaar op het azurewebsites-adres. Acc opruimen kan op elk moment:

```bash
AZURE_SUBSCRIPTION_ID=<id> infra/bootstrap/remove-acc.sh      # Azure: rg-dvd-acc, identiteit, SQL-beheergroep
infra/bootstrap/remove-acc.sh --entra                           # app-registraties "DVD … (acc)"
```

Daarna in GitHub de environment `acc` verwijderen.

## Terugdraaien

- **Website/domein**: DNS terugzetten naar de oude WordPress-records (TTL 300 → binnen minuten).
- **App/API**: vorige versie opnieuw uitrollen via **Deploy** `prod` op een eerdere commit.
- **Gegevens**: Dev is ongewijzigd en blijft de bron tot de content-freeze wordt opgeheven; Prod-database heeft
  point-in-time-restore.

## Na de livegang (fase 7, later)

Privacyverklaring bijwerken en linken (OQ-50), alerts en availability test, restore-oefening, eigen e-maildomein in
Azure Communication Services (daarna de verzendlimiet laten verhogen), Google Play, generale repetitie (fase 18).
