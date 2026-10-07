#!/usr/bin/env bash
# Heft de Acc-omgeving op (besluit 2026-10-07: alleen Dev en Prod). Eenmalig, door een beheerder met Owner op de
# subscription. Verwijdert:
#   - resource group rg-dvd-acc (database, Key Vault, opslag, mail, logging). De Key Vault heeft purge protection en
#     blijft 90 dagen als verwijderd bewaard (kost niets, de naam kv-dvd-acc is dan bezet);
#   - de deploy-identiteit id-dvd-github-acc met haar roltoewijzingen, en de groep sg-dvd-sql-admin-acc;
#   - met --entra ook de app-registraties "DVD … (acc)" in de External ID-tenant.
#   az login --tenant <tenant-id-vereniging>
#   AZURE_SUBSCRIPTION_ID=<id> infra/bootstrap/remove-acc.sh            # Azure
#   az login --tenant 260db5a1-e5b6-4388-9f6c-d9b02cb5578b --allow-no-subscriptions
#   infra/bootstrap/remove-acc.sh --entra                                 # app-registraties
# Daarna met de hand: GitHub → Settings → Environments → "acc" verwijderen. Idempotent.
set -euo pipefail

if [[ "${1:-}" == "--entra" ]]; then
  HERE="$(cd "$(dirname "$0")" && pwd)"
  # shellcheck source=infra/entra/lib.sh
  source "$HERE/../entra/lib.sh"
  use_ciam_tenant
  for NAME in "DVD API (acc)" "DVD Beheerportal (acc)" "DVD App (acc)" "DVD Provisioning (acc)"; do
    ID="$(find_by_name "$GRAPH/applications" "$NAME" id)"
    if [[ -n "$ID" ]]; then
      graph --method delete --url "$GRAPH/applications/$ID"
      echo "Verwijderd: $NAME"
    else
      echo "Bestaat niet (meer): $NAME"
    fi
  done
  exit 0
fi

: "${AZURE_SUBSCRIPTION_ID:?Zet AZURE_SUBSCRIPTION_ID}"
az account set --subscription "$AZURE_SUBSCRIPTION_ID"

echo "==> Resources in rg-dvd-acc"
if [[ "$(az group exists -n rg-dvd-acc)" == "true" ]]; then
  az resource list -g rg-dvd-acc --query "[].name" -o tsv | sed 's/^/    /'
  read -r -p "Resource group rg-dvd-acc met alles erin verwijderen? Typ ACC om te bevestigen: " CONFIRM
  [[ "$CONFIRM" == "ACC" ]] || { echo "Afgebroken." >&2; exit 1; }
  az group delete -n rg-dvd-acc --yes
  echo "    rg-dvd-acc verwijderd"
else
  echo "    bestaat niet (meer)"
fi

echo "==> Deploy-identiteit id-dvd-github-acc"
PRINCIPAL="$(az identity show -g rg-dvd-nonprod-shared -n id-dvd-github-acc --query principalId -o tsv 2>/dev/null || true)"
if [[ -n "$PRINCIPAL" ]]; then
  # Roltoewijzingen buiten rg-dvd-acc (bijv. op het gedeelde plan) eerst, anders blijven ze als "onbekend" staan.
  for ID in $(az role assignment list --assignee "$PRINCIPAL" --all --query "[].id" -o tsv); do
    az role assignment delete --ids "$ID" -o none
  done
  az identity delete -g rg-dvd-nonprod-shared -n id-dvd-github-acc
  echo "    verwijderd"
else
  echo "    bestaat niet (meer)"
fi

echo "==> Groep sg-dvd-sql-admin-acc"
GROUP_ID="$(az ad group list --filter "displayName eq 'sg-dvd-sql-admin-acc'" --query '[0].id' -o tsv)"
if [[ -n "$GROUP_ID" ]]; then
  az ad group delete --group "$GROUP_ID"
  echo "    verwijderd"
else
  echo "    bestaat niet (meer)"
fi

echo
echo "Klaar. Nog doen: infra/bootstrap/remove-acc.sh --entra (in de External ID-tenant) en GitHub-environment \"acc\" verwijderen."
