#!/usr/bin/env bash
# Eenmalige bootstrap van Productie (fase 7, runbook: docs/runbooks/livegang-productie.md).
# Uitvoeren door een beheerder met Owner op de subscription, ingelogd in de tenant van de subscription:
#   az login --tenant <tenant-id-vereniging>
#   AZURE_SUBSCRIPTION_ID=<id> infra/bootstrap/bootstrap-prod.sh [--what-if]
# Voorwaarde: bootstrap-nonprod.sh is eerder uitgevoerd (what-if-identiteit en -rol). Idempotent.
set -euo pipefail

: "${AZURE_SUBSCRIPTION_ID:?Zet AZURE_SUBSCRIPTION_ID}"
LOCATION="${DVD_LOCATION:-swedencentral}"
REPOSITORY="${DVD_GITHUB_REPOSITORY:-robinmom@37656265/vrolijkedrammers@1386066275}"
HERE="$(cd "$(dirname "$0")" && pwd)"

az account set --subscription "$AZURE_SUBSCRIPTION_ID"
TENANT_ID="$(az account show --query tenantId -o tsv)"

deploy_args=(--location "$LOCATION" --name dvd-bootstrap-prod
  --template-file "$HERE/prod.bicep"
  --parameters location="$LOCATION" githubRepository="$REPOSITORY")

if [[ "${1:-}" == "--what-if" ]]; then
  az deployment sub what-if "${deploy_args[@]}"
  exit 0
fi

echo "==> Bootstrap-deployment Productie ($LOCATION)"
OUTPUTS="$(az deployment sub create "${deploy_args[@]}" --query properties.outputs -o json)"
PLAN_ID="$(jq -r .appServicePlanId.value <<<"$OUTPUTS")"
CLIENT_ID="$(jq -r .deployClientId.value <<<"$OUTPUTS")"
PRINCIPAL_ID="$(jq -r .deployPrincipalId.value <<<"$OUTPUTS")"
ME="$(az ad signed-in-user show --query id -o tsv)"

GROUP="sg-dvd-sql-admin-prod"
echo "==> SQL-beheergroep $GROUP"
GROUP_ID="$(az ad group list --filter "displayName eq '$GROUP'" --query '[0].id' -o tsv)"
if [[ -z "$GROUP_ID" ]]; then
  GROUP_ID="$(az ad group create --display-name "$GROUP" --mail-nickname "$GROUP" \
    --description "SQL-beheerders De Vrolijke Drammers (prod)" --query id -o tsv)"
fi
for MEMBER in "$ME" "$PRINCIPAL_ID"; do
  if [[ "$(az ad group member check --group "$GROUP_ID" --member-id "$MEMBER" --query value -o tsv)" != "true" ]]; then
    az ad group member add --group "$GROUP_ID" --member-id "$MEMBER"
  fi
done

cat <<VARS

GitHub → Settings → Environments → "prod" (met Required reviewers) → Environment variables:
  AZURE_CLIENT_ID                 = $CLIENT_ID
  AZURE_TENANT_ID                 = $TENANT_ID
  AZURE_SUBSCRIPTION_ID           = $AZURE_SUBSCRIPTION_ID
  DVD_LOCATION                    = $LOCATION
  DVD_APP_SERVICE_PLAN_ID         = $PLAN_ID
  DVD_SQL_ADMIN_GROUP_OBJECT_ID   = $GROUP_ID

Daarna (runbook): DVD_API_CLIENT_ID, DVD_PORTAL_CLIENT_ID, DVD_MOBILE_CLIENT_ID (register-apps.sh prod),
DVD_GRAPH_CLIENT_ID en DVD_GRAPH_CERTIFICATE_NAME (register-provisioning-app.sh prod), DVD_BOOTSTRAP_ADMIN.
Geen van deze waarden is geheim, maar ze horen niet in de (publieke) repository.
VARS
