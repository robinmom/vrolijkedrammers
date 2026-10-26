# Runbook – Inlogpagina in de huisstijl (en eigen inlogdomein)

> Status: v1.0 · 2026-10-26 · Hoort bij ADR-004/ADR-014 en [entra-external-id.md](entra-external-id.md).
> Bevat **geen** secrets. Deze repository is openbaar.

## Welk scherm is dit?

Als een lid in de app (of beheerder in het portal) op **Inloggen** tikt, opent de **gehoste inlogpagina van Entra
External ID** in de browser — niet een scherm uit onze app-code. Het adres is:

| Omgeving | Inlogadres (tenant) |
|---|---|
| **Prod** (winkelversie van de app) | `vrolijkedrammersapp.ciamlogin.com` |
| Dev/test (TestFlight, dev-build) | `vrolijkedrammersdev.ciamlogin.com` |

De winkelversie gebruikt dus **automatisch** de prod-tenant; echte gebruikers krijgen nooit de dev-inlogpagina. Wat de
pagina laat zien (logo, kleuren, teksten en de URL) stel je in de **Entra-tenant** in — daar is geen app-build voor nodig.

Twee dingen "verraden" nu nog de techniek/omgeving, en die lossen we hieronder op:

1. **De opmaak** van de pagina (zonder huisstijl oogt hij kaal/technisch) → **Deel A: huisstijl**.
2. **De URL** `vrolijkedrammersapp.ciamlogin.com` (een Microsoft-adres, geen `vrolijkedrammers.nl`) → **Deel B: eigen
   inlogdomein**.

De app gebruikt browser-gedelegeerd inloggen (`WebBrowser` + Entra), dus de huisstijl uit deze runbook werkt voor
zowel de app als het portal.

---

## Deel A – Huisstijl (company branding)

Snelste winst, geen DNS nodig. Microsoft-documentatie:
[Customize branding (External ID)](https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-customize-branding-customers).

### Stappen (per tenant, dus apart voor prod en eventueel dev)

