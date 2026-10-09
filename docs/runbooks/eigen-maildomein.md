# Runbook — Eigen maildomein (vrolijkedrammers.nl als afzender)

Doel: e-mail uit de app, het portal en de website komt van `secretaris@`, `optocht@`, `penningmeester@` of
`voorzitter@vrolijkedrammers.nl` in plaats van `DoNotReply@…azurecomm.net`, en de verzendlimiet van ~10 per uur kan
omhoog (alleen mogelijk met een eigen domein).

Welke afzender een bericht krijgt: de afzender die het bericht vraagt, anders die van het antwoordadres (een bericht
met antwoordadres `penningmeester@vrolijkedrammers.nl` komt ook van penningmeester@), anders
`DoNotReply@vrolijkedrammers.nl` (bijv. het contactformulier, waar het antwoordadres de bezoeker is).

De mailboxen in Microsoft 365 blijven werken; MX-records worden niet aangeraakt.

## 1. Domein aanmaken (deploy)

Na het mergen van de PR: **Actions → Deploy → `prod`** (*Alleen infrastructuur* mag aan). Bicep maakt het domein
`vrolijkedrammers.nl` aan in `ecs-dvd-prod` met de vier afzenders. De samenvatting van de run toont een tabel met de
DNS-records (Domain, SPF, DKIM, DKIM2, DMARC). Er verandert nog niets aan het versturen.

## 2. DNS-records bij Cloud86

Neem de waarden uit de tabel van de run over:

| Record | Wat te doen |
|---|---|
| **Domain** (TXT op `@`, `ms-domain-verification=…`) | **Toevoegen** als extra TXT-record. De bestaande TXT-records (Google, SPF) blijven staan. |
| **SPF** | **Niet** als tweede SPF-record toevoegen (twee SPF-records = beide ongeldig). Het bestaande record aanvullen tot: `v=spf1 include:spf.protection.outlook.com include:spf.protection.azurecomm.net -all` |
| **DKIM** (CNAME `selector1-azurecomm-prod-net._domainkey`) | Toevoegen. |
| **DKIM2** (CNAME `selector2-azurecomm-prod-net._domainkey`) | Toevoegen. |
| **DMARC** | Bestaat al (`v=DMARC1; p=quarantine;`); laten staan. |

Claude controleert daarna of de records overal zichtbaar zijn.

## 3. Verifiëren (Azure, door de beheerder)

```bash
az login --tenant <tenant-id-vereniging>
for type in Domain SPF DKIM DKIM2; do
  az communication email domain initiate-verification -g rg-dvd-prod --email-service-name ecs-dvd-prod \
    --domain-name vrolijkedrammers.nl --verification-type "$type"
done
# Na een paar minuten: alle vier "Verified"?
az communication email domain show -g rg-dvd-prod --email-service-name ecs-dvd-prod --domain-name vrolijkedrammers.nl \
  --query "verificationStates.{Domain:domain.status,SPF:spf.status,DKIM:dkim.status,DKIM2:dkim2.status}" -o table
```

(Eerste keer vraagt `az` om de extensie `communication` te installeren: ja.)

## 4. Koppelen en aanzetten (deploy)

GitHub → environment `prod` → variabele **`DVD_EMAIL_DOMAIN_VERIFIED=true`**, dan **Deploy `prod`**. Bicep koppelt het
domein aan `acs-dvd-prod` en zet `Email__CustomSenderDomain`; de samenvatting toont "Eigen maildomein: gekoppeld".
Mailing gaat vanzelf van 9 naar 90 mails per uur (Azure staat met een eigen domein standaard 100 per uur toe).

Testen: portal → Mailing → een testmail naar jezelf; afzender moet `secretaris@vrolijkedrammers.nl` zijn en de mail
mag niet in de spam belanden (kijk in de headers naar `spf=pass`, `dkim=pass`, `dmarc=pass`).

## 5. Hogere limiet aanvragen

Azure-portal → `acs-dvd-prod` → *Email* → quota, of een supportverzoek "Communication Services – Email sending
limits". Vermeld: vereniging, nieuwsbrief aan alle leden, geverifieerd eigen domein. Pas na stap 4.

## Terugdraaien

`DVD_EMAIL_DOMAIN_VERIFIED` weghalen en Deploy `prod`: de app verstuurt dan weer via `DoNotReply@…azurecomm.net`.
