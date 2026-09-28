# Runbook: pushmeldingen (fase 10)

## Hoe het werkt

Een melding (portal **Meldingen**, of automatisch bij nieuws met "Pushmelding bij publicatie") komt in de outbox. De worker rolt de doelgroep uit naar ontvangers en apparaten en verstuurt via Expo Push in batches van 100. Een kwartier later haalt hij de afleverbevestigingen (receipts) op. Elke melding staat ook in de inbox van de ontvanger, ook als push bij die persoon uit staat of geweigerd is. Zie [ADR-009](../adr/ADR-009-push-notifications.md).

Zonder Expo-token staat `Push__Provider` op `Simulated`: alles werkt behalve dat er niets naar een toestel gaat. Afgeleverd telt dan direct als geslaagd.

## Echte push aanzetten (per omgeving)

1. **Expo-access-token**: expo.dev → project *vrolijkedrammers* → Credentials → zet **Enhanced security for push notifications** aan. Maak daarna onder Account settings → Access tokens een token (bij voorkeur een robot-token).
2. Token in Key Vault (het token gaat niet via chat of repository):
   ```sh
   az login            # werkabonnement, niet Giftnation
   infra/push/set-expo-token.sh dev
   ```
3. GitHub → Settings → Environments → `dev` → variabele **`DVD_PUSH_PROVIDER` = `Expo`**, en deploy opnieuw.
4. **Android (FCM)**: Firebase-project aanmaken, Android-app `nl.vrolijkedrammers.app` toevoegen, een serviceaccount-sleutel (FCM V1) downloaden en uploaden met `cd apps/mobile && npx eas-cli@latest credentials` → Android → Push Notifications (FCM V1). De sleutel gaat niet via chat of repository. Nodig zodra de app (fase 10b) tokens registreert.
5. **iOS (APNs)**: via EAS zodra het Apple Developer Program er is (fase 7).

## Eenmalig bij de eerste deploy van fase 10a

Bicep maakt in Key Vault de sleutel `dataprotection` aan (wrap/unwrap) en geeft de API de rol *Key Vault Crypto Service Encryption User*. De sleutelring staat in de container `dataprotection`. Wordt de sleutel of de ring verwijderd, dan zijn opgeslagen push-tokens onleesbaar; de app registreert ze bij de volgende start opnieuw.

## Problemen oplossen

| Symptoom | Oorzaak | Oplossing |
|---|---|---|
| Melding blijft op "Wordt verstuurd" | Worker staat uit of de database pauzeerde | Health `/health/ready`; de outbox pakt het bericht na maximaal 5 minuten opnieuw op |
| Veel "Mislukt" met `DeviceNotRegistered` | App verwijderd of token verlopen | Normaal; het token staat daarna uit en wordt na 30 dagen opgeruimd |
| Alle berichten mislukken, log "Expo Push mislukt (401)" | Access token ongeldig of enhanced security zonder token | Stap 1–2 opnieuw |
| "Dringende meldingen en meldingen aan iedereen vragen het recht …" | Rol zonder `notification.send.urgent` | Bestuur verstuurt, of het recht toekennen (Rollen en rechten) |
| Nieuwsvinkje "Pushmelding bij publicatie" uitgeschakeld | Openbaar bericht zonder urgent-recht, of geen `notification.send` | Idem, of het bericht alleen voor leden publiceren |
