# ADR-010: Synchronisatie met e-Boekhouden

- **Status**: Geaccepteerd · 2026-09-24 · besluiten B-06 (vrije velden) en B-05/ADR-014 (aanmaken van nieuwe leden via `POST /v1/member`)
- **Geverifieerd tegen**: e-Boekhouden REST API v1, OpenAPI-specificatie `https://api.e-boekhouden.nl/openapi/v1.json` (opgehaald 2026-09-24)

## Context

e-Boekhouden is voorlopig de primaire bron van ledeninformatie. De backend houdt een lokale kopie bij. Het lidnummer is de externe unieke sleutel. De synchronisatie moet idempotent zijn en mag app-specifieke gegevens nooit overschrijven. SyncJobs worden geregistreerd met tellingen, fouten en conflicten.

## Feiten uit de API (geverifieerd)

| Onderwerp | Bevinding |
|---|---|
| Authenticatie | `POST /v1/session` met `{ accessToken: <API-token>, source: "<max 10 tekens>" }` → `{ token, expiresIn }`. Het sessietoken gaat in de header `Authorization: <token>`. `DELETE /v1/session` trekt het in. De API-token wordt in e-Boekhouden aangemaakt en is daarna niet meer zichtbaar |
| Leden-endpoints | `GET /v1/member` (lijst), `GET /v1/member/{id}` (detail), `POST /v1/member` (aanmaken), `PATCH /v1/member/{id}`. *"Only available to clubs or associations."* |
| Lijst-filters | `limit` (1–2000, standaard 100), `offset`, `memberNumber`, `name`, `email`, `city` (filtersyntax `[eq]`, `[like]`, …) |
| Lijst-respons | `{ items[], count }`; items bevatten minimaal `id` en `memberNumber` (het item-schema verwijst naar `MembersResponseDto`, dat niet volledig in de spec is gedefinieerd) → **detail per lid ophalen** voor de volledige gegevens |
| Detailvelden | `id`, `memberNumber` (max 15), `name` (max 100, **één veld**), `salutation`, `gender` (`m`/`v`/`a`), `address` (max 150, **één regel**), `postalCode`, `city`, `country`, `phoneNumber`, `mobilePhoneNumber`, `faxNumber`, `emailAddress`, `emailAddressInvoice`, `emailAddressReminder`, `note`, `termOfPayment`, `iban`, `bic`, `freeText1..freeText10` (max 100), `doNotReceiveNewsletters`, `ledgerId`, `mandate`, `mandateType`, `mandateId`, `mandateSignedDate` |
| **Niet beschikbaar** | Geboortedatum, inschrijfdatum/-jaar, lidmaatschapsstatus (actief/opgezegd), categorie, gesplitste voornaam/tussenvoegsel/achternaam, `lastModified`/wijzigingsdatum |
| Delta-sync | **Geen** `modifiedSince`-filter en **geen webhooks** → altijd de volledige set vergelijken |
| Rate limits | **Niet gedocumenteerd** in de spec → conservatief throttlen (voorstel ≤ 5 requests/s, retry met backoff op 429/5xx) |
| Foutcodes | O.a. `SECURITY_010` (niet geauthenticeerd), `PAGE_001` (limit), `SECURITY_005` (administratie geblokkeerd wegens betalingsachterstand) |
| Alternatief | `/v1/relation` (relaties, type `B`/`P`) als de ledenmodule niet beschikbaar is |

## Options considered

1. **Pull-sync (volledig) via `/v1/member`**: gepland + handmatig, vergelijken via hash.
2. Push vanuit e-Boekhouden (webhooks): niet beschikbaar.
3. **CSV/Excel-export uit e-Boekhouden handmatig importeren**: fallback als de API niet werkt of de ledenmodule ontbreekt.
4. Bidirectionele sync (wijzigingen uit de app terugschrijven): meer risico en conflicten; niet gevraagd.

## Decision

**Optie 1**, met optie 3 als ondersteunde fallback via dezelfde verwerkingslogica (ImportJob met hetzelfde mappingprofiel). De sync zelf is eenrichtingsverkeer e-Boekhouden → app. **Uitzondering (besluit B-05, ADR-014)**: na goedkeuring van een lidmaatschapsaanvraag maakt de provisioning-service het lid aan met `POST /v1/member` (inclusief de vrije velden van B-06) en corrigeert zo nodig een e-mailadres met `PATCH /v1/member/{id}`. De nachtelijke sync bevestigt daarna idempotent op lidnummer.

### Algoritme

