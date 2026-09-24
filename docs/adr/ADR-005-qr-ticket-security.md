# ADR-005: QR-ticketsecurity

- **Status**: Voorgesteld · 2026-09-24

## Context

Ieder geldig lid krijgt per carnavalsjaar een persoonlijke digitale toegangscode (QR), later ook gekochte dagkaarten en pronkzittingtickets. Eisen: nooit (alleen) het lidnummer, geen leesbare persoonsgegevens, en bestand tegen screenshots, doorsturen, replay, kopiëren en gestolen telefoons. Scannen moet ook **offline** werken (ADR-006).

## Options considered

| Optie | Beschrijving | Screenshot/doorsturen | Replay | Offline valideren | Complexiteit |
|---|---|---|---|---|---|
| 1. Lidnummer in QR | — | ❌ | ❌ | ✅ | Laag (verboden) |
| 2. Statisch random token (128 bit) | Server zoekt het token op | ❌ (werkt eindeloos) | ❌ | ✅ met lijst | Laag |
| 3. UUID + servervalidatie | Idem als 2 | ❌ | ❌ | ✅ met lijst | Laag |
| 4. Server-signed token (JWT/COSE) | Server tekent `ref+exp`; app haalt periodiek nieuwe codes | ◐ (geldig tot exp; vereist verbinding voor verversing) | ◐ | ✅ met publieke sleutel | Middel |
| 5. **Dynamische, device-gebonden signed QR** | App ondertekent `ref+iat+exp` met een hardwaresleutel die alleen op dat device bestaat; de server kent de publieke sleutel | ✅ (screenshot verloopt in ≤ 45 s; ander device kan niet tekenen) | ✅ (exp + replaydetectie) | ✅ (publieke keys gecachet) | Middel |
| 6. TOTP-gebaseerd (gedeeld secret) | App berekent een roterende code met een gedeeld secret | ✅ | ✅ | ✅ maar de scanner moet de **secrets** kennen → groot lek-risico | Middel |

## Decision

**Optie 5: dynamische, device-gebonden, ondertekende QR**, met een statische printcode als fallback voor leden zonder smartphone.

### Ontwerp

1. **Ticketuitgifte**: `Ticket.public_ref` = 16 random bytes (CSPRNG), `credential_version = 1`. Geen persoonsgegevens.
2. **Device-binding**: bij het eerste openen van "Mijn QR" bindt de app het ticket aan het device (`POST /me/ticket/bind-device`, vereist device-proof-of-possession). Er is één actieve binding per ticket; opnieuw binden op een nieuw device maakt de oude binding direct ongeldig (en wordt gelogd; limiet: 3× per carnavalsjaar zonder tussenkomst van het bestuur).
3. **QR-payload** (binair, CBOR of vaste layout, base45-gecodeerd voor QR-alfanumeriek → compacte QR):

   | Veld | Grootte | Betekenis |
   |---|---|---|
   | `v` | 1 B | Formaatversie |
   | `ref` | 16 B | Ticket public_ref |
   | `cv` | 2 B | credential_version |
   | `did` | 8 B | Verkorte device-id (lookup) |
   | `iat` | 4 B | Unix-tijd (s) |
   | `exp` | 2 B | Delta (s), standaard 45 |
   | `sig` | 64 B | ECDSA P-256 (raw r‖s) over alle voorgaande velden, device-key |

   ≈ 97 bytes → QR versie ~6–7 (goed scanbaar op telefoonschermen).
4. **Verversing**: de app genereert elke 30 s een nieuwe code (lokaal, werkt offline). De UI toont een live-indicator (aftellende ring + tijd) en zet de schermhelderheid op maximaal.
5. **Validatie (scanner)**, online door de API of offline door de scanner met de bootstrap-cache:
   1. formaat en versie;
   2. `ref` → ticket bekend; status `Active`; tickettype geldig voor het huidige AccessWindow/event; houder heeft een geldig lidmaatschap;
   3. `cv` == actuele credential_version;
   4. `did` == gebonden device; signature geldig met de publieke sleutel van dat device; device niet `Revoked/Lost`;
   5. `iat − 90 s ≤ now ≤ iat + exp + 90 s` (klokmarge; scanners kalibreren hun klok bij de bootstrap);
   6. vorige scans van dit ticket in dit AccessWindow → herhaling bepalen (ADR-006).
