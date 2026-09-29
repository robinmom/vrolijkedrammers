# Runbook: spike hardwaresleutel (OQ-68, fase 9c)

> **Afgerond (2026-09-29).** Het spikescherm en het EAS-profiel `spike` zijn verwijderd; de hardwaresleutel zit in Mijn QR. Controleren op een toestel: log in, open Mijn QR en kijk in het portal bij **Toegang → Ledentickets** waar de sleutel staat (`StrongBox`, `TrustedEnvironment`, `SecureEnclave` of `Software`). Onderstaande tekst is de beschrijving van de spike.

De spike toont aan of de app een **niet-exporteerbare ECDSA P-256-sleutel** kan maken in de Secure Enclave (iOS) of StrongBox/TEE (Android), daarmee de QR-payload uit ADR-005 kan ondertekenen, en of .NET die handtekening kan controleren. Resultaten horen in [ADR-005](../adr/ADR-005-qr-ticket-security.md#spike-resultaat-oq-68-fase-9c).

## Wat er is

- Eigen Expo-module `apps/mobile/modules/device-key` (Expo Modules API, autolinking):
  - iOS: `DeviceKeyStore.swift` (Security-framework, `kSecAttrTokenIDSecureEnclave`, `WhenUnlockedThisDeviceOnly` + `privateKeyUsage`) en `DeviceKeyModule.swift` (dunne Expo-laag);
  - Android: `DeviceKeyModule.kt` (Android Keystore, eerst StrongBox, anders TEE; `setUnlockedDeviceRequired`; met een challenge de key-attestation-keten).
- Publieke sleutel als SubjectPublicKeyInfo (DER), handtekening als r‖s (64 bytes): direct bruikbaar in de QR-layout en in .NET (`ECDsa.ImportSubjectPublicKeyInfo`, `DSASignatureFormat.IeeeP1363FixedFieldConcatenation`).
- Spikescherm **Meer → Hardwaresleutel (spike)**: sleutel maken, 20× ondertekenen met tijdmeting, QR-lengte en -versie, resultaat delen (JSON). Alleen zichtbaar in ontwikkeling of in de EAS-build `spike`.
- Tests: QR-layout en base45 (Jest), .NET-verificatie van vectoren uit het Security-framework (`tests/Drammers.UnitTests/DeviceKey`).

**Niet in Expo Go:** Expo Go laadt geen eigen native code; het spikescherm meldt dat dan.

## Uitvoeren op een Android-toestel (EAS, gratis Expo-account)

```sh
cd apps/mobile
npx eas-cli@latest login          # eenmalig, eigen Expo-account
npx eas-cli@latest init           # eenmalig: koppelt het project (zet extra.eas.projectId in app.json → committen)
npx eas-cli@latest build --profile spike --platform android
```

Na de build geeft EAS een link/QR-code naar de APK. Installeer op het toestel (sta "onbekende bronnen" toe), open **Meer → Hardwaresleutel (spike)**:
1. **Sleutel maken** → noteer "Sleutel in" (verwacht: `StrongBox` op Pixel 3+ / recente Samsung, anders `TrustedEnvironment`) en "Attestatieketen".
2. **QR-code ondertekenen** → noteer de gemiddelde en maximale tijd.
3. **Resultaat delen** → stuur de JSON; daarmee wordt de handtekening in .NET gecontroleerd (vector toevoegen aan `tests/Drammers.UnitTests/DeviceKey/`).

Graag op twee toestellen: één recent (StrongBox) en één ouder/goedkoop (TEE).

## Uitvoeren op een iPhone

Een iPhone-build vraagt ondertekening door Apple:
- **Met Apple Developer Program** (€99/jaar, nodig voor fase 7): `npx eas-cli@latest build --profile spike --platform ios` (EAS registreert het toestel via `eas device:create`).
- **Zonder**: lokaal met Xcode en een gratis Apple-ID (7 dagen geldig). Vereist CocoaPods (`brew install cocoapods`), daarna `cd apps/mobile && EXPO_PUBLIC_DEVICE_KEY_SPIKE=1 npx expo run:ios --device`. Dit maakt de map `ios/` (staat in `.gitignore`).

De iOS-simulator heeft geen Secure Enclave: daar valt de module terug op een softwaresleutel en toont "Sleutel in: Software".

## Opruimen

**Sleutel verwijderen** in het spikescherm, of de app verwijderen (de sleutel verdwijnt mee; hij zit niet in back-ups).
