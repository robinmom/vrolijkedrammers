# 06 – Securitymodel

> Status: v0.3 · 2026-09-24 · besluiten B-01…B-07 verwerkt; accountmodel volgens ADR-014
> Referenties: OWASP API Security Top 10 (2023), OWASP ASVS 4.x L2, OWASP MASVS v2 / MASTG. Threat model: [11-threat-model.md](11-threat-model.md).

## 1. Securityprincipes

1. **Defense in depth**: identiteit (Entra) → autorisatie (permissions + resource-scope) → validatie → DB-rechten → netwerk → monitoring.
2. **Least privilege** voor mensen (rollen), services (managed identities met minimale Azure-RBAC) en database (aparte DB-users per functie).
3. **Geen secrets in Git**: Key Vault + managed identity; lokaal `dotnet user-secrets`; gitleaks in CI.
4. **Server is leidend**: de app wordt als onbetrouwbaar beschouwd (gemanipuleerde app, geroote telefoon).
5. **Privacy by design**: dataminimalisatie, pseudonimisering in logs, bewaartermijnen afgedwongen.

## 2. Authenticatie (ADR-004)

| Onderdeel | Maatregel |
|---|---|
| Identity provider | Microsoft Entra External ID: **één tenant** met aparte app-registraties per omgeving; Dev/Acc beperkt tot testers via *Require user assignment* + claim `environmentAccess` (B-02). **Zelfregistratie uit**; accounts alleen via de provisioning-service na goedkeuring (ADR-014) |
| App | OIDC Authorization Code + PKCE via systeembrowser (`expo-auth-session` / ASWebAuthenticationSession / Custom Tabs); gebrande hosted pages. Native-auth-SDK's bestaan (nog) niet voor React Native, zie ADR-004 |
| Portal | MSAL.js (Auth Code + PKCE) met een eenmalige e-mailcode. **Geen Conditional Access** (B-02-MFA): beheerders gebruiken een vrolijkedrammers.nl-adres waarvan de mailbox met MFA is beveiligd (Microsoft 365). Aanvullend: weinig beheerders, audit op alle beheeracties, meldingen bij gevoelige acties (fase 7); passkeys later |
| Methoden | E-mail + wachtwoord, e-mail-OTP (wachtwoordloos). Social login optioneel (niet in MVP) |
| Wachtwoorden | Uitsluitend in Entra (nooit in onze DB). Policy: min. 8 tekens + banned-password-list (Entra); "Wachtwoord opnieuw instellen" via e-mailcode; wachtwoorden nooit zichtbaar, terug te lezen of per mail te verzenden |
| Brute force | Entra smart lockout + onze rate limits en Turnstile op accountverzoeken, lid-worden en het optochtformulier |
| Tokens | Access token 60 min; refresh-token rotation (Entra, public client); sessie-intrekking via "revoke sessions" (Graph) bij blokkeren/verloren telefoon |
| Tokenopslag app | `expo-secure-store` (iOS Keychain `WHEN_UNLOCKED_THIS_DEVICE_ONLY`, Android Keystore-encrypted). Nooit in AsyncStorage |
| Tokenopslag portal | In-memory + MSAL sessionStorage-cache; geen tokens in localStorage |
| Validatie API | `Microsoft.Identity.Web`: issuer, audience, signing keys, lifetime; `oid` → `User.external_object_id` |
| Accountkoppeling | Account bestaat alleen voor leden en ouders van minderjarige leden (B-05). Nieuwe leden: na goedkeuring door het bestuur. Bestaande leden: aanvraag met lidnummer + e-mailadres; alleen bij een exacte match met e-Boekhouden direct aangemaakt, anders beoordeling door het bestuur; altijd een generieke respons (geen enumeratie). Het bezit van het e-mailadres wordt bewezen bij de eerste inlog (OTP) |
| Admin recovery | ≥ 2 break-glass-accounts (interne tenant-admin-accounts) met passkey/hardware-key, credentials verzegeld bewaard bij voorzitter en secretaris; gebruik → alert |

