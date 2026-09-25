#!/usr/bin/env bash
# Provisioning-app per omgeving in de External ID-tenant (fase 3, ADR-014): Graph User.ReadWrite.All (application) en
# een federated credential op de managed identity van de API. Geen secret of certificaat.
#   az login --tenant 260db5a1-e5b6-4388-9f6c-d9b02cb5578b --allow-no-subscriptions
#   infra/entra/register-provisioning-app.sh dev <principal-id managed identity API> <tenant-id vereniging>
# Idempotent.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=infra/entra/lib.sh
source "$HERE/lib.sh"

ENV="${1:?Gebruik: register-provisioning-app.sh dev|acc|prod <mi-principal-id> <workforce-tenant-id>}"
MI_PRINCIPAL_ID="${2:?principal-id (object-id) van de managed identity van app-dvd-api-$ENV}"
WORKFORCE_TENANT_ID="${3:?tenant-id van de tenant waarin de subscription staat}"
[[ "$ENV" =~ ^(dev|acc|prod)$ ]] || { echo "Onbekende omgeving: $ENV" >&2; exit 1; }
use_ciam_tenant

USER_READWRITE_ALL="741f803b-c850-494e-b5df-cde7c675a1ca" # Microsoft Graph application permission

echo "==> App-registratie"
NAME="DVD Provisioning ($ENV)"
BODY="{
  \"displayName\": \"$NAME\", \"signInAudience\": \"AzureADMyOrg\",
  \"notes\": \"Maakt en blokkeert accounts namens de API (ADR-014). Alleen via federatie met de managed identity van app-dvd-api-$ENV.\",
  \"requiredResourceAccess\": [{\"resourceAppId\": \"$GRAPH_APP_ID\", \"resourceAccess\": [{\"id\": \"$USER_READWRITE_ALL\", \"type\": \"Role\"}]}] }"
APP_ID="$(find_by_name "$GRAPH/applications" "$NAME" appId)"
if [[ -z "$APP_ID" ]]; then
  APP_ID="$(graph --method post --url "$GRAPH/applications" --body "$BODY" --query appId -o tsv)"
else
  graph --method patch --url "$GRAPH/applications(appId='$APP_ID')" --body "$BODY" -o none
fi
APP_OBJECT_ID="$(graph --method get --url "$GRAPH/applications(appId='$APP_ID')" --query id -o tsv)"

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

echo "==> Federatie met de managed identity van de API"
FIC_NAME="app-dvd-api-$ENV-managed-identity"
FIC_BODY="{\"name\": \"$FIC_NAME\", \"issuer\": \"https://login.microsoftonline.com/$WORKFORCE_TENANT_ID/v2.0\",
  \"subject\": \"$MI_PRINCIPAL_ID\", \"audiences\": [\"api://AzureADTokenExchange\"],
  \"description\": \"Managed identity van app-dvd-api-$ENV (tenant vereniging)\"}"
FIC_ID="$(graph --method get --url "$GRAPH/applications/$APP_OBJECT_ID/federatedIdentityCredentials" \
  --query "value[?name=='$FIC_NAME'].id | [0]" -o tsv)"
if [[ -z "$FIC_ID" ]]; then
  graph --method post --url "$GRAPH/applications/$APP_OBJECT_ID/federatedIdentityCredentials" --body "$FIC_BODY" -o none
else
  graph --method patch --url "$GRAPH/applications/$APP_OBJECT_ID/federatedIdentityCredentials/$FIC_ID" --body "$FIC_BODY" -o none
fi

cat <<VARS

Klaar. GitHub → Settings → Environments → "$ENV" → Environment variables:
  DVD_GRAPH_CLIENT_ID = $APP_ID
Let op: na het opnieuw opbouwen van de API-app (nieuwe managed identity) dit script opnieuw draaien.
VARS
