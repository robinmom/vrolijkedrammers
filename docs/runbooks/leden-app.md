# Runbook – Leden-app: inloggen, accounts en apparaten (fase 9a)

> Status: v1.0 · 2026-09-26 · Hoort bij ADR-014 (alleen leden krijgen een account) en fase 9 in [15-implementation-plan](../15-implementation-plan.md).
> Bevat **geen** secrets: client-ID's en tenant-ID's zijn openbaar (ze staan in elke inlogpagina).

## 1. Hoe het werkt

| Onderdeel | Werking |
|---|---|
| Account aanvragen ("Ik ben al lid") | Lidnummer + e-mailadres in de app. Klopt het exact met e-Boekhouden (actief lid, nog geen account), dan maakt de worker het Entra-account en de gebruiker (rol *Carnavalist*) en stuurt een welkomstmail. Anders komt het verzoek in **Beheerportal → Accountverzoeken**. De aanvrager ziet altijd dezelfde melding. |
| Account vanuit het portal | **Leden → lid → App-account aanmaken** (recht `member.approve`); het account krijgt het e-mailadres uit e-Boekhouden. |
| Inloggen | App → Meer → Inloggen: inlogpagina van Entra External ID met een e-mailcode (geen wachtwoord). Het refresh-token staat in de Keychain/Keystore. |
| Apparaten | Elke aanmelding registreert de installatie. Een afgemeld apparaat (door het lid of via **Gebruikers → gebruiker → Apparaten**) krijgt 401 `DEVICE_REVOKED` en logt uit. |
| Account verwijderen | App → Mijn gegevens → Account verwijderen: lokaal account geanonimiseerd, Entra-account verwijderd. Het lid in e-Boekhouden blijft. |
| AVG-export | App → Mijn gegevens → Mijn gegevens downloaden: JSON met alle gegevens, 24 uur te downloaden; na 2 dagen opgeruimd (lifecycle-regel). |
| Welkomstmail | Azure Communication Services, afzender `DoNotReply@…azurecomm.net` (eigen domein in fase 7). **De tekst is een concept; het bestuur keurt hem goed** (`MemberAccounts.WelcomeMail`). |

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

## 3. Testen met Expo Go

Expo Go heeft geen eigen URL-schema (`drammers://`). In Dev/Acc stuurt Entra de code daarom naar `https://…/app/auth-redirect`, die hem doorgeeft aan `exp://…` (alleen `exp://`, `exps://` en `drammers://` zijn toegestaan; zonder de PKCE-verifier van de app is de code waardeloos). In Production staat deze pagina uit.

Het testaccount moet in de groep **Testers** zitten (Dev/Acc, B-02) én als lid in de app bekend zijn: vraag een account aan met een lidnummer uit de ledenlijst en het e-mailadres uit e-Boekhouden, of maak het account aan via het portal.

## 4. Problemen oplossen

| Symptoom | Oorzaak | Oplossing |
|---|---|---|
| App: "Inloggen is voor deze omgeving nog niet ingericht" | `DVD_MOBILE_CLIENT_ID` ontbreekt | Stap 2 en 3 |
| Entra: "redirect URI … does not match" | Doorstuurpagina niet geregistreerd | Stap 1 |
| App: "Voor dit e-mailadres is (nog) geen account" | Wel Entra-account, geen gebruiker bij de vereniging (bijv. na verwijderen) | Account aanvragen of via het portal aanmaken |
| Accountverzoeken: "Aanmaken mislukt" | Graph of mail tijdelijk niet bereikbaar | **Opnieuw proberen**; de saga maakt nooit een tweede account |
| Geen welkomstmail | Spamfilter, of ACS-afzenderdomein nog niet geverifieerd | Map "Ongewenst"; status in Azure Portal → Communication Services → Insights |