**Als toch eigen authenticatie gekozen zou worden** (afgewezen in ADR-004), dan geldt minimaal: Argon2id (m=64 MiB, t=3, p=1) met unieke salt; lockout/throttling per account+IP; reset-tokens van 32 random bytes, gehasht opgeslagen, 30 min geldig, eenmalig; refresh-token rotation met reuse-detectie; sessie-invalidatie per device.

## 3. Autorisatie

Zie [07-rbac.md](07-rbac.md). Aanvullend:
- **BOLA-preventie (API1)**: elke query op een resource filtert op scope (`WHERE manager = @user` of permission). Object-ID's zijn UUIDv7 (niet sequentieel te raden), maar de autorisatie hangt daar niet van af.
- **BOPLA (API3)**: expliciete request-DTO's (geen mass assignment), expliciete response-DTO's per rol (geen entiteiten serialiseren); `registration_number` bestaat niet in de update-DTO.
- **BFLA (API5)**: `/admin/*` vereist permissions; een reflectietest dwingt af dat elk endpoint geannoteerd is.

## 4. Device-identificatie (§49)

| Stap | Maatregel |
|---|---|
| Registratie | De app genereert een **ECDSA P-256-sleutelpaar in hardware** (iOS Secure Enclave, Android StrongBox/TEE Keystore; via een kleine Expo native module, want `expo-secure-store` slaat alleen secrets op en genereert geen hardware-keys). Publieke sleutel → `POST /me/devices`. De server geeft `device_id` terug |
| Proof-of-possession | Server-challenge (nonce, 2 min) wordt gesigneerd door het device → gekoppeld. Gevoelige calls (scans, ticketbinding) dragen een device-signature (`X-Device-Id`, `X-Device-Signature` over method+path+timestamp+body-hash) |
| Attestation (S) | iOS App Attest / Android Play Integrity bij registratie van **scanner**-devices; resultaat in `attestation_status`. Niet verplicht voor gewone leden (kosten/complexiteit), wel voor `trusted_scanner` |
| Trusted scanner | Het bestuur keurt een device expliciet goed (`trusted_scanner = 1`) in het portal; alleen op zo'n device werkt de scanmodus |
| Intrekken | Device `Revoked/Lost` → signatures geweigerd, push-token verwijderd, gebonden tickets moeten opnieuw gebonden worden |

Client-informatie (model, OS, appversie) is **informatief**; identiteit berust op het sleutelbezit.

## 5. QR-toegang (ADR-005)

Samenvatting van het ontwerp:

- **Ticket** heeft een random `public_ref` (128 bit) en een `credential_version`; er staat geen lidnummer of naam in de QR.
- **App** houdt een ticketbinding: het ticket is gebonden aan de device-sleutel (`bound_device_id`).
- **QR-payload** (compact binair, base45-gecodeerd, ≈ 97 bytes; details in ADR-005):
  `v=1 | ref=public_ref | cv=credential_version | did=device_id(kort) | iat=unix | exp=iat+45s | sig=ECDSA-P256_device(…) (64 bytes)`
- De QR ververst elke 30 s in de app (animatie + tijdsindicator: "live" code, maakt screenshots zichtbaar verouderd).
- **Scanner** valideert:
  1. de signature met de publieke sleutel van het gebonden device (uit de bootstrap-cache);
  2. `exp` versus de scannerklok (±90 s tolerantie voor klokafwijking; de scannerklok wordt bij bootstrap tegen servertijd gekalibreerd);
  3. `cv` == actuele credential_version, ticketstatus `Active`, lidmaatschap geldig, geldig voor het huidige AccessWindow/event;
  4. replay: dezelfde `(ref, iat)` al gezien → herhaalde scan (geen fout, wel registratie).
