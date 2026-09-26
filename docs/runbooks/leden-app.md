# Runbook – Leden-app: inloggen, accounts en apparaten (fase 9a)

> Status: v1.0 · 2026-09-26 · Hoort bij ADR-014 (alleen leden krijgen een account) en fase 9 in [15-implementation-plan](../15-implementation-plan.md).
> Bevat **geen** secrets: client-ID's en tenant-ID's zijn openbaar (ze staan in elke inlogpagina).

## 1. Hoe het werkt

| Onderdeel | Werking |
|---|---|
| Account aanvragen ("Ik ben al lid") | Lidnummer + e-mailadres in de app. Klopt het exact met e-Boekhouden (actief lid, nog geen account), dan maakt de worker het account (rol *Carnavalist*) en stuurt een welkomstmail. Anders komt het verzoek in **Beheerportal → Accountverzoeken**. De aanvrager ziet altijd de melding "in behandeling". |
| Eerste keer inloggen | Het lid maakt op de inlogpagina zelf een inlog met hetzelfde e-mailadres ("Maak er een") en een code per e-mail. De API koppelt die inlog bij de eerste aanroep aan het goedgekeurde account (ADR-014, herzien 2026-09-27). Wie niet is goedgekeurd, komt niet verder dan "geen account". |
| Account vanuit het portal | **Leden → lid → App-account aanmaken** (recht `member.approve`); het account krijgt het e-mailadres uit e-Boekhouden. |
| Inloggen | App → Meer → Inloggen: inlogpagina van Entra External ID met een e-mailcode (geen wachtwoord). Het refresh-token staat in de Keychain/Keystore. Beheerders loggen op dezelfde manier in op het portal. |
| Apparaten | Elke aanmelding registreert de installatie. Een afgemeld apparaat (door het lid of via **Gebruikers → gebruiker → Apparaten**) krijgt 401 `DEVICE_REVOKED` en logt uit. |
| Account verwijderen | App → Mijn gegevens → Account verwijderen: lokaal account geanonimiseerd, Entra-account verwijderd. Het lid in e-Boekhouden blijft. |
| AVG-export | App → Mijn gegevens → Mijn gegevens downloaden: JSON met alle gegevens, 24 uur te downloaden; na 2 dagen opgeruimd (lifecycle-regel). |
| Welkomstmail | Azure Communication Services, afzender `DoNotReply@…azurecomm.net` (eigen domein in fase 7). Legt uit: eerste keer "Maak er een" met dit e-mailadres, daarna steeds een code. **De tekst is een concept; het bestuur keurt hem goed** (`MemberAccounts.WelcomeMail`). |

## 2. Eenmalig inrichten per omgeving (door de beheerder)

1. **Redirect-URI's van de app bijwerken** (voegt in Dev/Acc de doorstuurpagina voor Expo Go toe):
   ```bash
   az login --tenant 260db5a1-e5b6-4388-9f6c-d9b02cb5578b --allow-no-subscriptions
   DVD_PORTAL_URL=https://app-dvd-api-dev.azurewebsites.net/beheer/ infra/entra/register-apps.sh dev
   ```
2. **GitHub → Settings → Environments → dev → Environment variables:** `DVD_MOBILE_CLIENT_ID` = de waarde die het script toont bij "DVD App (dev)".
3. **Deploy opnieuw draaien** (Actions → Deploy → Run workflow), zodat `Auth__MobileClientId` en `Auth__MobileRedirectBridge` in de app-instellingen komen.
4. Controle: `https://app-dvd-api-dev.azurewebsites.net/api/v1/app-auth-config` toont een `clientId` en een `redirectBridgeUrl`.

De Graph-provisioning (fase 3) en Azure Communication Services (fase 1) zijn al ingericht; er is geen extra recht nodig.

### Eenmalig: user flow bijwerken (zelf een inlog maken met e-mail + code)

Na deze wijziging: `infra/entra/register-apps.sh dev` opnieuw draaien (stap 1 hierboven). Controle in het Entra-beheercentrum → External Identities → User flows → *DVD aanmelden* → Properties: "Sign up" staat aan, alleen *Email one-time passcode*.

### Overstappen van een inlog met wachtwoord (accounts van vóór 2026-09-27)

Accounts die de API met Graph heeft aangemaakt, vragen om een wachtwoord. Overstappen, per persoon:
1. Entra-beheercentrum (tenant *De Vrolijke Drammers App*) → Users → de gebruiker → **Delete**. Alleen de inlog verdwijnt; rollen en lidkoppeling staan in de app-database.
2. De persoon logt opnieuw in (app of portal) en kiest **"Maak er een"** met hetzelfde e-mailadres; er volgt een code.
3. **Alleen Dev/Acc:** voeg de nieuwe inlog toe aan de groep Testers: `infra/entra/set-tester.sh <e-mailadres> dev` (of `dev,acc`). Tot dan toont de inlogpagina "geen toegang" (AADSTS50105).
4. Opnieuw inloggen: de API koppelt de nieuwe inlog aan het bestaande account (auditregel `user.relinked`).

## 3. Testen met Expo Go

Expo Go heeft geen eigen URL-schema (`drammers://`). In Dev/Acc stuurt Entra de code daarom naar `https://…/app/auth-redirect`, die hem doorgeeft aan `exp://…` (alleen `exp://`, `exps://` en `drammers://` zijn toegestaan; zonder de PKCE-verifier van de app is de code waardeloos). In Production staat deze pagina uit.

Het testaccount moet in de groep **Testers** zitten (Dev/Acc, B-02) én als lid in de app bekend zijn: vraag een account aan met een lidnummer uit de ledenlijst en het e-mailadres uit e-Boekhouden, of maak het account aan via het portal.

## 4. Problemen oplossen

| Symptoom | Oorzaak | Oplossing |
|---|---|---|
| App: "Inloggen is voor deze omgeving nog niet ingericht" | `DVD_MOBILE_CLIENT_ID` ontbreekt | Stap 2 en 3 |
| Entra: "redirect URI … does not match" | Doorstuurpagina niet geregistreerd | Stap 1 |
| App: "Voor dit e-mailadres is (nog) geen account" | Wel een inlog, maar geen goedgekeurd account met dit e-mailadres (of de oude inlog met wachtwoord bestaat nog) | Account aanvragen of via het portal aanmaken; bij een oude inlog: zie "Overstappen" |
| Inlogpagina vraagt om een wachtwoord | Inlog van vóór 2026-09-27 (met wachtwoord) | Zie "Overstappen" |
| Inlogpagina: AADSTS50105 (Dev/Acc) | Nieuwe inlog zit nog niet in de groep Testers | `set-tester.sh` |
| Accountverzoeken: "Aanmaken mislukt" | Mail tijdelijk niet bereikbaar | **Opnieuw proberen**; de saga maakt nooit een tweede account of tweede mail |
| Geen mail na "Account aanvragen", niets in Accountverzoeken | Het lid had al een app-account: status "Had al een account" (filter in Accountverzoeken); het lid krijgt een herinneringsmail. Een identiek verzoek dat nog op het bestuur wacht, telt binnen 24 uur één keer | Filter op "Had al een account"; zo nodig eerst **App-account verwijderen** op het lid-detail |
| Geen welkomstmail | Spamfilter, of ACS-afzenderdomein nog niet geverifieerd | Map "Ongewenst"; status in Azure Portal → Communication Services → Insights |
