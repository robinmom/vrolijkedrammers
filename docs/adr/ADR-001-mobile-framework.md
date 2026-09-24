# ADR-001: Mobiel framework

- **Status**: Voorgesteld · 2026-09-24
- **Beslissers**: bestuur + technisch lead

## Context

De app moet op iOS en Android dezelfde look & feel en functionaliteit hebben (Figma-ontwerp voor beide platformen, licht/donker). Nodig zijn: pushnotificaties, camera/QR-scannen, hardware-gebonden sleutels (QR-security), offline-opslag (scanner), app store-distributie, en onderhoud door een kleine groep. Het beheerportal wordt een web-SPA.

## Options considered

| Criterium | **React Native + Expo** | **Flutter** | Native (Swift + Kotlin) |
|---|---|---|---|
| Codebase | 1 (TypeScript) | 1 (Dart) | 2 |
| Overlap met portal/website | Hoog: zelfde taal, API-client, tokens, validatieschema's | Laag (Dart) | Geen |
| UI-fidelity Figma | Goed; native componenten + eigen styling; platformverschillen via `Platform.select` | Zeer goed (eigen rendering, pixel-perfect), Material/Cupertino | Beste |
| Push | `expo-notifications` + Expo Push Service (gratis), of FCM/APNs direct | `firebase_messaging` (FCM + APNs via FCM) | Native |
| Camera/QR | `expo-camera` (barcode scanning ingebouwd, ML Kit/AVFoundation) | `mobile_scanner` (ML Kit/AVFoundation) | Native |
| Secure storage / hardware keys | `expo-secure-store` + kleine native module voor Secure Enclave/Keystore-sleutels (Expo Modules API) | `flutter_secure_storage` + platform channel | Native |
| Offline | `expo-sqlite`, TanStack Query persist | `sqflite`/`drift` | Native |
| Build/distributie | **EAS Build/Submit** (cloud builds zonder Mac), EAS Update (OTA JS-updates) | Eigen CI (Codemagic/GitHub macOS runners), geen OTA (Shorebird betaald) | Xcode + Gradle, 2 pipelines |
| App stores | Standaard; OTA binnen de store-regels (alleen JS/assets) | Standaard | Standaard |
| Onderhoud | Expo SDK-upgrade 1–2× per jaar; groot ecosysteem; veel ontwikkelaars beschikbaar | Stabiel, goede tooling; kleinere pool in NL | Dubbel werk |
| Kosten | EAS Free-tier voldoende; optioneel $19/mnd | Gratis; CI-minuten macOS | Hoog (2× ontwikkeling) |
| Toegankelijkheid | Native accessibility-API's direct | Goed (semantics tree) | Beste |

## Decision

**React Native met Expo (managed workflow + development builds), TypeScript, Expo Router.**

## Reasoning

- Eén taal (TypeScript) voor app, beheerportal en (later) website → gedeelde API-client (gegenereerd uit OpenAPI), design tokens en validatie. Dat is het belangrijkste onderhoudsvoordeel voor een vrijwilligersorganisatie.
- EAS maakt iOS-builds en store-submits mogelijk zonder eigen Mac-infrastructuur, en OTA-updates maken snelle bugfixes tijdens carnaval mogelijk.
- Alle benodigde capabilities (push, QR, secure storage, SQLite) zijn beschikbaar als onderhouden Expo-modules. Alleen de hardware-sleutel (Secure Enclave/Keystore-signing) vraagt een kleine eigen native module via de Expo Modules API.
- Flutter levert iets betere pixel-controle, maar introduceert Dart als tweede taal naast C# en TypeScript zonder hergebruik richting het portal.

## Consequences

- Development builds (geen Expo Go) vanwege de eigen native module.
- Expo SDK-upgrades inplannen (buiten de carnavalsperiode).
- Platformverschillen uit Figma (tabbar M3, large titles iOS) via gedeelde componenten met `Platform.select`.
- E2E-tests met Maestro; unit-tests met Jest + React Native Testing Library.

## Security implications

- Tokens in Keychain/Keystore via `expo-secure-store`; geen AsyncStorage voor gevoelige data.
- JS-bundle is inspecteerbaar (Hermes-bytecode vertraagt maar voorkomt het niet) → geen secrets in de app; server beslist alles.
- EAS Update met code signing, zodat alleen ondertekende OTA-updates worden geaccepteerd.
- Scanner-devices: App Attest/Play Integrity via native module (S).

## Cost implications

- Expo/EAS: Free-tier (beperkte builds, lagere prioriteit) volstaat; optioneel Starter-plan ~$19/mnd rond releases.
- Apple Developer € 99/jaar, Google Play $25 eenmalig (gelden voor elke optie).
- Lagere ontwikkel- en onderhoudskosten dan native (één codebase) en dan Flutter (gedeelde TS-code met het portal).