- **Screenshot/doorsturen**: een screenshot is ≤ 45 s geldig; doorsturen naar een ander device werkt niet (de signature is gebonden aan de device-sleutel die niet te exporteren is).
- **Gestolen telefoon**: de sleutel vereist een ontgrendeld device (Keychain-accesscontrol `.userPresence` optioneel); het lid/bestuur trekt het device in → revocatie is direct online effectief, offline na de volgende bootstrap-delta (≤ 5 min bij verbinding).
- **Weergave naam/foto op scanner**: bij twijfel kan de scanner de naam (en optioneel pasfoto) zien om de identiteit te verifiëren.
- **Leden zonder smartphone**: geprinte kaart met een statische `print_code` (random, gehasht opgeslagen), resultaat altijd met naam; gebruik gecombineerd met bandje; intrekbaar.

## 6. Mollie (betalingen)

| Risico | Maatregel |
|---|---|
| Gemanipuleerde redirect ("betaald") | Redirect-pagina toont alleen de **serverstatus**; tickets worden uitsluitend uitgegeven na server-side `GET /v2/payments/{id}` met status `paid` |
| Vervalste webhook | De webhook bevat alleen een `id`; wij vertrouwen de body niet en halen de status altijd bij Mollie op met onze API-key. Onbekende id's → gelogd, genegeerd, `200` |
| Webhook-URL raden/DoS | Webhook-URL bevat een per-omgeving random pad-segment (bijv. `/payments/mollie/webhook/{secretSlug}`) als extra filter (geen echte authenticatie); rate limit; zodra Mollie next-gen webhooks met signatures GA zijn: signature verifiëren (OQ-24) |
| Dubbele verwerking | Idempotent: transitie op basis van (payment, status) in één transactie met rowversion; tickets per OrderLine maar één keer uitgegeven (unique constraint `order_line_id` + seq) |
| Bedrag-manipulatie | Prijs uitsluitend server-side uit TicketType; client stuurt alleen type + aantal |
| Overselling | Capaciteitsreservering in transactie met `UPDLOCK` op TicketType-teller; holds verlopen na 15 min |
| API-key | Key Vault; alleen de API-identiteit leest `mollie-api-key`; aparte test-key voor Dev/Acc |
| Refunds | Alleen `payment.manage`, audit, ticket automatisch geblokkeerd |

## 7. e-Boekhouden

- De API-token staat in Key Vault; alleen de managed identity van de API-app heeft `get`-rechten; het secret wordt uitsluitend door de sync- en provisioningmodule gelezen (code-review, B-01). Gebruikte calls: lezen op `/v1/member` en **schrijven** via `POST /v1/member` (en `PATCH` voor e-mailcorrectie) na goedkeuring door het bestuur (ADR-014). Het token wordt aangemaakt onder een e-Boekhouden-gebruiker met minimale rechten (bij voorkeur beperkt tot de ledenadministratie, OQ-03); alert bij ongebruikelijke aantallen schrijfacties.
- **Microsoft Graph** (accountprovisioning): aparte app-registratie met `User.ReadWrite.All`, certificaat in Key Vault, uitsluitend gebruikt door de provisioning-service; elke aanroep in de AuditLog.
- Sessies worden na gebruik afgesloten (`DELETE /v1/session`).
- **Massa-deactivatie-guard**: als > 10 % (configureerbaar) van de actieve leden in één run "verdwijnt", wordt de run als `Conflict` gemarkeerd en worden er **geen** deactivaties doorgevoerd (bescherming tegen API-storingen of een lege response).
- Dry-run-modus voor een eerste sync en na wijzigingen in de veldmapping.

## 8. Bestandsuploads (ADR-008)

