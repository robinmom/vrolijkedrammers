# Runbook: pushmeldingen (fase 10)

## Hoe het werkt

Een melding (portal **Meldingen**, of automatisch bij nieuws met "Pushmelding bij publicatie") komt in de outbox. De worker rolt de doelgroep uit naar ontvangers en apparaten en verstuurt via Expo Push in batches van 100. Een kwartier later haalt hij de afleverbevestigingen (receipts) op. Elke melding staat ook in de inbox van de ontvanger, ook als push bij die persoon uit staat of geweigerd is. Zie [ADR-009](../adr/ADR-009-push-notifications.md).

Zonder Expo-token staat `Push__Provider` op `Simulated`: alles werkt behalve dat er niets naar een toestel gaat. Afgeleverd telt dan direct als geslaagd.

## Echte push aanzetten (per omgeving)

1. **Expo-access-token**: ga naar <https://expo.dev/settings/access-tokens> (ingelogd als `robinmom`). Zet daar **Enhanced Security for Push Notifications** aan en maak met **Create token** een token (bij voorkeur een robot-token). Kopieer het token: het is maar één keer zichtbaar.
2. Token in Key Vault (het token gaat niet via chat of repository):
   ```sh
   az login            # werkabonnement, niet Giftnation
   infra/push/set-expo-token.sh dev
   ```
3. GitHub → repository → **Settings** → **Environments** → `dev` → **Environment variables** → **Add variable**: `DVD_PUSH_PROVIDER` = `Expo`. Deploy daarna opnieuw (Actions → Deploy → **Run workflow**, of een nieuwe merge).
4. **Android (FCM)**, nodig zodra de app tokens registreert (fase 10b):
   1. <https://console.firebase.google.com> → **Project toevoegen** (bijv. "Vrolijke Drammers").
   2. In het project: **Android-app toevoegen** met pakketnaam `nl.vrolijkedrammers.app`; download `google-services.json` (die verwerkt fase 10b in de app-configuratie).
   3. **Projectinstellingen** (tandwiel) → **Serviceaccounts** → **Nieuwe privésleutel genereren** → **Sleutel genereren**. Bewaar het JSON-bestand veilig; het gaat niet via chat of repository.
   4. Uploaden: expo.dev → project *vrolijkedrammers* → **Credentials** → Android `nl.vrolijkedrammers.app` → **Service Credentials** → **FCM V1 service account key** → **Add a service account key**. Of met de CLI: `cd apps/mobile && npx eas-cli@latest credentials` → Android → production → Google Service Account → *Manage your Google Service Account Key for Push Notifications (FCM V1)* → *Upload a new service account key*.
5. **iOS (APNs)**: via EAS zodra het Apple Developer Program er is (fase 7).

## Eenmalig bij de eerste deploy van fase 10a

**Eerst de bootstrap opnieuw** (Owner op de subscription): de pipeline mag alleen de rollen uit `infra/bootstrap/environment-access.bicep` toekennen, en daar is *Key Vault Crypto Service Encryption User* bij gekomen. Zonder deze stap faalt de uitrol met `roleAssignments/write … does not have permission`.

```sh
az login --tenant <tenant-id-vereniging>
AZURE_SUBSCRIPTION_ID=<id> DVD_LOCATION=swedencentral infra/bootstrap/bootstrap-nonprod.sh
```


Bicep maakt in Key Vault de sleutel `dataprotection` aan (wrap/unwrap) en geeft de API de rol *Key Vault Crypto Service Encryption User*. De sleutelring staat in de container `dataprotection`. Wordt de sleutel of de ring verwijderd, dan zijn opgeslagen push-tokens onleesbaar; de app registreert ze bij de volgende start opnieuw.

## Problemen oplossen

| Symptoom | Oorzaak | Oplossing |
|---|---|---|
| Melding blijft op "Wordt verstuurd" | Worker staat uit of de database pauzeerde | Health `/health/ready`; de outbox pakt het bericht na maximaal 5 minuten opnieuw op |
| Veel "Mislukt" met `DeviceNotRegistered` | App verwijderd of token verlopen | Normaal; het token staat daarna uit en wordt na 30 dagen opgeruimd |
| Alle berichten mislukken, log "Expo Push mislukt (401)" | Access token ongeldig of enhanced security zonder token | Stap 1–2 opnieuw |
| "Dringende meldingen en meldingen aan iedereen vragen het recht …" | Rol zonder `notification.send.urgent` | Bestuur verstuurt, of het recht toekennen (Rollen en rechten) |
| Nieuwsvinkje "Pushmelding bij publicatie" uitgeschakeld | Openbaar bericht zonder urgent-recht, of geen `notification.send` | Idem, of het bericht alleen voor leden publiceren |
