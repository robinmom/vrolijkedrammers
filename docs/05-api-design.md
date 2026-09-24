# 05 – API-ontwerp

> Status: v0.3 · 2026-09-24 · accountmodel ADR-014 (geen zelfregistratie, accountverzoeken, provisioning), optochtformulier voor niet-leden
> Stijl: REST/JSON, API-first, OpenAPI 3.1 (gegenereerd door ASP.NET Core + gecontroleerd in CI).

## 1. Conventies

| Onderwerp | Afspraak |
|---|---|
| Base-URL | `https://api.vrolijkedrammers.nl/api/v1` (prod), `api-acc.` / `api-dev.` |
| Versiebeheer | URL-major (`/v1`); alleen additieve wijzigingen binnen v1; breaking = `/v2` met overlapperiode (app-versies in het veld!) |
| Authenticatie | `Authorization: Bearer <Entra access token>`; publieke endpoints zonder token |
| Content | `application/json; charset=utf-8`; camelCase properties; datums ISO 8601 (`2027-02-07`, `2027-02-07T12:30:00Z`); tijden altijd UTC in de API |
| Paginering | `?page=1&pageSize=25` (max 100) → `{ items, page, pageSize, totalCount }`. Voor scanlogs: cursor (`?after=<id>`) |
| Sorteren/filteren | `?sort=registrationNumber,-updatedAt` · filters als queryparameters (`?status=Submitted&categoryId=3`) |
| Fouten | RFC 9457 ProblemDetails: `type`, `title`, `status`, `detail`, `code` (machineleesbaar), `errors` (veldvalidatie), `traceId` |
| Idempotency | `Idempotency-Key` (UUID) verplicht op: `POST /orders`, `POST /parade/registrations/{id}/submit`, `POST /tickets/scans/batch`; server bewaart 24 uur het resultaat per key+user |
| Concurrency | `ETag`/`If-Match` (rowversion) op PUT/PATCH van inschrijvingen, events, nieuws; bij mismatch `412 Precondition Failed` |
| Rate limiting | Zie §6; `429` met `Retry-After` |
| Taal | Foutteksten in het Nederlands (`detail`), `code` in het Engels |
| Deeplinks | `drammers://` + universal/app links `https://app.vrolijkedrammers.nl/...` |

Voorbeeld fout:

```json
{
  "type": "https://api.vrolijkedrammers.nl/problems/validation",
  "title": "Validatie mislukt",
  "status": 422,
  "code": "PARADE_PARTICIPANTS_OUT_OF_RANGE",
  "detail": "Deze categorie vereist minimaal 10 deelnemers (nu 8).",
  "errors": { "adultCount": ["Minimaal 10 deelnemers voor 'Volwassenen Loopgroepen groot (10+)'."] },
  "traceId": "00-4bf92f…-01"
}
```

## 2. Publieke endpoints (geen login)

| Methode | Pad | Omschrijving |
|---|---|---|
| GET | `/app-config` | Min./aanbevolen appversie, maintenance, feature flags (publiek deel) |
| GET | `/carnival-years/current` | Actief carnavalsjaar incl. carnavalsdata (countdown) |
| GET | `/events` | Publieke events (`?from&to&category`); met token: incl. leden-/doelgroepevents |
| GET | `/events/{id}` | Detail (403/404 als niet zichtbaar; altijd 404 om het bestaan niet te lekken) |
| GET | `/events/{id}/ical` | iCalendar-bestand ("Toevoegen aan agenda") |
| GET | `/event-categories` | Filterchips |
| GET | `/news` · `/news/{id}` | Nieuws (zelfde zichtbaarheidslogica) |
| GET | `/photo-albums` · `/photo-albums/{id}` · `/photo-albums/{id}/photos` | Foto's (urls = kortlevende user-delegation-SAS, ook voor publieke albums; geen publieke containers, ADR-008) |
| GET | `/parade/current` | Publieke optochtinfo (datum, start, route, aantal deelnemers, tijdlijn) |
| GET | `/parade/categories` | Actieve categorieën incl. min/max en uitleg |
| POST | `/membership-applications` | Lid worden: aanvraag in de wachtrij, zonder account (Turnstile + rate limit, ADR-014) |
| POST | `/membership-applications/{id}/verify-email` | E-mailadres van de aanvrager bevestigen (code uit de mail) |
| POST | `/account-requests` | "Ik ben al lid": `{ memberNumber, email }` → altijd generieke respons; bij een exacte match wordt het account aangemaakt, anders volgt beoordeling door het bestuur (ADR-014) |
| POST | `/parade/public-registrations` | Optochtinschrijving door niet-leden via het webformulier (Turnstile, rate limit) |
| POST | `/parade/public-registrations/{id}/verify-email` | E-mailadres van de contactpersoon bevestigen; pas daarna wordt de inschrijving ingediend en het opgavenummer toegekend |
| GET | `/parade/public-registrations/status?token=…` | Status alleen lezen via een ondertekende link uit de e-mail |
| POST | `/push-devices/anonymous` | Gast-push registreren (install-id + token) |
| POST | `/payments/mollie/webhook` | Mollie-webhook (`id=tr_…`, form-encoded) |
| GET | `/ticket-types/on-sale` | Tickets in verkoop (R4) |
| POST | `/orders` | Order aanmaken (gast mogelijk, R4) |
| GET | `/orders/{id}?token=…` | Orderstatus voor gast (ondertekende order-token) |

