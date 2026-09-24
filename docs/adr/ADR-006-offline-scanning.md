# ADR-006: Offline scannen en reconciliatie

- **Status**: Voorgesteld · 2026-09-24

## Context

Tijdens carnaval kan het mobiele netwerk overbelast zijn. Scanners moeten dan blijven werken. Kernscenario: scanner A en B zijn offline en scannen allebei dezelfde QR. Beide tonen lokaal "eerste geldige scan". Na synchronisatie moet zichtbaar worden dat dit een dubbele toegang was. Scans mogen elkaar nooit overschrijven.

## Options considered

1. **Alleen online scannen**: eenvoudig, maar onbruikbaar bij netwerkproblemen.
2. **Offline scannen met lokale validatie en queue + server-reconciliatie** (eventual consistency).
3. **Peer-to-peer-synchronisatie tussen scanners** (Bluetooth/lokaal wifi-netwerk): directe detectie van dubbele scans, maar complex en onbetrouwbaar in een drukke omgeving.
4. **Lokale edge-server** (laptop + router in de zaal): betrouwbaar lokaal netwerk, maar hardware- en beheerlast.

## Decision

**Optie 2**, met de mogelijkheid om later een lokaal wifi-netwerk toe te voegen (vermindert offline tijd zonder architectuurwijziging).

### Werking scanner

**Bootstrap** (bij het starten van de scanmodus en elke 5 min bij verbinding; delta via `?since=`):
- AccessWindows, WristbandPolicies, tickettypen;
- ticketlijst: `ref-hash`, status, `credential_version`, gebonden `device_id` + publieke sleutel, weergavenaam, tickettype, lidmaatschap geldig t/m;
- revocaties (tickets/devices);
- scans van het huidige AccessWindow (van alle scanners, compact: `ref-hash`, tijd, device) → een offline scanner kent in elk geval de scans tot de laatste sync;
- servertijd → `clock_offset` voor de eigen klok.

Opslag: versleutelde SQLite (key in SecureStore); gewist 24 uur na het laatste AccessWindow van het carnavalsjaar, of bij uitloggen (alleen met een lege queue).

**Scannen** (online-first met korte timeout):
1. Lokale validatie (ADR-005) → resultaat binnen < 200 ms.
2. Is er verbinding? `POST /tickets/scan` met een timeout van 1,5 s. Het serverresultaat is leidend (weet van scans van andere scanners).
3. Timeout of offline → toon het lokale resultaat met de indicator "Offline gecontroleerd"; zet de scan in de queue (`client_scan_id`, `scanned_at_device` (gecorrigeerd met clock_offset), ruwe QR-velden, lokaal resultaat).
4. Queue-sync: elke 10–30 s bij verbinding via `POST /tickets/scans/batch` (idempotent op `client_scan_id`).

**Lokale herhalingsdetectie**: de scanner kent de eigen scans + de gesynchroniseerde scans van anderen → kan offline al ORANJE tonen als de code eerder (vóór de laatste sync) op een ander device is gescand.

### Server-reconciliatie

Bij het ontvangen van scans (online of batch), per ticket + AccessWindow, in een transactie met een lock op het ticket (`UPDLOCK` op de ticketrij):
1. Voeg het record in (nooit overschrijven; `client_scan_id` unique → duplicaat = no-op).
2. Sorteer alle scans van dit ticket in dit AccessWindow op `scanned_at_device` (tiebreak: `received_at_server`, daarna `client_scan_id`).
3. Herbereken `final_result` deterministisch:
   - eerste geldige scan → `FirstEntry`;
   - latere geldige scans op hetzelfde device → `RepeatSameDevice`;
   - latere geldige scans op een ander device → `RepeatOtherDevice`;
   - ongeldig → `Invalid` (reden).
4. Als een scan lokaal als "eerste" werd getoond (`local_result = Valid`) maar `final_result = RepeatOtherDevice` → `conflict_detected_after_sync = 1`, `previous_scan_id` gezet.
5. Een ticket dat offline is toegelaten terwijl het online al geblokkeerd was → `final_result = Invalid(Blocked)` + `conflict_detected_after_sync = 1` ("Toegelaten tijdens offline, ticket was geblokkeerd").
6. Signaleer conflicten: dashboardteller, scanlog-filter "Conflicten", en bij de volgende scan van hetzelfde ticket toont de scanner ORANJE met de melding "Eerder (offline) gescand op Scanner B om 20:14".

Alleen de reconciliatie-procedure mag `final_result`, `previous_scan_id` en `conflict_detected_after_sync` bijwerken. Alle andere velden zijn immutable (DB-rechten).

### Tijd

- Opgeslagen: `scanned_at_device` (gecorrigeerd), `received_at_server`, `clock_skew_ms`.
- Rapportage gebruikt `scanned_at_device` (de werkelijke toegangstijd); plausibiliteitscheck: als `scanned_at_device` > `received_at_server` + 2 min of ouder dan het AccessWindow, dan krijgt het record de vlag `suspicious_time` en valt terug op `received_at_server` voor de volgorde.

### Voorbeeld

| Tijd (device) | Scanner | Lokaal getoond | Na sync |
|---|---|---|---|
| 20:14:03 | A (offline) | GROEN – geldig | FirstEntry |
| 20:14:40 | B (offline) | GROEN – geldig | RepeatOtherDevice, conflict = ja, vorige = A 20:14:03 |
| 20:31:10 | A (online) | ORANJE – eerder gescand op ander apparaat (B 20:14:40) … | RepeatSameDevice/Other (volgens regels) |

Rapport: 1 unieke bezoeker, 3 scans, 1 eerste toegang, 1 offline-conflict.

## Reasoning

- Toegang moet doorgaan bij netwerkproblemen; de schade van een zeldzame dubbele toegang is beperkt en wordt achteraf zichtbaar.
- Deterministische reconciliatie (sorteren op device-tijd) geeft hetzelfde resultaat, ongeacht de volgorde van synchroniseren.
- Geen P2P- of edge-hardware nodig; bandjes vormen de fysieke tweede controle.

## Consequences

- De scanner-app heeft een queue-UI (aantal niet-gesynchroniseerde scans, laatste sync-tijd) en waarschuwt als het > 10 min offline is.
- Uitloggen/afsluiten met een niet-lege queue wordt geblokkeerd (waarschuwing).
- De bootstrap-omvang (± 1.000 tickets × ~150 B) is klein.
- Tests: scenario's A/B, omgekeerde syncvolgorde, dubbele batches, klokafwijking, geblokkeerd tijdens offline (zie 12).

## Security implications

- Offline revocaties werken pas na de volgende sync (restrisico, T4/T9); tijdens carnaval de bootstrap-frequentie verhogen.
- De scanner-cache bevat minimale persoonsgegevens (weergavenaam) en is versleuteld; gewist 24 uur na het laatste AccessWindow.
- Batch-uploads vereisen `ticket.scan` + een trusted device + device-signature, zodat een aanvaller geen scans kan injecteren.
- Een gemanipuleerde scanner kan lokaal onterecht GROEN tonen, maar alle scans zijn herleidbaar tot device + gebruiker.

## Cost implications

- Geen infrastructuurkosten; extra ontwikkeltijd ± 1,5–2 weken (cache, queue, reconciliatie, tests).
- Optioneel: lokaal wifi/4G-router in de zaal (~€ 100–200 eenmalig) om de offline tijd te beperken.
