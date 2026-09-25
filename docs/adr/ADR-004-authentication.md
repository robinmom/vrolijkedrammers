# ADR-004: Authenticatie

- **Status**: Geaccepteerd · 2026-09-24 (besluiten **B-02**: één tenant; **B-05**: alleen leden, zelfregistratie uit, zie ADR-014) · Bijgesteld 2026-09-25: **B-02-MFA**, geen Conditional Access (zie Decision)

## Context

Leden, ouders, groepsverantwoordelijken (soms niet-lid) en beheerders moeten veilig inloggen in de app, het portal en later de website. Wachtwoorden mogen nooit zichtbaar of verzendbaar zijn; resetten via tijdelijke tokens; brute-force-bescherming; sessies intrekbaar. Beheerders hebben MFA nodig. De app is React Native (ADR-001).

## Options considered

| Criterium | **Microsoft Entra External ID** | **Eigen authentication service** (ASP.NET Core Identity + OpenIddict/Duende) | Andere IdP (Auth0, Firebase Auth) |
|---|---|---|---|
| Wachtwoordopslag, reset, lockout | Door Microsoft beheerd (smart lockout, banned passwords) | Zelf bouwen/onderhouden (Argon2id, reset tokens, lockout) | Beheerd |
| MFA | Voor klantaccounts: e-mail-OTP, sms (kosten), passkey (FIDO2); **geen authenticator-app**. Conditional Access: "alle gebruikers" (met uitsluitingen) × geselecteerde apps (geverifieerd, Microsoft Learn "External tenant features", 2026-05) | Zelf bouwen (TOTP) | Beheerd (betaald) |
| Wachtwoordloos | E-mail-OTP ingebouwd | Zelf bouwen | Ja |
| React Native | OIDC + PKCE via systeembrowser (`expo-auth-session`), gebrande hosted pages. *Native auth-SDK alleen voor Swift/Kotlin/web (geverifieerd, Microsoft Learn 2026-06)* | Volledig eigen UI | SDK's beschikbaar |
| Azure-integratie | Native (Microsoft.Identity.Web, MSAL.js) | Goed | Via OIDC |
| Onderhoud/securityrisico | Laag | **Hoog** (eigen crypto-flows, patches, tokenservice) | Laag |
| Kosten | **Gratis tot 50.000 MAU** | Hosting + ontwikkeltijd | Gratis tiers beperkt; betaald bij groei |
| Dataresidentie | EU-datalocatie selecteerbaar | Eigen | Afhankelijk |

## Decision

**Microsoft Entra External ID** als identity provider, met **één external tenant** (`dvd`) voor alle omgevingen en **aparte app-registraties per omgeving** (Dev, Acc, Prod × API, app, portal). Dev/Acc-apps staan op *Require user assignment* (alleen toegewezen testers, groep `Testers`), en de Dev/Acc-API accepteert alleen tokens met de claim `environmentAccess` die de omgeving bevat (custom attribuut, via *Attributes & Claims* in het token) (B-02). **Zelfregistratie staat uit** (`isSignUpAllowed = false`); accounts worden uitsluitend door de backend via Microsoft Graph aangemaakt na goedkeuring (ADR-014, B-05). Verder:
- App: OIDC Authorization Code + PKCE via systeembrowser (`expo-auth-session`), met gebrande sign-in pages (logo, kleuren uit het design system).
- Portal: MSAL.js; aanmelden met een **eenmalige e-mailcode**. **Geen Conditional Access-policy** (B-02-MFA, 2026-09-25): beheerders gebruiken een eigen vrolijkedrammers.nl-adres waarvan de mailbox in Microsoft 365 met MFA is beveiligd, zodat misbruik van het portal eerst een mailbox met MFA vereist. Beheerrechten komen uit onze RBAC en worden geaudit. Access tokens van External ID bevatten geen `amr`, dus de API kan MFA niet zelf controleren. Passkeys zijn een latere verbetering.
- Bestuursleden gebruiken hetzelfde account voor app en portal (geen aparte workforce-tenant).
- Methoden: e-mail-OTP (standaard) en optioneel e-mail + wachtwoord (in te stellen via "Wachtwoord vergeten"); er gaan nooit wachtwoorden per mail.
- **Autorisatie (rollen/permissions) in onze eigen database** (07-rbac), gekoppeld via `oid` → `User.external_object_id`.
- Koppeling aan een lid ontstaat bij het aanmaken van het account (ADR-014): het account bestaat alleen voor een lid of voor een ouder/verzorger van een minderjarig lid.

## Reasoning

- Wachtwoordbeheer, lockout, reset en MFA zijn security-kritische functies die een kleine organisatie beter niet zelf bouwt.
- Kosten zijn nul bij onze schaal; de integratie met Azure/.NET is first-party.
- Rollen in de eigen DB, omdat ze vaak wijzigen, scoped zijn (groepen, kinderen) en in het portal beheerd moeten worden. Tokens blijven klein.

## Consequences

- Inloggen in de app opent een systeembrowser-sheet (ASWebAuthenticationSession/Custom Tabs). Dat is visueel minder naadloos dan native auth, maar veiliger (geen wachtwoordinvoer in de app) en gebruikelijk. Heroverwegen als Microsoft een React Native native-auth-SDK uitbrengt of via de native-auth REST API (hoger risico, eigen UI).
- Eén tenant; per omgeving drie app-registraties (API, mobiele app, portal) + één provisioning-app-registratie (Graph `User.ReadWrite.All`); gescript (Graph/`az`) en gedocumenteerd.
- Tenantbrede wijzigingen (user flow, branding, CA-policy, attributen) raken alle omgevingen tegelijk en kunnen niet apart getest worden → wijzigingen via een checklist met terugdraaiplan, buiten drukke periodes.
- Testaccounts staan in dezelfde tenant als echte accounts: herkenbaar (`test+…@`-adressen), in de groep `Testers`, nooit toegewezen aan de Prod-apps.
- E-mail-OTP als tweede factor na e-mail + wachtwoord is zwakker (dezelfde mailbox); daarom worden beheerders actief begeleid naar een passkey.
- Blokkeren/verloren telefoon: via Microsoft Graph `revokeSignInSessions` + onze device-revocatie.
- Account verwijderen: Graph-delete + lokale anonimisering.

## Security implications

- Geen wachtwoorden of hashes in onze systemen; resetten alleen via Entra-flows ("Wachtwoord opnieuw instellen").
- PKCE voorkomt het onderscheppen van de code; refresh-tokens met rotation; tokens in Keychain/Keystore.
- MFA voor beheerders verkleint het impactvolste risico (admin-takeover).
- Break-glass-accounts voor tenantbeheer met sterke MFA, gebruik gemonitord.
- Afhankelijkheid van één leverancier (beschikbaarheid); de impact is beperkt, omdat QR/scanner offline werkt en publieke content zonder login beschikbaar is.

## Cost implications

- € 0 tot 50.000 MAU. Sms-MFA is betaald (niet gebruiken; e-mail-OTP en passkey). Geen Conditional Access, dus geen add-on-kosten (OQ-69 vervallen).
- Een eigen auth-service zou naar schatting 3–6 weken extra ontwikkeling kosten plus blijvend securityonderhoud.
