#!/usr/bin/env bash
# Eenmalige bootstrap van Dev + Acc (runbook: docs/runbooks/omgeving-opbouwen.md).
# Uitvoeren door een beheerder met Owner op de subscription, ingelogd in de tenant van de subscription:
#   az login --tenant <tenant-id-vereniging>
#   AZURE_SUBSCRIPTION_ID=<id> DVD_LOCATION=swedencentral infra/bootstrap/bootstrap-nonprod.sh [--what-if]
# Idempotent: opnieuw uitvoeren is veilig.
set -euo pipefail

: "${AZURE_SUBSCRIPTION_ID:?Zet AZURE_SUBSCRIPTION_ID}"
LOCATION="${DVD_LOCATION:-swedencentral}"
# OIDC-subject van GitHub met eigenaar- en repo-ID (zie https://api.github.com/repos/<eigenaar>/<repo>: owner.id en id).
REPOSITORY="${DVD_GITHUB_REPOSITORY:-robinmom@37656265/vrolijkedrammers@1386066275}"
BUDGET_EMAIL="${DVD_BUDGET_EMAIL:-}"
HERE="$(cd "$(dirname "$0")" && pwd)"

az account set --subscription "$AZURE_SUBSCRIPTION_ID"
TENANT_ID="$(az account show --query tenantId -o tsv)"
EMAILS_JSON="$(jq -cn --arg e "$BUDGET_EMAIL" '[$e | select(length > 0)]')"

deploy_args=(--location "$LOCATION" --name dvd-bootstrap-nonprod
  --template-file "$HERE/nonprod.bicep"
  --parameters location="$LOCATION" githubRepository="$REPOSITORY" budgetContactEmails="$EMAILS_JSON")

if [[ "${1:-}" == "--what-if" ]]; then
  az deployment sub what-if "${deploy_args[@]}"
  exit 0
fi

echo "==> Bootstrap-deployment ($LOCATION)"
OUTPUTS="$(az deployment sub create "${deploy_args[@]}" --query properties.outputs -o json)"
PLAN_ID="$(jq -r .appServicePlanId.value <<<"$OUTPUTS")"
WHATIF_CLIENT_ID="$(jq -r .whatIfClientId.value <<<"$OUTPUTS")"
ME="$(az ad signed-in-user show --query id -o tsv)"

for ENV in dev acc; do
  CLIENT_ID="$(jq -r --arg e "$ENV" '.deployIdentities.value[] | select(.environment == $e) | .clientId' <<<"$OUTPUTS")"
  PRINCIPAL_ID="$(jq -r --arg e "$ENV" '.deployIdentities.value[] | select(.environment == $e) | .principalId' <<<"$OUTPUTS")"
  GROUP="sg-dvd-sql-admin-$ENV"

  echo "==> SQL-beheergroep $GROUP"
  GROUP_ID="$(az ad group list --filter "displayName eq '$GROUP'" --query '[0].id' -o tsv)"
  if [[ -z "$GROUP_ID" ]]; then
    GROUP_ID="$(az ad group create --display-name "$GROUP" --mail-nickname "$GROUP" \
      --description "SQL-beheerders De Vrolijke Drammers ($ENV)" --query id -o tsv)"
  fi
  for MEMBER in "$ME" "$PRINCIPAL_ID"; do
    if [[ "$(az ad group member check --group "$GROUP_ID" --member-id "$MEMBER" --query value -o tsv)" != "true" ]]; then
      az ad group member add --group "$GROUP_ID" --member-id "$MEMBER"
    fi
  done

  cat <<VARS

GitHub → Settings → Environments → "$ENV" → Environment variables:
  AZURE_CLIENT_ID                 = $CLIENT_ID
  AZURE_TENANT_ID                 = $TENANT_ID
  AZURE_SUBSCRIPTION_ID           = $AZURE_SUBSCRIPTION_ID
  DVD_LOCATION                    = $LOCATION
  DVD_APP_SERVICE_PLAN_ID         = $PLAN_ID
  DVD_SQL_ADMIN_GROUP_OBJECT_ID   = $GROUP_ID
VARS
  [[ "$ENV" == dev ]] && DEV_GROUP_ID="$GROUP_ID"
done

cat <<VARS

GitHub → Settings → Secrets and variables → Actions → Variables (repository), voor what-if in pull requests:
  AZURE_WHATIF_CLIENT_ID          = $WHATIF_CLIENT_ID
  AZURE_TENANT_ID                 = $TENANT_ID
  AZURE_SUBSCRIPTION_ID           = $AZURE_SUBSCRIPTION_ID
  DVD_LOCATION                    = $LOCATION
  DVD_APP_SERVICE_PLAN_ID         = $PLAN_ID
  DVD_DEV_SQL_ADMIN_GROUP_OBJECT_ID = $DEV_GROUP_ID

Geen van deze waarden is geheim, maar ze horen niet in de (publieke) repository.
VARS
