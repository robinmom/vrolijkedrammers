# Runbook: kaartverkoop en Mollie (fase 19)

> **Status:**
> - Dev: de test-sleutel staat sinds 30-09-2026 in `kv-dvd-dev`.
> - Acc en productie: nog niet gezet. Productie krijgt de live-sleutel.

## Mollie-sleutel in Key Vault

De API leest de sleutel uit Key Vault. Hij staat niet in de repository, niet in app-settings en komt niet via Claude.

1. Log in bij Mollie (my.mollie.com) → **Developers → API-keys**.
2. Kies de sleutel:
   - Dev en Acc: de **test**-sleutel (`test_…`);
   - productie: alleen de **live**-sleutel (`live_…`).
3. Zet de sleutel als secret **`mollie-api-key`** in de Key Vault van de omgeving, in een eigen terminal. Je hebt de rol *Key Vault Administrator* of *Key Vault Secrets Officer* op de vault nodig:

   ```bash
   read -rs MOLLIE_KEY   # plakken en Enter; niets zichtbaar, niet in de shellhistorie
   az keyvault secret set --vault-name kv-dvd-<omgeving> --name mollie-api-key --value "$MOLLIE_KEY" --output none
   unset MOLLIE_KEY
   ```

   Controleren zonder de waarde te tonen: `az keyvault secret list --vault-name kv-dvd-<omgeving> --query "[?name=='mollie-api-key'].attributes.enabled"`.
4. Klaar. Binnen tien minuten gebruikt de API de nieuwe sleutel (hij bewaart de sleutel kort in het geheugen). Opnieuw starten is niet nodig.

Zonder sleutel geeft bestellen met betalen de melding "Online betalen is nog niet ingesteld". Gratis groepskaarten en contante bestellingen in het portal werken dan wel.

## Webhook

De API geeft zelf de webhook-URL mee aan Mollie: `https://<api-host>/api/v1/payments/mollie/webhook`. In het Mollie-dashboard hoeft niets ingesteld te worden.

De webhook bevat alleen het betalings-id. De status wordt altijd bij Mollie opgehaald, dus een vervalste melding doet niets.

## Testen in de testmodus

1. Zet in het portal onder **Verkoop** (Pronkzitting, Dagkaarten, Activiteiten of Munten) een product op "Te koop".
2. Bestel in de app of op de webpagina. Kies op de testpagina van Mollie de status "Paid".
3. De bestelling staat in het portal op **Betaald** en de koper krijgt een e-mail met de link naar de QR.
4. Test ook "Failed" en "Canceled". De plaatsen komen dan direct weer vrij.

## Webpagina voor de website

- De openbare kaartverkoop staat op `https://<api-host>/kaarten/`. Link ernaar vanaf vrolijkedrammers.nl.
- Gasten kopen daar kaarten (geen munten) en betalen met iDEAL.
- Na het betalen komen ze op `/kaarten/bestelling/`, met de QR. Dezelfde link staat in de e-mail.

## Reconciliatie met de penningmeester

- **Portal:** elke pagina onder Verkoop toont de omzet en per bestelling de betaalwijze (iDEAL, contant, gratis).
- **Mollie:** het dashboard toont de betalingen met de omschrijving `<product> · bestelling <nummer>`.
- Nooit terugbetalen. Een geannuleerde bestelling blijft in Mollie als betaald staan en staat in het portal als Geannuleerd.