## 3. Ingelogde gebruiker (`/me`)

| Methode | Pad | Permission | Omschrijving |
|---|---|---|---|
| GET | `/me` | (ingelogd) | Profiel, gekoppeld lid, rollen, **permissions**, features, kinderen |
| GET | `/me/member` | member.read.own | Eigen ledengegevens + status |
| GET | `/me/children` | guardian.read.own | Gekoppelde kinderen |
| GET/PUT | `/me/notification-preferences` | notification.read.own | Voorkeuren |
| GET | `/me/notifications` | notification.read.own | Inbox (paginated) |
| POST | `/me/notifications/{id}/read` | notification.read.own | Gelezen markeren |
| GET | `/me/devices` · DELETE `/me/devices/{id}` | (ingelogd) | Eigen devices |
| POST | `/me/devices` | (ingelogd) | Device registreren (publieke sleutel + attestation), zie 06 |
| POST | `/me/devices/{id}/challenge` · `/verify` | (ingelogd) | Proof-of-possession |
| PUT | `/me/devices/{id}/push-token` | (ingelogd) | Push-token registreren/verversen |
| GET | `/me/ticket` | ticket.read.own | Actief toegangsticket (+ kinderen: `/me/children/{id}/ticket`) incl. `publicRef`, `credentialVersion`, geldigheid |
| POST | `/me/ticket/bind-device` | ticket.read.own | Ticket aan dit device binden (verplaatsen = oude binding vervalt) |
| GET | `/me/orders` · `/me/tickets` | ticket.read.own | Gekochte tickets |
| POST | `/me/privacy/export` | member.read.own | AVG-export aanvragen (mail met downloadlink) |
| DELETE | `/me` | (ingelogd) | Account verwijderen (lid blijft bestaan in ledenadministratie) |

## 4. Scanner

| Methode | Pad | Permission | Omschrijving |
|---|---|---|---|
| GET | `/scanner/bootstrap` | ticket.scan (+ trusted device) | Publieke sleutels, actieve AccessWindows, WristbandPolicies, ticketlijst (`publicRef`-hash, status, credentialVersion, bound device-key), revocaties; `?since=` voor delta |
| POST | `/tickets/scan` | ticket.scan | Online validatie van één QR: `{ qr, clientScanId, scannedAt, accessWindowId? }` → resultaat (§5) |
| POST | `/tickets/scans/batch` | ticket.scan | Offline queue uploaden (max 500) → per scan het definitieve resultaat + conflicten |
| POST | `/tickets/scans/{id}/decision` | ticket.scan | Operatorbeslissing bij oranje |
| POST | `/tickets/{id}/wristbands` | ticket.scan | Bandje verstrekt |

Scanresultaat:

