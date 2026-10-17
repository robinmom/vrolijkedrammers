#!/usr/bin/env bash
# Eigen External ID-tenant voor Dev (beveiligingsonderzoek H1, 2026-10-17; runbook docs/runbooks/dev-tenant.md).
# Zelfde werkwijze als de prod-tenant (docs/runbooks/entra-external-id.md §3): een Azure-resource in rg-dvd-identity,
# gefactureerd per MAU (gratis tot 50.000). Vereist een ingelogde gebruiker met Owner/Contributor op de subscription.
#   az login   (tenant van de vereniging)
#   AZURE_SUBSCRIPTION_ID=<sub> infra/entra/dev-tenant-aanmaken.sh
# Idempotent: bestaat de tenant al, dan toont het script alleen de gegevens.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=infra/entra/lib.sh
source "$HERE/lib.sh"

SUB="${AZURE_SUBSCRIPTION_ID:?Zet AZURE_SUBSCRIPTION_ID}"
NAME="$DEV_CIAM_NAME"
RG="rg-dvd-identity"
API="2023-05-17-preview"
URL="https://management.azure.com/subscriptions/$SUB/resourceGroups/$RG/providers/Microsoft.AzureActiveDirectory/ciamDirectories/$NAME?api-version=$API"
az account set --subscription "$SUB"

if ! az rest --method get --url "$URL" -o none 2>/dev/null; then
  echo "==> Naam '$NAME' controleren"
  AVAILABLE="$(az rest --method post \
    --url "https://management.azure.com/subscriptions/$SUB/providers/Microsoft.AzureActiveDirectory/checkNameAvailability?api-version=$API" \
    --body "{\"name\": \"$NAME\", \"countryCode\": \"NL\"}" --query nameAvailable -o tsv)"
  [[ "$AVAILABLE" == true ]] || { echo "Naam '$NAME' is niet beschikbaar; kies een andere met DVD_DEV_CIAM_NAME." >&2; exit 1; }

  echo "==> Tenant aanmaken (duurt enkele minuten)"
  az rest --method put --url "$URL" -o none --body "{
    \"location\": \"Europe\", \"sku\": {\"name\": \"Standard\", \"tier\": \"A0\"},
    \"tags\": {\"project\": \"vrolijkedrammers\", \"component\": \"identity\", \"environment\": \"dev\"},
    \"properties\": {\"createTenantProperties\": {\"displayName\": \"De Vrolijke Drammers App (Dev)\", \"countryCode\": \"NL\"}}}"
fi

for _ in $(seq 1 60); do
  STATE="$(az rest --method get --url "$URL" --query properties.provisioningState -o tsv)"
  [[ "$STATE" == Succeeded ]] && break
  [[ "$STATE" == Failed ]] && { echo "Aanmaken mislukt; zie het activiteitenlogboek van $RG." >&2; exit 1; }
  sleep 10
done
[[ "$STATE" == Succeeded ]] || { echo "Nog niet klaar ($STATE); draai het script later opnieuw." >&2; exit 1; }
TENANT_ID="$(az rest --method get --url "$URL" --query properties.tenantId -o tsv)"

cat <<NEXT

Klaar: $NAME.onmicrosoft.com ($TENANT_ID).
Verder met docs/runbooks/dev-tenant.md, stap 2. Exporteer voor de scripts:
  export DVD_DEV_CIAM_TENANT_ID=$TENANT_ID
  az login --tenant $TENANT_ID --allow-no-subscriptions
NEXT
