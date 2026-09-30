# Runbook: iPhone-app testen via TestFlight

TestFlight is Apple's testomgeving. Testers installeren de app **TestFlight** uit de App Store en krijgen daarin de testversie van De Vrolijke Drammers. De app praat met de Dev-omgeving (`app-dvd-api-dev`) en betaalt met de Mollie-**test**sleutel.

Er komen geen geheimen in de repository of in de chat: Apple-inloggegevens, certificaten en de App Store Connect-sleutel staan alleen bij Apple en EAS.

## 1. Eenmalig: Apple Developer-account en de app in App Store Connect

1. Het **Apple Developer Program** moet actief zijn (€ 99 per jaar). Een organisatie heeft een D-U-N-S-nummer nodig; Apple controleert dat.
2. Ga naar [App Store Connect](https://appstoreconnect.apple.com) → **Apps → +** → **Nieuwe app**:
   - Platform: iOS
   - Naam: De Vrolijke Drammers
   - Primaire taal: Nederlands
   - Bundel-ID: **`nl.vrolijkedrammers.app`**. Staat die er nog niet bij, maak hem dan eerst aan onder Certificates, Identifiers & Profiles → Identifiers. EAS kan dat ook doen bij de eerste build.
   - SKU: bijvoorbeeld `vrolijkedrammers-app`
3. Noteer het **Apple ID** van de app. Dat is een getal onder App-informatie; het is niet geheim.

## 2. Eenmalig: de eerste build doe je zelf

Bij de eerste iOS-build maakt EAS het distributiecertificaat, het provisioningprofiel en de pushsleutel (APNs) aan. Daarvoor log je in met je Apple ID. Doe dit zelf in een terminal in `apps/mobile`:

```bash
npx eas-cli@latest build --platform ios --profile testflight
```

- Beantwoord de vragen met **Yes**: inloggen bij Apple, het team kiezen, en EAS het certificaat, profiel en de pushsleutel laten maken en beheren.
- De build duurt ongeveer 15–25 minuten. Het buildnummer telt automatisch op (`autoIncrement`).

Daarna bewaart EAS de certificaten versleuteld. Volgende builds kan Claude starten zonder jouw Apple-wachtwoord.

## 3. Eenmalig: sleutel voor het automatisch insturen

Zodat builds zonder jouw Apple-login naar TestFlight kunnen:

1. App Store Connect → **Gebruikers en toegang → Integraties → App Store Connect API** → **Sleutel genereren**, met de rol *App Manager*.
2. Download het `.p8`-bestand. Dat kan maar één keer. Noteer ook de **Issuer ID** en **Key ID**.
3. Zet de sleutel in EAS, niet in git:

   ```bash
   npx eas-cli@latest credentials --platform ios
   ```

   Kies: `testflight` → *App Store Connect: Manage your API Key* → *Add a new API Key* → het `.p8`-bestand, de Key ID en de Issuer ID.
4. Verwijder daarna het `.p8`-bestand van je computer, of bewaar het in een wachtwoordkluis.

## 4. Build insturen naar TestFlight

```bash
npx eas-cli@latest submit --platform ios --profile testflight --latest
```

De eerste keer vraagt EAS om het Apple ID van de app (stap 1.3). Na het insturen verwerkt Apple de build; dat duurt 5–30 minuten. De vraag over versleuteling hoeft niet meer te worden beantwoord: in `app.json` staat `ITSAppUsesNonExemptEncryption: false`, omdat de app alleen standaard-HTTPS gebruikt.

Bouwen en insturen in één keer kan ook:

```bash
npx eas-cli@latest build --platform ios --profile testflight --auto-submit-with-profile testflight
```

## 5. Testers toevoegen

**Interne testers** (maximaal 100, direct beschikbaar, zonder controle door Apple):
- App Store Connect → Gebruikers en toegang → gebruiker toevoegen, bijvoorbeeld met de rol *Marketing* of *Ontwikkelaar*.
- Daarna in de app → **TestFlight → Interne test** → groep maken → testers toevoegen.

**Externe testers**, bijvoorbeeld het bestuur en leden (maximaal 10.000):
- **TestFlight → Externe test** → groep maken → testers per e-mail uitnodigen, of een **openbare link** aanzetten.
- De eerste build per versie gaat langs **Beta App Review** van Apple; dat duurt meestal minder dan een dag.
- Vul daarvoor bij Testinformatie in:
  - een korte beschrijving;
  - een contact-e-mailadres;
  - **inloggegevens van een testaccount**: maak een testlid aan in het portal, met een e-mailadres waarop je de code kunt ontvangen.

Testers installeren **TestFlight** uit de App Store en openen de uitnodiging. Nieuwe builds komen daarna vanzelf in TestFlight.

## Wat testers met een echte build wel kunnen (en in Expo Go niet)

- Mijn QR en de munten-QR met de **hardwaresleutel** van de iPhone (werkt ook offline).
- **Pushmeldingen**.
- De **scanner** bij de deur en de kassa (rollen Deurcontrole en Kassa).
- Betalen met iDEAL in de Mollie-testomgeving en terugkeren naar de app.

## Problemen

| Melding | Oorzaak | Oplossing |
|---|---|---|
| "No bundle identifier found" / "App not found" | De app bestaat nog niet in App Store Connect | Stap 1 |
| Build blijft "Processing" in App Store Connect | Apple verwerkt de build | 5–30 minuten wachten |
| "Missing Compliance" | Een oude build van vóór de versleutelingsinstelling | Nieuwe build maken |
| Tester ziet de build niet | Tester zit niet in de groep, of Beta App Review loopt nog | Groep en reviewstatus controleren |
