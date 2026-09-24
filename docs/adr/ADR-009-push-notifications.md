# ADR-009: Pushnotificaties

- **Status**: Voorgesteld · 2026-09-24

## Context

Push is een kernfunctie: optochtmeldingen, programmawijzigingen, nieuws, dansgarde (via ouders), kader, dringende berichten. Doelgroepen: iedereen (incl. gasten), leden, rollen, groepen, individuen, ouders van kinderen. Vastleggen: delivery- en read-status. Er is één React Native/Expo-app (ADR-001). Verwacht: ± 1.000–3.000 devices.

## Options considered

| Criterium | **Expo Push Service** | **Azure Notification Hubs** | FCM (HTTP v1) + APNs direct |
|---|---|---|---|
| Integratie Expo-app | Native (`expo-notifications`, ExponentPushToken) | Native tokens (APNs/FCM) registreren; geen officiële Expo-SDK → eigen registratie via backend | Native tokens; zelf APNs-JWT + FCM-OAuth beheren |
| Kosten | Gratis | Free: 1M pushes, **max 500 actieve devices**; Basic ~€ 9/mnd (200k devices) | Gratis |
| Delivery-feedback | Push tickets + receipts (bijv. `DeviceNotRegistered`) | Beperkt (per-message telemetry vanaf Standard ~€ 170/mnd) | FCM-respons; APNs-respons |
| Doelgroepen | Wij expanderen in de backend (tokens per user) | Tags/templates in de hub | Zelf / FCM-topics |
| Credentials | APNs key + FCM service account geüpload in EAS | Zelf in de hub | Zelf |
| Azure-native | Nee (extern, VS) | Ja | Nee (Google/Apple) |
| Lock-in | Laag (tokens zijn te vervangen door native tokens) | Laag | Geen |

## Decision

**Expo Push Service** achter een eigen abstractie `IPushSender` in de backend; de doelgroepbepaling gebeurt in onze eigen database (`NotificationRecipient` per user, tokens in `PushDevice`).

- Fan-out via de outbox → worker in de API-app (ADR-007), in batches van 100 berichten (Expo-limiet).
- Receipts ophalen na ≥ 15 min → `delivery_status` bijwerken; `DeviceNotRegistered` → token deactiveren.
- Read-status via de in-app-inbox (openen van de melding of het bericht → `read_at`).
- Android-notification channels per categorie (Dringend, Optocht, Programma, Nieuws, …) → gebruikers kunnen per categorie in het OS dempen; "Dringend" met hoge prioriteit.
- Payload bevat alleen titel, korte body en een `notificationId`/deeplink; details worden via de API opgehaald.

## Reasoning

- Nul kosten en de minste code bij een Expo-app; receipts geven voldoende delivery-inzicht.
- Notification Hubs Free valt af door de limiet van 500 devices; Basic kost geld zonder meerwaarde voor onze doelgroeplogica (die we sowieso in onze DB nodig hebben voor inbox, ouders en read-status).
- Door de abstractie is migreren naar FCM/APNs direct of Notification Hubs mogelijk zonder domeinwijzigingen.

## Consequences

- Afhankelijkheid van de beschikbaarheid van Expo; bij storing blijven berichten in de inbox staan (pull bij het openen van de app).
- Push-credentials (APNs-key, FCM-serviceaccount) beheerd in EAS; een Expo access token (optioneel, "enhanced push security") in Key Vault.
- Inbox is de "bron van waarheid"; push is een attendering.

## Security implications

- Schakel **Expo "enhanced security for push notifications"** in (access token vereist bij verzenden), zodat gelekte push-tokens niet door derden te misbruiken zijn.
- Push-tokens worden versleuteld opgeslagen (Data Protection) en nooit gelogd.
- Geen persoonsgegevens of gevoelige inhoud in de payload (lock screen, Expo-servers in de VS); bij dansgarde: "Nieuw bericht voor [voornaam]", details in de app.
- Verzendrechten via permissions (`notification.send`, `.group`, `.urgent`), audit per verzending.

## Cost implications

- Expo Push: € 0.
- Alternatief Notification Hubs Basic ~€ 9/mnd; Standard ~€ 170/mnd (niet nodig).