| Maatregel | Detail |
|---|---|
| Toegestane typen | PDF, JPG, PNG, DOCX (optocht); JPG/PNG/HEIC (foto's). Controle op extensie **én** magic bytes; content-type server-side bepaald |
| Grootte | Configurabel (standaard 10 MB document, 25 MB foto); request-size limits in Kestrel |
| Opslag | Private containers (`parade-documents`, `photos-original`, `photos-derived`, `quarantine`); geen public access op storage-accountniveau (`allowBlobPublicAccess=false`) |
| Bestandsnamen | Server genereert het blob-pad (`{registrationId}/{uuid}.{ext}`); de originele naam alleen als gesanitiseerde metadata |
| Malware | Upload → `quarantine` → **Microsoft Defender for Storage malware scanning** (on-upload) → bij `Clean` verplaatsen naar de doelcontainer; `Infected` → verwijderen + melding. Zie ADR-008 voor kostenafweging en fallback |
| Afbeeldingen | Opnieuw encoderen (ImageSharp) → EXIF/GPS strippen, thumbnails; beschermt tegen polyglot-bestanden |
| DOCX | Alleen download (nooit server-side renderen); `Content-Disposition: attachment` |
| Download | Via API-autorisatie → user-delegation-SAS (read, 5 min, specifieke blob, HTTPS only) |

## 9. API- en webbeveiliging

| Onderwerp | Maatregel |
|---|---|
| Transport | Alleen HTTPS (TLS 1.2+), HSTS (1 jaar, includeSubDomains), App Service "HTTPS only", minimale TLS 1.2 |
| Headers | `Content-Security-Policy` (portal: `default-src 'self'`; connect-src API + Entra), `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `X-Frame-Options: DENY`/`frame-ancestors 'none'`, `Permissions-Policy` |
| CORS | Allowlist: portal-domein, websitedomein; geen wildcard met credentials |
| CSRF | API gebruikt bearer-tokens (geen cookies) → CSRF niet van toepassing; indien later cookie-auth (BFF) dan SameSite=strict + anti-forgery |
| XSS | Portal: React escaping; rich text (nieuws/events) opgeslagen als Markdown of gesanitized HTML (HtmlSanitizer allowlist); app rendert Markdown → native (geen WebView-HTML) |
| SQL-injectie | EF Core parametrisering; geen string-concatenatie; raw SQL alleen met `FromSql` + parameters; code review + CodeQL |
| Inputvalidatie | FluentValidation; maximale lengtes gelijk aan DB; numerieke ranges; enum-allowlists; JSON-body max 1 MB (behalve uploads) |
| Excel-injectie | Export: cellen die met `= + - @ \t \r` beginnen worden geprefixt met `'` |
| Foutafhandeling | Geen stacktraces naar clients; ProblemDetails met traceId |
| SSRF | Geen user-supplied URL's die de server ophaalt |
| Deserialisatie | System.Text.Json, geen polymorfe type-info van de client |
| Afhankelijkheden | Dependabot (NuGet, npm, GitHub Actions), `dotnet list package --vulnerable`, `pnpm audit` in CI, CodeQL (C#, TS), gitleaks |

## 10. Mobiele app (MASVS)

| MASVS | Maatregel |
|---|---|
| STORAGE | Tokens en keys alleen in Keychain/Keystore; SQLite-scannercache versleuteld (SQLCipher via `expo-sqlite` met key uit SecureStore); geen PII in logs; backups: `android:allowBackup=false` voor gevoelige data |
| CRYPTO | Platform-API's; ECDSA P-256 in hardware (Secure Enclave ondersteunt geen Ed25519); geen eigen crypto |
| AUTH | OIDC+PKCE; biometrie optioneel als lokale unlock voor QR/scanner (geen vervanging van serverauth) |
| NETWORK | ATS (iOS) / network security config (Android) HTTPS-only; certificate pinning **niet** in MVP (onderhoudsrisico bij certificaatrotatie), heroverwegen voor scanner (OQ-25) |
| PLATFORM | Deeplinks gevalideerd; geen gevoelige data in push-payload (alleen titel/body + id; details via API) |
| CODE | Hermes bytecode, geen secrets in bundle (alleen publieke config); EAS Build met managed signing-credentials |
| RESILIENCE | Root/jailbreak-detectie **alleen als waarschuwing** in scanmodus (lage waarde, hoge onderhoudslast); scanner-trust via attestation |
| Updates | `app-config` → forced update bij `min_app_version`; Expo Updates (OTA) alleen voor JS-fixes, ondertekend (code signing) |

## 11. Azure-beveiliging

| Onderwerp | Maatregel |
|---|---|
| Identiteiten | System-assigned managed identity voor de API-app (inclusief workers, B-01); Azure RBAC: `Key Vault Secrets User`, `Storage Blob Data Contributor` (alleen de benodigde containers via ABAC-condities of aparte accounts), geen `Owner` voor apps |
| Database | Entra-only authenticatie (SQL-auth uit); DB-users uit managed identities: `app_runtime` (DML op de eigen schemas, alleen INSERT/SELECT op audit), `app_migrator` (DDL, alleen vanuit de pipeline), `app_reporting` (SELECT op reporting-views), optioneel `app_sync` via een user-assigned MI voor de sync-verbinding |
| Encryptie | TDE (standaard), TLS in transit; Always Encrypted niet nodig (kosten/complexiteit); gevoelige velden (push-tokens, print-codes) gehasht/versleuteld op applicatieniveau (ASP.NET Data Protection met key ring in Blob + Key Vault key) |
| Netwerk | MVP: publieke endpoints met **firewallbeperkingen** (SQL: "Allow Azure services" uit, alleen App Service outbound IP's + beheer-IP's tijdelijk; Storage: alleen via RBAC/SAS). Later/optioneel: VNet-integratie + private endpoints (kosten ~€7/endpoint/maand) — zie 08 |
| Key Vault | RBAC-mode, soft delete + purge protection, aparte vault per omgeving |
| Defender for Cloud | Gratis CSPM-aanbevelingen aan; betaalde plannen alleen Defender for Storage (malware scanning) indien gekozen |
| Beheer | Portal-toegang tot Azure alleen voor 2–3 personen met MFA; PIM niet nodig (licentie) — wel minimaal "Contributor" op resourcegroepniveau per omgeving |
| Pipelines | GitHub OIDC-federatie (geen client secrets), environment protection rules voor Acc/Prod |

## 12. Logging en audit

- **Auditlog** (append-only, DB-rechten) voor: gebruiker/rol/permissions gewijzigd, lid goedgekeurd/afgewezen, account/ticket geblokkeerd/ingetrokken/heruitgegeven, QR-credential gewijzigd, sync/imports uitgevoerd, nieuws gepubliceerd, push verstuurd, startnummer/status/gemeten lengte gewijzigd, aanrijtijden geïmporteerd/gepubliceerd, exports met persoonsgegevens, betalingen/refunds, config-wijzigingen, AVG-acties.
- **Applicatielogs** (App Insights): geen wachtwoorden, tokens, push-tokens, QR-payloads, volledige e-mailadressen of telefoonnummers. Gebruikers-ID's als pseudoniem (UUID); IP-adressen gehasht (App Insights IP-masking staat standaard aan).
- Bewaartermijn logs: App Insights 30 dagen (standaard), auditlog volgens §13.

## 13. Privacy / AVG

### Rollen en grondslagen

| Verwerking | Grondslag | Opmerking |
|---|---|---|
| Ledenadministratie, toegang, communicatie aan leden | Uitvoering overeenkomst (lidmaatschap) | |
| Pushmeldingen niet-transactioneel | Toestemming (OS-permissie + voorkeuren) | |
| Optochtinschrijving (contactpersoon) | Uitvoering overeenkomst/verzoek | |
| Scanlogs | Gerechtvaardigd belang (toegangscontrole, veiligheid) | Minimaliseren, korte bewaartermijn |
| Foto's van evenementen | Gerechtvaardigd belang / toestemming bij herkenbare portretten en minderjarigen | Beleid + verwijderverzoek |
| Betalingen | Overeenkomst + wettelijke plicht (fiscaal 7 jaar) | |
| Minderjarigen < 16 | Toestemming ouder/verzorger (UAVG art. 5) | Guardian-relatie verplicht |

### Bewaartermijnen (voorstel, configureerbaar in `RetentionPolicy`)

| Gegevens | Termijn | Actie |
|---|---|---|
| Actieve leden | Duur lidmaatschap | — |
| Oud-leden (inactief) | 2 jaar na einde | Anonimiseren (naam → "Oud-lid #…", contact leeg); jubileumhistorie (join_year) blijft geanonimiseerd bruikbaar |
| Overleden leden | 1 jaar, daarna als oud-lid; optioneel "in memoriam"-vermelding op verzoek familie | |
| Afgewezen lidmaatschapsaanvragen | 6 maanden | Verwijderen |
| Ouderaccounts zonder actief minderjarig kind | 6 maanden na het beëindigen van de laatste relatie | Verwijderen (Entra + lokaal) |
| Optochtinschrijvingen via het webformulier (niet-leden) | Zie optochtinschrijvingen | Contactgegevens anonimiseren |
| Afgewezen/verlopen accountverzoeken | 3 maanden | Verwijderen |
| LoginHistory | 12 maanden | Verwijderen |
| TicketScan | 2 carnavalsjaren | Aggregeren (tellingen per uur/dag) en detail verwijderen |
| Tickets / orders / payments | 7 jaar (fiscaal) | Daarna verwijderen |
| Optochtinschrijvingen | 3 jaar (historie, prijzen) | Contactgegevens anonimiseren; groepsnaam/onderwerp/categorie blijven |
| Optochtdocumenten | 1 jaar na optocht | Verwijderen |
| Notificaties/recipients | 12 maanden | Verwijderen |
| Push-tokens | Bij uitschrijven/ongeldig; inactief > 6 maanden | Verwijderen |
| AuditLog | 2 jaar; financiële en AVG-acties 7 jaar | Verwijderen |
| Foto's | Onbeperkt voor archief, tenzij verwijderverzoek | Handmatig |
| App Insights | 30 dagen | Automatisch |
| Backups | Volgens backup-retentie (PITR 7–14 dagen, LTR 12 maanden) | Automatisch |

### Rechten van betrokkenen
- **Inzage/export**: `POST /me/privacy/export` → JSON + PDF-samenvatting (profiel, rollen, tickets, scans, meldingen, inschrijvingen) via tijdelijke downloadlink.
- **Rectificatie**: NAW via secretariaat (e-Boekhouden is de bron); lokale gegevens in app/portal.
- **Verwijderen**: account verwijderen door de gebruiker; volledige verwijdering/anonimisering via `member.privacy` met controle op bewaarplichten.
- **Foto's/portretrecht**: meldknop "Verwijder deze foto" in de app → melding naar de redactie → foto direct verborgen tot beoordeling.
- **Verwerkersovereenkomsten**: Microsoft (DPA via Product Terms), Mollie, e-Boekhouden, Expo (EAS/push), eventueel mailprovider. Privacyverklaring bijwerken (OQ-50).
- **Dataresidentie**: Azure West Europe (NL); Entra External ID kiest een EU-datalocatie; Expo Push (VS) verwerkt alleen push-token + berichttekst → geen gevoelige inhoud in pushberichten.

## 14. Secure development lifecycle

- **Publieke GitHub-repository** (B-03): branch protection op `main`, verplichte PR-review (≥ 1), CI groen.
- Pipelines: build, unit/integration tests, lint (dotnet format, ESLint), dependency review, gitleaks, OpenAPI-diff, **CodeQL**, **GitHub secret scanning + push protection**, Dependabot.
- Production-deploy alleen via de GitHub Environment `production` met **required reviewers**; de OIDC-federatie naar Azure geldt alleen voor het subject `environment:production`.
- Omdat de code openbaar is: nooit echte persoonsgegevens in testdata, fixtures of issues; securitymeldingen via een privékanaal (SECURITY.md, GitHub private vulnerability reporting).
- Jaarlijks: pentest-light / OWASP ZAP-baseline tegen Acc vóór carnaval; restore-test; toegangsreview rollen (wie heeft welke rechten).
- Securityincidenten: procedure in SECURITY.md (melden, beoordelen, datalek-meldplicht AP binnen 72 uur).