6. **Resultaat en registratie**: altijd een TicketScan-record, ook bij ROOD.
7. **Intrekken/heruitgifte**: ticket `Blocked` of `credential_version++` → alle bestaande codes zijn ongeldig (online direct; offline na de bootstrap-delta).
8. **Printkaart** (geen smartphone): `print_code` = 12 tekens random (base32, ~60 bit), gehasht (SHA-256 + pepper) opgeslagen, als QR met prefix `P:`. Statisch → de scanner toont altijd de naam ter controle; bij de eerste toegang wordt een bandje verplicht. Intrekbaar/vervangbaar.
9. **Dagkaarten voor gasten (R4)**: gasten zonder app ontvangen een server-signed QR (optie 4) in e-mail/webpagina met geldigheid = de dag; na de eerste scan geldt een bandje. Een geaccepteerd lager beveiligingsniveau (betaald product, eenmalig gebruik, dubbelgebruik zichtbaar).

## Reasoning

- Alleen optie 5 adresseert screenshots **en** doorsturen **en** replay terwijl offline validatie mogelijk blijft zonder geheimen op de scanner (scanners kennen alleen publieke sleutels).
- Hardware-gebonden sleutels (Secure Enclave/StrongBox) zijn niet te exporteren of kopiëren.
- Een korte `exp` beperkt het misbruikvenster tot seconden; replaydetectie maakt hergebruik zichtbaar.

## Consequences

- Een kleine eigen native module is nodig voor sleutelgeneratie/-ondertekening (Expo Modules API, Swift/Kotlin, ~200 regels).
- Het lid moet de telefoon bij zich hebben (geen screenshot als backup); communicatie: "Screenshots werken niet".
- Toestelwissel: opnieuw binden via de app (inloggen op het nieuwe toestel); oude binding vervalt.
- Ouders met minderjarige kinderen: het ticket van het kind kan aan het device van de ouder of het kind worden gebonden (één tegelijk).
- Klokafwijking scanners: kalibratie tegen de servertijd bij bootstrap/sync; tolerantie 90 s.

## Security implications

| Risico | Mitigatie |
|---|---|
| Screenshot | Verloopt in ≤ 45 s (+ marge); iOS screenshot-melding; Android `FLAG_SECURE` |
| Doorgestuurde QR | Ander device kan geen geldige signature maken; een doorgestuurde afbeelding verloopt |
| Replay | `exp` + replaydetectie `(ref, iat)` + registratie van herhaalde scans |
| Gekopieerde code/app-data | Private key niet exporteerbaar; een app-databackup bevat geen key |
| Gestolen telefoon | OS-lock vereist voor key-gebruik (`kSecAttrAccessibleWhenUnlockedThisDeviceOnly`); optionele biometrie; device-revocatie |
| Gemanipuleerde app | Kan alleen codes maken voor een ticket waarvan het de key heeft (eigen ticket) |
| Scanner-lek | Scanners bevatten alleen publieke sleutels + ticketstatus + weergavenaam (minimaal); cache versleuteld en gewist 24 uur na het laatste AccessWindow van het carnavalsjaar, of bij uitloggen (gelijk aan ADR-006) |
| Enumeratie | 128-bit refs, signature vereist; ROOD "Onbekende code" zonder details |

## Cost implications

- Geen externe kosten; ontwikkeling ± 1–2 weken extra ten opzichte van een statische QR (native module, validatie, tests).
- Attestation (App Attest/Play Integrity) voor scanners: gratis binnen de quota.
