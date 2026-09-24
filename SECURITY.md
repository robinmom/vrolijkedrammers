# Security

## Een kwetsbaarheid of datalek melden

Heb je een beveiligingsprobleem of mogelijk datalek gevonden in de app, de beheeromgeving of de API van De Vrolijke Drammers?

- Meld het **niet** via een openbaar issue (deze repository is openbaar). Gebruik GitHub *private vulnerability reporting* of mail.
- Mail naar het securityaanspreekpunt van de vereniging: **security@vrolijkedrammers.nl** *(adres nog in te richten, OQ-52)*.
- Beschrijf wat je zag, hoe je het kunt reproduceren en welke gegevens mogelijk geraakt zijn.

We bevestigen binnen 3 werkdagen (tijdens carnaval zo snel mogelijk). Test alleen tegen je eigen account; doe geen pogingen om gegevens van anderen in te zien, te wijzigen of diensten te verstoren.

## Incidentprocedure (intern)

1. **Melden**: aan de IT-beheerder en de secretaris.
2. **Beoordelen**: wat is geraakt (persoonsgegevens? minderjarigen? betalingen?), sinds wanneer, hoeveel personen.
3. **Beperken**: accounts/devices/tokens intrekken, secrets roteren (Key Vault), eventueel maintenance mode.
4. **Datalek?** → melden bij de Autoriteit Persoonsgegevens **binnen 72 uur** als er een risico is voor betrokkenen; betrokkenen informeren bij een hoog risico. Vastleggen in het datalekregister van de vereniging.
5. **Herstellen en evalueren**: oorzaak, maatregelen, update van het threat model.

## Afspraken voor ontwikkelaars

- **Geen secrets in Git.** Gebruik `dotnet user-secrets` lokaal en Key Vault in Azure. gitleaks draait als pre-commit hook en in CI; GitHub secret scanning + push protection staan aan (publieke repository, B-03).
- Geen productiedata in Development/Acceptance, en nooit echte persoonsgegevens in testdata, fixtures, issues of pull requests (openbare repository).
- Log geen wachtwoorden, tokens, push-tokens, QR-payloads of onnodige persoonsgegevens.
- Elk endpoint heeft een expliciete permission (`[RequirePermission]`) of is bewust publiek (`[AllowAnonymous]`).
- Dependencies worden bijgewerkt via Dependabot; kritieke kwetsbaarheden binnen 7 dagen patchen.
- Production-deploys alleen via de pipeline met goedkeuring.

## Ondersteunde versies

Alleen de laatste productieversie van de API en de laatste twee app-versies (afgedwongen via `min_app_version`) krijgen securityfixes.

Het volledige securitymodel staat in [docs/06-security.md](docs/06-security.md) en het threat model in [docs/11-threat-model.md](docs/11-threat-model.md).