1. Ga naar het [Entra-admincentrum](https://entra.microsoft.com) en wissel naar de tenant **De Vrolijke Drammers App**
   (`vrolijkedrammersapp.onmicrosoft.com`, prod). Rol: Global Administrator.
2. **Identity → User experiences → Company branding** (soms onder *Overzicht → Vormgeving/Branding*).
3. Kies **Customize** (of *Configure* bij de standaard-ervaring) en vul in:
   - **Achtergrond**: effen kleur **warm-wit `#FAFAF7`** (of een rustige achtergrondafbeelding in de huisstijl). De
     achtergrondkleur is ook wat kort te zien is tijdens het laden.
   - **Banner-logo**: het ronde Drammers-logo (bron: `src/Drammers.Website/wwwroot/img/logo.png`). Het admincentrum
     toont de vereiste maat/bestandsgrootte per veld — houd die aan (banner-logo is klein en breed; lever een nette PNG
     met transparante achtergrond).
   - **Vierkant logo** (licht én donker, als het veld er is): hetzelfde ronde logo.
   - **Favicon**: `apps/mobile/assets/favicon.png` of het ronde logo op klein formaat.
   - **Tekst onderaan de pagina** (sign-in page footer/tekst): bijv. "Carnavalsvereniging De Vrolijke Drammers · Loil".
   - Laat **zelfregistratie uit** (dat regelen wij via welkomstmails; ADR-014) en toon alleen wat nodig is.
4. **Opslaan**. De wijziging is vrijwel direct zichtbaar op een volgende inlogpoging (soms enkele minuten).

### Huisstijl-waarden (design system)

| Rol | Kleur |
|---|---|
| Drammers-rood (accent/knoppen) | `#ED0012` |
| Loils-blauw | `#087BC1` |
| Donkerblauw (tekst) | `#123047` |
| Warm-wit (achtergrond) | `#FAFAF7` |
| Accentgeel | `#F4B942` |

> Tip: configureer de huisstijl **vóór** je verder test — nieuwe inlogpagina's pakken de branding dan meteen op.

### Controle

Open de app (of het portal) en start een inlog. De pagina toont nu het logo, de warm-witte achtergrond en de
verenigingstekst in plaats van een kale Microsoft-pagina.

---

## Deel B – Eigen inlogdomein (`login.vrolijkedrammers.nl`)

Hiermee verdwijnt `*.ciamlogin.com` uit de URL; gebruikers zien dan `login.vrolijkedrammers.nl`. Vereist **Azure Front
Door** als reverse proxy. Microsoft-documentatie:
[Enable custom URL domains (External ID)](https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-custom-url-domain)
(algemeen beschikbaar sinds dec 2024).

### Benodigdheden

- Beheer over DNS van `vrolijkedrammers.nl` (om een CNAME en verificatierecord te zetten).
- Een subdomein, voorstel: **`login.vrolijkedrammers.nl`**.
- Een Azure Front Door-profiel (Standard volstaat) in de Azure-subscription van de vereniging.

### Stappen

1. **Domein toevoegen en verifiëren in de tenant**
   - Entra-admincentrum (prod-tenant) → **Identity → Settings → Domain names** → voeg `login.vrolijkedrammers.nl` toe.
   - Volg de verificatie (een TXT-record bij de DNS van `vrolijkedrammers.nl` zetten). Wacht tot "Verified".
2. **Azure Front Door aanmaken**
   - Azure-portal → **Front Door and CDN profiles → Create**.
   - Origin-type: **Custom**; origin-hostnaam: **`vrolijkedrammersapp.ciamlogin.com`**.
   - Noteer de **front-end hostnaam** van Front Door (iets als `dvd-login-xxxx.z01.azurefd.net`).
3. **Custom URL-domein koppelen in Entra**
   - Entra → **Identity → Settings → Domain names → Custom URL domains** → **Add custom URL domain** → kies
     `login.vrolijkedrammers.nl`.
4. **DNS zetten** bij `vrolijkedrammers.nl`:
   - **CNAME**: `login` → de Front Door front-end hostnaam uit stap 2.
   - Eventueel het door Front Door/Entra gevraagde **TXT-/validatierecord** voor het aangepaste domein.
   - Wacht tot de certificaten (managed TLS via Front Door) en de koppeling actief zijn.
5. **App/infra laten wijzen naar het eigen domein**
   - Zet in [`infra/env/prod.bicepparam`](../../infra/env/prod.bicepparam) de authority om:
     - van `https://vrolijkedrammersapp.ciamlogin.com/<tenant-id>/v2.0`
     - naar `https://login.vrolijkedrammers.nl/<tenant-id>/v2.0`
   - Uitrollen (deploy). De app haalt de authority op via `GET /api/v1/app-auth-config`, dus daarna opent de app vanzelf
     de inlogpagina op het eigen domein. (Dit kan Claude voor je doen zodra het domein actief is.)

### Belangrijk

- Het oude adres `vrolijkedrammersapp.ciamlogin.com` blijft werken; **bestaande sessies worden ongeldig** bij de
  overstap, dus iedereen logt één keer opnieuw in.
- Doe dit **eerst op dev** (eigen dev-tenant) als je wilt oefenen; prod als het werkt.
- Houd de redirect-URI's van de app-registraties in de gaten: die wijzen naar de app (`drammers://auth`), niet naar het
  inlogdomein, dus die hoeven meestal niet te wijzigen.

---

## Samengevat

| Doel | Oplossing | App-build nodig? | DNS nodig? |
|---|---|---|---|
| Pagina oogt als de vereniging | Deel A – company branding | Nee | Nee |
| URL toont `login.vrolijkedrammers.nl` i.p.v. `*.ciamlogin.com` | Deel B – eigen inlogdomein (Front Door) | Nee | Ja |

Beide stel je in het Entra-admincentrum/Azure-portal in (jouw account). Claude kan de runbook bijwerken, helpen met de
DNS/Front Door-stappen en de `prod.bicepparam`-authority omzetten zodra het domein actief is.
