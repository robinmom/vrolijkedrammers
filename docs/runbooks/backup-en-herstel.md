# Runbook: Back-up en herstel (Prod)

Ingesteld via Bicep (`infra/env/prod.bicepparam`, 2026-10-17), volgens docs/08 §8.

| Onderdeel | Back-up | Bewaren |
| --- | --- | --- |
| Database `sqldb-dvd` | Point-in-time restore (elk moment, tot op ±10 minuten) | 14 dagen |
| | Langetermijn: wekelijks en maandelijks | 4 weken en 12 maanden |
| | Ook in een tweede regio (geo-redundant) | |
| Opslag `stdvdprod` (foto's, documenten) | Soft delete en versiebeheer | 30 dagen |
| | Hele opslag terug naar een tijdstip | 14 dagen |
| Key Vault (geheimen) | Soft delete met purge protection | 90 dagen |
| Code en infrastructuur | GitHub en Bicep | |
| Sloten | Database-server, opslag en Key Vault kunnen niet zomaar worden verwijderd (`infra/prod/locks.sh`) | |

## Eenmalig na de deploy

1. Merge `kosten-omlaag` en `backup-prod` en draai **Deploy → prod**.
2. Hersteltest (zie hieronder). Doe deze **vóór** de sloten: met een slot op de server kan de testkopie niet meer weg.
3. Sloten zetten (beheerder met Owner):

   ```
   az login --tenant <tenant-id-vereniging>
   AZURE_SUBSCRIPTION_ID=<id> infra/prod/locks.sh
   ```

## Database terugzetten

**Verkeerde gegevens (bijv. een import):** zet een kopie terug naar het moment vóór de fout, vergelijk en herstel gericht.

```
az sql db restore -g rg-dvd-prod -s sql-dvd-prod -n sqldb-dvd --dest-name sqldb-dvd-herstel \
  --time "2026-10-17T18:00:00Z" --service-objective S1
```

Daarna kun je gegevens uit `sqldb-dvd-herstel` overnemen (via de firewallregel en een query, zoals bij de
diagnoses). Of je zet in `ConnectionStrings__Drammers` de database om naar de herstelde kopie. Ruim de kopie daarna
op (eerst het slot van de server halen).

**Ouder dan 14 dagen:** gebruik een langetermijnback-up.

```
az sql db ltr-backup list -l swedencentral -s sql-dvd-prod -d sqldb-dvd -o table
az sql db ltr-backup restore --backup-id "<id uit de lijst>" --dest-database sqldb-dvd-herstel \
  --dest-server sql-dvd-prod --dest-resource-group rg-dvd-prod --service-objective S1
```

**Regio-uitval:**
- Zet de database met geo-restore terug in een andere regio: `az sql db geo-backup restore`.
- Rol de infrastructuur daar uit met Bicep, met een andere `location`.
- Reken op 4–8 uur.

## Bestanden terughalen

- **Eén bestand verwijderd of overschreven:** in de Azure Portal bij `stdvdprod` → Containers → "Verwijderde blobs
  weergeven" of "Versies" → terugzetten.
- **Veel tegelijk misgegaan:** de hele opslag terug naar een tijdstip (binnen 14 dagen). Let op: dat geldt voor alle
  bestanden.

  ```
  az storage blob restore --account-name stdvdprod -g rg-dvd-prod --time-to-restore "2026-10-17T18:00:00Z"
  ```

## Geheimen

Een verwijderd geheim zet je terug met `az keyvault secret recover --vault-name kv-dvd-prod -n <naam>`. Is een geheim
uitgelekt: maak een nieuwe waarde bij de leverancier (Mollie, e-Boekhouden, Expo, Facebook) en zet die met
`infra/prod/set-secret.sh prod <naam>`. Herstart daarna de app.

## Mobiele app

Builds zijn reproduceerbaar via EAS. Bewaar een kopie van de Android-uploadsleutel (EAS → Credentials → Android →
Download keystore) en de inlog van het Apple Developer-account in de wachtwoordkluis van het bestuur.

## Hersteltest (elk jaar in oktober)

1. Zet een kopie terug van een uur geleden (`sqldb-dvd-herstel`, zie hierboven).
2. Controleer dat de belangrijkste tabellen gevuld zijn (aantal leden, inschrijvingen, bestellingen).
3. Verwijder de kopie: `az sql db delete -g rg-dvd-prod -s sql-dvd-prod -n sqldb-dvd-herstel --yes`.
4. Haal één testbestand terug uit soft delete.
5. Leg vast in de tabel hieronder.

| Datum | Wat | Resultaat | Door |
| --- | --- | --- | --- |
| | | | |