```json
{
  "scanId": 123456,
  "result": "WarnRepeatOtherDevice",
  "color": "orange",
  "message": "Deze QR-code is eerder gescand vanaf een ander apparaat.",
  "holder": { "displayName": "J. Jansen", "ticketType": "Lid – toegang carnaval", "photoUrl": null },
  "previousScan": { "scannedAt": "2027-02-06T19:12:03Z", "deviceName": "Scanner Ingang 1", "scannerName": null },
  "wristband": { "issued": true, "type": "Rood – zaterdag", "issuedAt": "2027-02-06T19:12:30Z" },
  "invalidReason": null
}
```

## 5. Optocht (groepsverantwoordelijke)

| Methode | Pad | Permission / scope | Omschrijving |
|---|---|---|---|
| GET | `/parade/categories` | publiek | |
| GET | `/parade/registrations` | parade.register (rol Lid) | Eigen inschrijvingen (waar manager) |
| POST | `/parade/registrations` | parade.register | Concept aanmaken (status Draft) |
| GET | `/parade/registrations/{id}` | manager | Detail + toegestane acties (`allowedActions[]`, `editableFields[]`) |
| PUT | `/parade/registrations/{id}` | manager + editpolicy | Wijzigen (If-Match). Velden buiten het beleid → 403 `PARADE_FIELD_LOCKED`. `registrationNumber` in de body wordt **genegeerd en geweigerd** (422 als afwijkend) |
| POST | `/parade/registrations/{id}/validate` | manager | Dry-run validatie (waarschuwingen/fouten) |
| POST | `/parade/registrations/{id}/submit` | manager (Draft) | Definitief indienen (Idempotency-Key) → opgavenummer |
| POST | `/parade/registrations/{id}/withdraw` | manager (volgens beleid) | Intrekken met reden |
| DELETE | `/parade/registrations/{id}` | manager (alleen Draft) | Concept verwijderen |
| GET/POST | `/parade/registrations/{id}/documents` | manager | Lijst / upload (multipart, max N MB) |
| GET | `/parade/registrations/{id}/documents/{docId}/download` | manager / parade.read | 302 naar SAS-url (5 min) |
| DELETE | `/parade/registrations/{id}/documents/{docId}` | manager (volgens beleid) | |
| GET | `/parade/registrations/{id}/arrival-time` | manager | Aanrijtijd (na publicatie) |
| GET/POST | `/parade/registrations/{id}/managers` | manager (Owner) | Mede-beheerder uitnodigen |

## 6. Beheer (`/admin`)

### Leden en lidmaatschap
| Methode | Pad | Permission |
|---|---|---|
| GET | `/admin/members` (`?q&status&role&group&joinYear&ageFrom&ageTo`) | member.read |
| GET | `/admin/members/{id}` (incl. syncstatus, laatste login, rollen) | member.read |
| PATCH | `/admin/members/{id}` (alleen lokale velden) | member.update |
| GET | `/admin/members/{id}/scans` | ticket.read |
| POST | `/admin/members/{id}/block` · `/unblock` | member.block |
| POST | `/admin/members/{id}/provision-account` (account aanmaken voor een bestaand lid, ADR-014) | member.update |
| GET | `/admin/account-requests` · POST `/{id}/approve` · `/{id}/reject` | member.approve |
| GET | `/admin/account-provisioning` (status per aanvraag) · POST `/{id}/retry` · `/{id}/link-member` `{ memberNumber }` (handmatige terugval) | member.approve |
| GET/POST/DELETE | `/admin/members/{id}/guardians` (POST maakt zo nodig het ouderaccount aan via de provisioning) | member.update |
| GET | `/admin/members/export?format=xlsx|csv&…` | member.export |
| POST | `/admin/members/import` (handmatige sync starten; `?dryRun=true`) | import.run |
| GET | `/admin/sync-jobs` · `/admin/sync-jobs/{id}` (+ items) | import.run |
| GET/POST | `/admin/sync-conflicts` · `/{id}/resolve` | import.run |
| GET | `/admin/membership-applications` | member.approve |
| POST | `/admin/membership-applications/{id}/start-review` · `/approve` (start de provisioning: e-Boekhouden → lokaal → Entra) · `/reject` | member.approve |

