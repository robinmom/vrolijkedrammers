#!/usr/bin/env bash
# Provisioning-app per omgeving in de External ID-tenant (fase 3, ADR-014): Graph User.ReadWrite.All (application).
# Aanmelden met een certificaat uit Key Vault (provisioning-certificate.sh). Federatie met de managed identity van de
# API werkt niet naar een external tenant (AADSTS700236).
#   az login --tenant 260db5a1-e5b6-4388-9f6c-d9b02cb5578b --allow-no-subscriptions
#   infra/entra/register-provisioning-app.sh dev
# Idempotent.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=infra/entra/lib.sh
source "$HERE/lib.sh"

ENV="${1:?Gebruik: register-provisioning-app.sh dev|acc|prod}"
[[ "$ENV" =~ ^(dev|acc|prod)$ ]] || { echo "Onbekende omgeving: $ENV" >&2; exit 1; }
use_ciam_tenant

USER_READWRITE_ALL="741f803b-c850-494e-b5df-cde7c675a1ca" # Microsoft Graph application permission

echo "==> App-registratie"
NAME="DVD Provisioning ($ENV)"
BODY="{
  \"displayName\": \"$NAME\", \"signInAudience\": \"AzureADMyOrg\",
  \"notes\": \"Maakt en blokkeert accounts namens de API (ADR-014). Aanmelden met het certificaat graph-provisioning uit kv-dvd-$ENV.\",
  \"requiredResourceAccess\": [{\"resourceAppId\": \"$GRAPH_APP_ID\", \"resourceAccess\": [{\"id\": \"$USER_READWRITE_ALL\", \"type\": \"Role\"}]}] }"
APP_ID="$(find_by_name "$GRAPH/applications" "$NAME" appId)"
if [[ -z "$APP_ID" ]]; then
  APP_ID="$(graph --method post --url "$GRAPH/applications" --body "$BODY" --query appId -o tsv)"
else
  graph --method patch --url "$GRAPH/applications(appId='$APP_ID')" --body "$BODY" -o none
fi

SP_ID="$(graph --method get --url "$GRAPH/servicePrincipals(appId='$APP_ID')" --query id -o tsv 2>/dev/null || true)"
if [[ -z "$SP_ID" ]]; then
  SP_ID="$(graph --method post --url "$GRAPH/servicePrincipals" --body "{\"appId\": \"$APP_ID\"}" --query id -o tsv)"
fi

echo "==> Beheerderstoestemming User.ReadWrite.All"
GRAPH_SP_ID="$(graph --method get --url "$GRAPH/servicePrincipals(appId='$GRAPH_APP_ID')" --query id -o tsv)"
GRANTED="$(graph --method get --url "$GRAPH/servicePrincipals/$SP_ID/appRoleAssignments" \
  --query "value[?appRoleId=='$USER_READWRITE_ALL'].id | [0]" -o tsv)"
if [[ -z "$GRANTED" ]]; then
  graph --method post --url "$GRAPH/servicePrincipals/$GRAPH_SP_ID/appRoleAssignedTo" --body "{
    \"principalId\": \"$SP_ID\", \"resourceId\": \"$GRAPH_SP_ID\", \"appRoleId\": \"$USER_READWRITE_ALL\"}" -o none
fi

cat <<VARS

Klaar. GitHub → Settings → Environments → "$ENV" → Environment variables:
  DVD_GRAPH_CLIENT_ID = $APP_ID
Daarna: infra/entra/provisioning-certificate.sh $ENV (certificaat in Key Vault en publieke sleutel op de app).
VARS
