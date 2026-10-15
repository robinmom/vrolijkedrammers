# Runbook — Mailings via de mailbox in Microsoft 365

**Waarom.** Mailings via Azure Communication Services (ACS) belandden bij Outlook.com in Ongewenst. SPF, DKIM en DMARC
slaagden wel (SCL 5, `SpamFilterAuthJ`). ACS verstuurt via gedeelde servers met weinig reputatie.

**Wat verandert.** Mailings (nieuwsbrieven, uitnodigingen en hun testmail) van voorzitter@ en secretaris@ gaan via de
echte mailbox in Microsoft 365, met Microsoft Graph. De koppen voor afmelden met één klik gaan mee.

- Elke verstuurde mail staat ook bij **Verzonden items** van die mailbox.
- Alle andere mail (welkomstmails, facturen, optocht, contactformulier) blijft via ACS gaan.
- Heeft de app (nog) geen rechten op de mailbox, dan gaat de mailing automatisch via ACS. Er gaat dus niets verloren.

Limieten van Microsoft 365: 30 berichten per minuut en 10.000 ontvangers per dag per mailbox. De mailing verstuurt
standaard 90 per uur, dus ruim daaronder.

## 1. Rechten op de mailboxen

Dit doe je zelf, met een beheerdersaccount van Microsoft 365.

Eenmalig installeren:

```
brew install powershell
pwsh -Command "Install-Module ExchangeOnlineManagement -Scope CurrentUser"
```

Dan, met `az login` actief:

```
pwsh infra/m365/mailbox-verzenden.ps1
```

Het script:

1. zoekt de managed identity van `app-dvd-api-prod` op;
2. controleert dat voorzitter@ en secretaris@ echte mailboxen zijn, geen alias of doorstuuradres;
3. geeft de app in Exchange de rol **Application Mail.Send**, alleen voor die twee mailboxen (RBAC for
   Applications);
4. toont per mailbox of het versturen is toegestaan.

**Niet doen:** in Entra de API-machtiging `Mail.Send` aan de managed identity geven. Die geldt voor álle mailboxen.

## 2. Aanzetten

1. Ga in GitHub naar Settings → Secrets and variables → Actions → Variables.
2. Zet `DVD_EMAIL_MAILBOX_SENDERS` op `voorzitter,secretaris`.
3. Draai **Actions → Deploy → prod**. *Alleen infrastructuur* mag aan.

Testen:

1. Stuur in het portal een **Testmail naar mij** van een mailing met afzender Voorzitter.
2. Controleer dat de mail in **Verzonden items** van voorzitter@ staat.
3. Bekijk in de berichtbron van de ontvangen mail de `List-Unsubscribe`-kop.

Staat de mail niet bij Verzonden items, dan is hij via ACS gegaan. De rechten uit stap 1 kunnen tot een uur nodig
hebben. Application Insights toont dan de waarschuwing "Mailbox in Microsoft 365 niet beschikbaar".

**Terugzetten:** maak `DVD_EMAIL_MAILBOX_SENDERS` leeg en deploy opnieuw.

## 3. DKIM voor Microsoft 365

Mail die via Microsoft 365 gaat, wordt pas met ons eigen domein ondertekend als DKIM daar aanstaat. Zonder DKIM slaagt
DMARC alleen op SPF. Dit geldt ook voor gewone mail vanuit Outlook.

1. Draai `pwsh infra/m365/mailbox-verzenden.ps1 -Dkim`. Het script toont twee CNAME-records.
2. Zet de twee CNAME-records bij Cloud86:
   - `selector1._domainkey`
   - `selector2._domainkey`

   De bestaande records blijven staan: SPF, DMARC en de `selector1/2-azurecomm-prod-net`-records van ACS.
3. Wacht tot de records zichtbaar zijn. Daarna:

   ```
   pwsh infra/m365/mailbox-verzenden.ps1 -Dkim -Enable
   ```