### Content
| Methode | Pad | Permission |
|---|---|---|
| CRUD | `/admin/events` (+ `/audiences`, `/attachments`) | event.manage |
| CRUD | `/admin/news` (+ `/publish`, `/schedule`, `/archive`) | news.manage |
| CRUD | `/admin/photo-albums` · POST `/admin/photo-albums/{id}/photos` (upload) · PATCH volgorde/hidden | photo.manage |
| CRUD | `/admin/carnival-years` (+ `/activate`, `/access-windows`) | config.manage |
| CRUD | `/admin/groups` (+ leden) | member.update |

### Meldingen
| Methode | Pad | Permission |
|---|---|---|
| POST | `/admin/notifications/preview-audience` → aantallen | notification.send(.group) |
| POST | `/admin/notifications` (direct/gepland) | notification.send(.group) (+ `.urgent`) |
| GET | `/admin/notifications` · `/{id}` (met delivery/read-stats) | notification.send |
| POST | `/admin/notifications/{id}/cancel` | notification.send |

### Optocht
| Methode | Pad | Permission |
|---|---|---|
| CRUD | `/admin/parades` | parade.config |
| CRUD | `/admin/parade/categories` | parade.config |
| GET | `/admin/parade/registrations` (filters §13-7.1, `?missing=startNumber,measuredLength,documents`) | parade.read |
| GET | `/admin/parade/registrations/{id}` (+ `/history`, `/status-history`) | parade.read |
| PUT | `/admin/parade/registrations/{id}` | parade.manage (/ `-final`) |
| POST | `/admin/parade/registrations/{id}/transition` `{ to, reason }` | parade.manage |
| PUT | `/admin/parade/registrations/{id}/start-number` `{ startNumber \| null }` | parade.assign-start-number |
| PUT | `/admin/parade/registrations/{id}/measured-length` | parade.manage |
| PUT | `/admin/parades/{id}/order` `{ orderedRegistrationIds[], compositionVersion }` | parade.assign-start-number |
| POST | `/admin/parades/{id}/start-numbers/preview` `{ startAt, mode: FillEmpty \| Renumber }` | parade.assign-start-number |
| POST | `/admin/parades/{id}/start-numbers/apply` `{ previewToken }` | parade.assign-start-number |
| POST | `/admin/parades/{id}/start-numbers/publish` | parade.assign-start-number |
| GET | `/admin/parades/{id}/summary` (lengtes, aantallen) | parade.read |
| GET | `/admin/parades/{id}/export?format=xlsx|csv` | parade.export |
| GET | `/admin/parades/{id}/arrival-times/template` | parade.import-arrival-times |
| POST | `/admin/parades/{id}/arrival-times/import` (upload → ImportJob met preview) | parade.import-arrival-times |
| GET | `/admin/import-jobs/{id}` (+ rijen/fouten) | parade.import-arrival-times |
| POST | `/admin/import-jobs/{id}/confirm` · `/cancel` | parade.import-arrival-times |
| POST | `/admin/parades/{id}/arrival-times/publish` `{ notify: true }` | parade.import-arrival-times |

### Tickets, scanners, betalingen
| Methode | Pad | Permission |
|---|---|---|
| CRUD | `/admin/ticket-types` (+ validities) | ticket.manage |
| POST | `/admin/carnival-years/{id}/issue-member-tickets` (idempotent) | ticket.manage |
| GET | `/admin/tickets` · `/{id}` (+ scans) | ticket.read |
| POST | `/admin/tickets/{id}/block` · `/unblock` · `/reissue` (credentialVersion++) · `/print-card` | ticket.manage |
| GET | `/admin/scans` (cursor, filters) · `/admin/scans/conflicts` | ticket.read |
| GET/POST | `/admin/devices` · `/{id}/trust-scanner` · `/{id}/revoke` | ticket.manage |
| CRUD | `/admin/wristband-policies` | ticket.manage |
| GET | `/admin/orders` · `/admin/payments` | payment.read |
| POST | `/admin/payments/{id}/refund` | payment.manage |

