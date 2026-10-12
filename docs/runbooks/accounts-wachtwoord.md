# Runbook: inloggen met wachtwoord en welkomstmails

Besluit van 2026-10-12. Elk lid krijgt direct een account, ook bij Microsoft (Entra External ID). Zelf een account
aanvragen of aanmaken kan niet meer. De welkomstmail legt uit hoe je de eerste keer inlogt: e-mailadres invullen, dan
**"Wachtwoord vergeten?"** kiezen, de code uit de mail invullen en zelf een wachtwoord kiezen.

Er staat geen wachtwoord in de mail. Entra External ID dwingt bij de eerste aanmelding geen nieuw wachtwoord af, dus een
wachtwoord in de mail zou blijven werken.

## Wat er verandert

| Situatie | Voorheen (e-mail + code) | Nu (`Accounts__PasswordSignIn=true`) |
|---|---|---|
| Account voor een lid (aanmelding goedgekeurd, portal "App-account aanmaken", ouder uitnodigen) | Account wacht tot het lid zelf een inlog maakt | De API maakt de inlog meteen, met een willekeurig wachtwoord dat niemand kent |
| Bestaande inlog met e-mail + code | n.v.t. | Krijgt bij de welkomstmail een wachtwoordinlog erbij |
| "Account aanvragen" in de app | Verzoek naar het bestuur | Knop weg; de API weigert verzoeken (409) |
| Nieuw lid uit de ledensync | Geen account | Account en welkomstmail, maar pas nadat de welkomstmails één keer zijn verstuurd |
| Wachtwoord kwijt | n.v.t. | Het lid kiest zelf "Wachtwoord vergeten?". Het bestuur kan op de pagina van het lid de welkomstmail opnieuw sturen |

## Stappen (eerst Dev, dan Prod)

1. **Entra.** De beheerder van de External ID-tenant voert dit uit:
   ```bash
   az login --tenant 260db5a1-e5b6-4388-9f6c-d9b02cb5578b --allow-no-subscriptions
   infra/entra/wachtwoord-inloggen.sh dev
   ```
   Het script doet drie dingen:
   - het maakt de user flow "DVD inloggen met wachtwoord" (zelf aanmelden uit);
   - het verplaatst het portal en de app van deze omgeving naar die flow;
   - het geeft de provisioning-app het recht `User-PasswordProfile.ReadWrite.All`.
2. **GitHub.** Zet in environment `dev` de variabele `DVD_PASSWORD_SIGN_IN` op `true`.
3. **Deploy.** Start de Deploy-workflow voor `dev`. De migratie `WelkomstmailAccounts` voegt `identity.User.welcome_sent_at` toe.
4. **Testen in Dev:**
   - Portal → lid zonder account → "App-account aanmaken". Er komt een mail met "Wachtwoord vergeten?".
   - In de app: Meer → Inloggen → e-mailadres → "Wachtwoord vergeten?" → code → eigen wachtwoord → ingelogd.
   - Een bestaand account (eerder ingelogd met een code): stuur de welkomstmail opnieuw via de pagina van het lid. Daarna
     werkt "Wachtwoord vergeten?" ook voor dit account.
   - Portal → Accountverzoeken → kaart "Welkomstmails". Controleer of de aantallen kloppen; nog niet versturen in Dev,
     tenzij je dat wilt.
5. **Prod.** Herhaal stap 1 t/m 3 met `prod`. Doe dit vlak voor de lancering: vanaf dat moment werkt alleen inloggen met
   een wachtwoord.
6. **Lancering:**
   - zet de apps in de stores live;
   - zet de feature flag `website.app` aan;
   - kies in het portal bij "Welkomstmails" **Welkomstmail naar iedereen sturen…**.

   De mails gaan één voor één, met 40 seconden ertussen. Dat blijft binnen de maillimiet van ongeveer 90 per uur.

## Welkomstmail naar iedereen: wie krijgt wat

- **Actieve leden (15 jaar of ouder) met een e-mailadres:** krijgen een account (als ze dat nog niet hadden) en de mail.
- **Kinderen** (jonger dan 15, of dansgarde zonder geboortedatum): geen eigen account. De ouder logt in via het eigen account.
- **Leden met het e-mailadres van een ander lid** (bijv. partners): geen tweede account. Wil zo iemand een eigen account,
  dan moet er eerst een eigen e-mailadres in het lid komen. Stuur daarna de welkomstmail via de pagina van het lid.
- **Accounts zonder lid** (ouders/verzorgers, beheerders): krijgen ook de mail.
- **Al een welkomstmail gehad:** wordt overgeslagen. Twee keer op de knop drukken stuurt dus niemand dubbel.

## Terugdraaien

1. Voer `infra/entra/wachtwoord-inloggen.sh <env> --terug` uit. Het portal en de app gaan dan terug naar "DVD aanmelden"
   (e-mail + code).
2. Zet `DVD_PASSWORD_SIGN_IN` uit en deploy.

Accounts die de API intussen heeft gemaakt, hebben alleen een wachtwoordinlog. Het is niet zeker dat zo'n account ook
werkt in de flow met e-mail + code. Test dat in Dev voordat je in Prod terugdraait.

## Controle bij problemen

- **"Wachtwoord vergeten?" ontbreekt op de inlogpagina.** Controleer in het Entra-admincentrum dat de user flow "DVD
  inloggen met wachtwoord" als methode *E-mail met wachtwoord* heeft en dat de app eraan gekoppeld is.
- **Een welkomstmail mislukt.** Het bericht `account.welcome` blijft in de outbox staan met `LastError`. Vaak gaat het om
  een Graph-fout over rechten: controleer de rechten van "DVD Provisioning (env)".