```
SyncJob start (status Running; lock: maximaal 1 actieve sync)
session = POST /v1/session
ids = pagineer GET /v1/member?limit=500&offset=… → lijst (id, memberNumber)
voor elk lid (throttled): detail = GET /v1/member/{id}
    map → EbMemberSnapshot (alleen toegestane velden; IBAN/BIC/mandaat/notitie worden genegeerd)
    parse vrije velden volgens mappingconfiguratie (bijv. freeText1 = birth_date, freeText2 = join_year, freeText3 = status)
    hash = SHA256(canonieke JSON van snapshot)
    local = Member WHERE member_number = snapshot.memberNumber
    geen local            → INSERT (Created)                         [nooit dubbel: UNIQUE member_number]
    local.eb_hash == hash → Unchanged (eb_last_seen_at bijwerken)
    anders                → UPDATE alleen e-Boekhouden-velden (Updated, changed_fields gelogd)
                            e-mail gewijzigd bij een actief account → SyncConflict (EmailChangedForActiveAccount), Member-e-mail wel bijwerken, login-e-mail niet
    parsefout             → waarschuwing; oude waarde behouden
duplicaten in de bron (zelfde memberNumber 2×, of eb-id met ander memberNumber) → SyncConflict
missing = actieve lokale leden die niet in ids voorkomen
    als |missing| / |actief| > drempel (10 %) → Conflict (MassDeletionGuard): niets deactiveren, alert
    anders → eb_missing_since = now, sync_state = Missing; na bevestiging in portal (of na 2 opeenvolgende runs) → membership_status = Inactive
reactivatie: lid dat weer verschijnt → Reactivated
DELETE /v1/session
SyncJob einde: tellingen, status (Succeeded / SucceededWithWarnings / Failed / Conflict)
```

- **Idempotent**: dezelfde bron twee keer verwerken → tweede run `Unchanged` voor iedereen; UNIQUE-constraint op `member_number` en `eb_member_id`.
- **Dry-run**: dezelfde logica zonder schrijfacties; rapport met de verwachte wijzigingen (verplicht bij de eerste sync en na een mappingwijziging).
- **Planning**: dagelijks 03:00 (worker-scheduler met `sp_getapplock`, ADR-007) + handmatig vanuit het portal; tijdens carnaval 2× per dag.
- **Transacties**: per lid (niet één grote transactie), zodat één fout de run niet blokkeert; de job-samenvatting aan het einde.

### Veldeigenaarschap

Zie [04-data-model.md §4](../04-data-model.md). Samengevat: e-Boekhouden beheert NAW/contact en de gemapte vrije velden; al het overige (rollen, devices, tickets, scans, relaties, voorkeuren, audit, lokale statusoverride, validiteit) is lokaal en wordt door de sync **nooit** aangeraakt.

### Verdwenen en inactieve leden

| Situatie | Gedrag |
|---|---|
| Lid verdwijnt uit e-Boekhouden | `sync_state = Missing`; na bevestiging → `Inactive`: account naar "beperkt" (alleen publieke content), ticket ongeldig, rollen blijven bewaard maar inactief, gegevens blijven tot de bewaartermijn |
| Status "opgezegd" in vrij veld | `Inactive` per de einddatum (indien bekend) |
| Lokaal `Suspended`/`Deceased` | Override wint altijd van de afleiding uit de sync |
| Lid komt terug | `Reactivated` → `Active` (behalve bij een override) |

### Conflictoplossing

- e-Boekhouden wint voor e-Boekhouden-velden.
- Lokaal wint voor lokale velden.
- Echte conflicten (e-mail van een actief account, duplicaten, massadeletie, lidnummerwijziging) → `SyncConflict` voor handmatige afhandeling in het portal (keuzes: accepteren / negeren / koppelen aan ander lid).

## Reasoning

- De API biedt geen delta of webhooks; een volledige vergelijking met hashes is eenvoudig, robuust en bij enkele honderden leden snel genoeg (± 500 detailcalls bij 5 req/s ≈ 2 min).
- De sync blijft eenrichtingsverkeer (geen conflicten); schrijven gebeurt alleen gericht en na goedkeuring door het bestuur (ADR-014).
- De vrije velden zijn de enige manier om geboortedatum/inschrijfjaar/status in e-Boekhouden te houden. Dat vraagt afspraken met de secretaris (OQ-01/02).

## Consequences

- Afspraken over de invulling van de vrije velden (formaat `YYYY-MM-DD`, jaartal `YYYY`, vaste statuswaarden) en een validatierapport in het portal.
- Portal-schermen: SyncJobs, SyncJobItems (met filter), SyncConflicts, mappingconfiguratie (alleen `config.manage`).
- Contractmonitoring: een wijziging in de e-Boekhouden-spec (nieuwe velden, deprecated) wordt maandelijks door een CI-job gecontroleerd (spec-diff) → issue.

## Security implications

- API-token geeft toegang tot de administratie en heeft voor ADR-014 **schrijfrechten** op leden nodig: opslag in Key Vault, alleen de managed identity van de API-app (gelezen door de sync- en provisioningmodule), onder een e-Boekhouden-gebruiker met minimale rechten (bij voorkeur alleen ledenadministratie, OQ-03), jaarlijkse rotatie, alert bij afwijkende aantallen schrijfacties.
- Dataminimalisatie: IBAN, BIC, mandaat, notities en factuuradressen worden niet opgehaald of opgeslagen (de detailcall levert ze wel → direct weggooien, niet loggen).
- De massadeletie-guard voorkomt dat een storing leidt tot het deactiveren van alle leden.
- Logs bevatten lidnummers en veldnamen, geen veldwaarden van PII.

## Cost implications

- API-gebruik zit in het e-Boekhouden-abonnement (controleren, OQ-03).
- Uitvoering in de bestaande API-app: geen extra kosten.