### Rapportages, rollen, audit, config
| Methode | Pad | Permission |
|---|---|---|
| GET | `/admin/reports/attendance?carnivalYearId&groupBy=day|hour` | report.view |
| GET | `/admin/reports/parade?carnivalYearId` | report.view |
| GET | `/admin/reports/members?…` | report.view |
| GET | `/admin/reports/jubilees?carnivalYearId&format=` | report.view + member.export (voor export) |
| GET | `/admin/dashboard` | report.view |
| CRUD | `/admin/roles` (+ `/permissions`) · `/admin/users/{id}/roles` | role.manage |
| GET | `/admin/permissions` | role.manage |
| GET | `/admin/audit-log` (filters; **geen** PUT/DELETE) | audit.read |
| GET/PUT | `/admin/config/feature-flags` · `/app-config` · `/retention` · `/jubilee-rules` | config.manage |
| GET/POST | `/admin/privacy-requests` · `/{id}/export` · `/{id}/anonymize` | member.privacy |

## 7. Rate limiting (ASP.NET Core RateLimiter)

| Policy | Toepassing | Limiet |
|---|---|---|
| `anon-read` | Publieke GET's per IP | 120/min |
| `anon-write` | Lid worden, anonieme push-registratie, orders per IP | 10/min, 50/dag |
| `auth-default` | Ingelogd, per user | 300/min |
| `account-request` | `/account-requests`, `/membership-applications`, `/parade/public-registrations` per IP | 5/uur, 20/dag |
| `scan` | Scan-endpoints per device | 120/min (piek ingang) |
| `upload` | Documenten/foto's per user | 30/uur |
| `webhook` | Mollie-webhook | 300/min globaal (geen IP-allowlist, zie 06) |

Wachtwoord- en login-bruteforcebescherming ligt bij Entra External ID (smart lockout).

## 8. Webhooks en integraties

- **Mollie** → `POST /payments/mollie/webhook`: altijd `200 OK` na het vastleggen van het `PaymentWebhook`-record (ook bij een onbekend id, om geen informatie te lekken). De verwerking haalt de status op met `GET /v2/payments/{id}` (server-side, API-key uit Key Vault). Zie ADR/06.
- **e-Boekhouden**: geen inkomende webhooks (de API biedt ze niet). Pull via een geplande worker-job (ADR-010).
- **Expo Push receipts**: een worker-job haalt ≥ 15 min na verzending de receipts op en werkt `NotificationRecipient` bij; `DeviceNotRegistered` → token deactiveren.

## 9. OpenAPI en client-generatie

- `Microsoft.AspNetCore.OpenApi` genereert `openapi/v1.json`; Scalar-UI op `/docs` in Dev/Acc (niet publiek in Prod, of achter login).
- CI: de spec wordt gegenereerd en vergeleken met de vorige (`oasdiff`) → een breaking change binnen v1 laat de build falen.
- `packages/api-client`: `openapi-typescript` + `openapi-fetch` voor app en portal.
- Voorbeelden en foutcodes worden in de spec gedocumenteerd (`x-error-codes`).

## 10. Foutcodes (selectie)

| Code | HTTP | Betekenis |
|---|---|---|
| `ACCOUNT_DISABLED` | 403 | Account uitgeschakeld (lidmaatschap beëindigd of geblokkeerd) |
| `ENVIRONMENT_ACCESS_DENIED` | 403 | Token zonder `environmentAccess` voor deze (Dev/Acc-)omgeving (B-02) |
| `PARADE_REGISTRATION_CLOSED` | 409 | Buiten inschrijfperiode |
| `PARADE_FIELD_LOCKED` | 403 | Veld niet wijzigbaar in deze status |
| `PARADE_START_NUMBER_TAKEN` | 409 | Startnummer al in gebruik (bevat de groepsnaam van de houder) |
| `PARADE_PARTICIPANTS_OUT_OF_RANGE` | 422 | Deelnemers buiten categoriebereik |
| `PARADE_INVALID_TRANSITION` | 409 | Statusovergang niet toegestaan |
| `CONCURRENCY_CONFLICT` | 412 | ETag komt niet overeen |
| `TICKET_NOT_FOUND` / `TICKET_BLOCKED` / `TICKET_EXPIRED` | 200 (in scanresultaat) | Scanner krijgt altijd een resultaatobject |
| `DEVICE_NOT_TRUSTED` | 403 | Scanmodus op niet-goedgekeurd device |
| `ORDER_SOLD_OUT` | 409 | Capaciteit bereikt |
| `IDEMPOTENCY_KEY_REUSED` | 422 | Zelfde key, andere payload |
