#!/usr/bin/env bash
# Sloten tegen per ongeluk verwijderen (docs/08 §8, 2026-10-17) op de database-server, de opslag en de Key Vault van
# Prod. Wijzigen (bijv. een deploy of een andere databasegrootte) blijft gewoon mogelijk; verwijderen kan pas nadat
# iemand met eigenaarsrechten het slot bewust weghaalt. Eenmalig, door een beheerder met Owner op de subscription
# (de deploy-identiteit mag geen sloten zetten). Idempotent.
#   az login --tenant <tenant-id-vereniging>
#   AZURE_SUBSCRIPTION_ID=<id> infra/prod/locks.sh            # sloten zetten
#   AZURE_SUBSCRIPTION_ID=<id> infra/prod/locks.sh --list     # bekijken
# Slot weghalen (alleen als je echt iets wilt verwijderen):
#   az lock delete -g rg-dvd-prod -n niet-verwijderen --resource <naam> --resource-type <type>
set -euo pipefail

: "${AZURE_SUBSCRIPTION_ID:?Zet AZURE_SUBSCRIPTION_ID}"
az account set --subscription "$AZURE_SUBSCRIPTION_ID"
RG=rg-dvd-prod
NOTES="Prod: niet verwijderen zonder overleg (back-ups, docs/08 §8). Slot bewust weghalen als het echt moet."

if [[ "${1:-}" == "--list" ]]; then
  az lock list -g "$RG" --query "[].{naam:name,niveau:level,op:id}" -o table
  exit 0
fi

STORAGE="$(az storage account list -g "$RG" --query "[0].name" -o tsv)"
for TARGET in "sql-dvd-prod|Microsoft.Sql/servers" "$STORAGE|Microsoft.Storage/storageAccounts" "kv-dvd-prod|Microsoft.KeyVault/vaults"; do
  NAME="${TARGET%%|*}"
  TYPE="${TARGET##*|}"
  az lock create -g "$RG" -n niet-verwijderen --lock-type CanNotDelete --resource "$NAME" --resource-type "$TYPE" --notes "$NOTES" -o none
  echo "Slot op $NAME ($TYPE)"
done

echo
echo "Klaar. Controle: AZURE_SUBSCRIPTION_ID=$AZURE_SUBSCRIPTION_ID infra/prod/locks.sh --list"
