# Runbook — App Store-review

Apple vroeg bij de eerste inzending (oktober 2026, richtlijn 2.1, *Information Needed*) om een schermopname en uitleg.
Deze tekst staat ook in App Store Connect → *App Review Information → Notes*, zodat volgende inzendingen dezelfde
informatie hebben. Bij een nieuwe inzending: tekst controleren (diensten, functies) en zo nodig bijwerken.

## Schermopname maken

Op een fysieke iPhone met de nieuwste iOS en de **productie-build** (via TestFlight), Bedieningspaneel → Schermopname,
ongeveer 2–3 minuten:

1. Op het beginscherm van de iPhone beginnen en de app openen.
2. Zonder account: Home → Programma (een activiteit openen) → Nieuws (een bericht openen) → Optocht → Meer → Foto's.
3. Registratie: Meer → Lid worden (formulier tot de stap met de e-mailcode) en kort "Ik ben al lid – account aanvragen".
4. Inloggen: Meer → Inloggen met `app@vrolijkedrammers.nl` en het reviewwachtwoord.
5. Ingelogd: Mijn QR → Munten → Kaarten → pronkzitting bestellen tot de betaalpagina van Mollie (niet betalen).
   De QR-codes blijven tijdens de opname leeg; dat is de beveiliging (zie de tekst hieronder). Toch even openen.
6. Account verwijderen: Meer → Mijn gegevens → Account verwijderen, tot het bevestigingsscherm. **Niet bevestigen**,
   anders is het reviewaccount weg.

Geen prijzen of persoonlijke gegevens van echte leden in beeld brengen.

## Tekst voor Apple

Plakken in *Notes* en als antwoord op het bericht. `[reviewwachtwoord]` vervangen door het echte wachtwoord (alleen in
App Store Connect, nooit in deze repository).

> **1. Screen recording**
> Attached: a recording on a physical iPhone (latest iOS) of the submitted build, starting from launch. It shows browsing
> without an account, registration ("Lid worden" = become a member, and "account aanvragen" = request an account for
> existing members), login with the demo account, the member features (member ticket QR, drink tokens, ticket ordering up
> to the payment page) and the account deletion flow (Meer → Mijn gegevens → Account verwijderen).
>
> **Note on the QR screens:** while the screen is being recorded, the member ticket ("Mijn QR") and drink token ("Munten")
> screens intentionally do not show the QR code. This is a security measure: the app blocks screen recording and
> screenshots of these codes so a valid code cannot be copied and shared with someone else. The codes are also bound to
> the device and refresh every 30 seconds. On a device that is not recording, the QR code is shown normally.
>
> **2. Purpose and audience**
> De Vrolijke Drammers is the carnival association of Loil, a village in the Netherlands (founded 1958). The app is the
> association's public information channel for everyone in and around Loil: the carnival programme and events, news,
> photo albums, and the carnival parade (route, times, participants, results). All of this works without an account.
> Members of the association can log in for member features: a personal QR member ticket for entry during carnival,
> ordering tickets for the association's evening show ("pronkzitting"), buying drink tokens in advance to collect at the
> bar, and notifications. It replaces paper member cards, printed programmes and cash handling at the bar.
>
> **3. Setup and access**
> No setup is needed for public content. To see member features, go to Meer (More) → Inloggen and sign in with:
> E-mail: app@vrolijkedrammers.nl — Password: [reviewwachtwoord]
> Sign-in uses e-mail and password in the system browser sheet (Microsoft Entra External ID). New users either apply for
> membership in the app ("Lid worden", approved by the board) or, as existing members, request an account; the board
> approves before access is granted. Account deletion: Meer → Mijn gegevens → Account verwijderen. The app has no
> user-generated content visible to other users; news, events and photos are published by the association's board only.
> As explained under 1, the QR codes are hidden while screen recording is active; this is intended behaviour.
>
> **4. External services**
> Microsoft Azure (hosting and database, EU region), Microsoft Entra External ID (authentication), Mollie (payment
> processor for event tickets and drink tokens, via iDEAL in the browser), Apple Push Notification service via Expo
> (notifications), Azure Communication Services (transactional e-mail). No AI services, no advertising or analytics SDKs.
>
> **5. Regional differences**
> The app functions the same in all regions. Its content is in Dutch and is intended for the Netherlands and the
> neighbouring border region.
>
> **6. Regulated industry / third-party material**
> Not applicable. The app is not in a regulated industry. All content (logo, photos, texts) belongs to the association.
>
> **Payments:** tickets and drink tokens give access to real-world events and are redeemed physically at the venue
> (Guideline 3.1.3(e), goods and services consumed outside the app). They are paid via Mollie in the browser; no digital
> content is unlocked by these payments.

## Aandachtspunten

- **3.2 (alleen voor een organisatie)**: de tekst benadrukt dat programma, nieuws, optocht en foto's zonder account
  werken. Blijft Apple bij een "organisatie-app", dan is *Unlisted App Distribution* het alternatief.
- **3.1.1 (In-App Purchase)**: kaarten en munten zijn diensten buiten de app (3.1.3(e)); geen Apple-betaling nodig.
  Komt er ooit digitale content tegen betaling in de app, dan geldt dat niet meer.
- **Beschikbaarheid**: App Store Connect → *Pricing and Availability*: Nederland (eventueel België en Duitsland).
