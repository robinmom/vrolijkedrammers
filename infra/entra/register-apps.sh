#!/usr/bin/env bash
# App-registraties per omgeving in de Entra External ID-tenant (B-02, docs/runbooks/entra-external-id.md §4).
#   az login --tenant 260db5a1-e5b6-4388-9f6c-d9b02cb5578b --allow-no-subscriptions
#   DVD_PORTAL_URL=https://app-dvd-api-dev.azurewebsites.net/beheer/ infra/entra/register-apps.sh dev
# Idempotent. Maakt per omgeving: API, beheerportal (SPA) en app (public client).
# Dev/Acc: "Require user assignment" + groep Testers; de API krijgt de claim environmentAccess.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=infra/entra/lib.sh
source "$HERE/lib.sh"

ENV="${1:?Gebruik: register-apps.sh dev|acc|prod}"
[[ "$ENV" =~ ^(dev|acc|prod)$ ]] || { echo "Onbekende omgeving: $ENV" >&2; exit 1; }
PORTAL_URL="${DVD_PORTAL_URL:-}"
RESTRICTED=$([[ "$ENV" == prod ]] && echo false || echo true)
use_ciam_tenant

# ---------------------------------------------------------------------------------------------
# Tenant-breed (eenmalig): custom attribuut, claims-mapping, groep Testers, user flow zonder sign-up.
# ---------------------------------------------------------------------------------------------
echo "==> Custom attribuut environmentAccess"
# Handmatige stap (de Azure CLI heeft geen IdentityUserFlow-rechten): Entra-beheercentrum → External Identities →
# Custom user attributes → Add: naam "environmentAccess", type String. Dit maakt ook b2c-extensions-app aan.
EXTENSION_NAME="$(environment_access_extension)"
echo "==> Claims-mapping-policy (extensie → claim 'environmentAccess')"
POLICY_NAME="DVD environmentAccess-claim"
POLICY_ID="$(find_by_name "$GRAPH/policies/claimsMappingPolicies" "$POLICY_NAME" id)"
if [[ -z "$POLICY_ID" ]]; then
  DEFINITION="$(jq -cn --arg ext "$EXTENSION_NAME" '{ClaimsMappingPolicy: {Version: 1, IncludeBasicClaimSet: "true",
    ClaimsSchema: [{Source: "user", ExtensionID: $ext, JwtClaimType: "environmentAccess"}]}} | tojson')"
  POLICY_ID="$(graph --method post --url "$GRAPH/policies/claimsMappingPolicies" \
    --body "{\"displayName\": \"$POLICY_NAME\", \"isOrganizationDefault\": false, \"definition\": [$DEFINITION]}" --query id -o tsv)"
fi

echo "==> Groep Testers"
TESTERS_ID="$(find_by_name "$GRAPH/groups" Testers id)"
if [[ -z "$TESTERS_ID" ]]; then
  TESTERS_ID="$(graph --method post --url "$GRAPH/groups" --body '{
    "displayName": "Testers", "mailNickname": "testers", "mailEnabled": false, "securityEnabled": true,
    "description": "Mogen inloggen op de Dev/Acc-apps (samen met het attribuut environmentAccess)"}' --query id -o tsv)"
fi

echo "==> User flow 'DVD aanmelden' (e-mail + eenmalige code, zelfregistratie uit)"
# Dit endpoint ondersteunt geen $select/$top.
FLOW_ID="$(graph --method get --url "$GRAPH/identity/authenticationEventsFlows" \
  --query "value[?displayName=='DVD aanmelden'].id | [0]" -o tsv)"
if [[ -z "$FLOW_ID" ]]; then
  FLOW_ID="$(graph --method post --url "$GRAPH/identity/authenticationEventsFlows" --body '{
    "@odata.type": "#microsoft.graph.externalUsersSelfServiceSignUpEventsFlow",
    "displayName": "DVD aanmelden",
    "description": "Alleen aanmelden; accounts worden via de backend aangemaakt (ADR-014)",
    "onInteractiveAuthFlowStart": {
      "@odata.type": "#microsoft.graph.onInteractiveAuthFlowStartExternalUsersSelfServiceSignUp",
      "isSignUpAllowed": false },
    "onAuthenticationMethodLoadStart": {
      "@odata.type": "#microsoft.graph.onAuthenticationMethodLoadStartExternalUsersSelfServiceSignUp",
      "identityProviders": [ { "id": "EmailOtpSignup-OAUTH" } ] } }' --query id -o tsv)"
fi

# ---------------------------------------------------------------------------------------------
# Per omgeving
# ---------------------------------------------------------------------------------------------
ensure_app() { # <displayName> <json-body> → appId
  local app_id
  app_id="$(find_by_name "$GRAPH/applications" "$1" appId)"
  if [[ -z "$app_id" ]]; then
    app_id="$(graph --method post --url "$GRAPH/applications" --body "$2" --query appId -o tsv)"
  else
    graph --method patch --url "$GRAPH/applications(appId='$app_id')" --body "$2" -o none
  fi
  echo "$app_id"
}

ensure_service_principal() { # <appId> → object-id
  local sp_id
  sp_id="$(graph --method get --url "$GRAPH/servicePrincipals(appId='$1')" --query id -o tsv 2>/dev/null || true)"
  if [[ -z "$sp_id" ]]; then
    sp_id="$(graph --method post --url "$GRAPH/servicePrincipals" --body "{\"appId\": \"$1\"}" --query id -o tsv)"
  fi
  graph --method patch --url "$GRAPH/servicePrincipals/$sp_id" --body "{\"appRoleAssignmentRequired\": $RESTRICTED}" -o none
  echo "$sp_id"
}

ensure_testers_assignment() { # <sp-id>
  [[ "$RESTRICTED" == true ]] || return 0
  local existing
  existing="$(graph --method get --url "$GRAPH/servicePrincipals/$1/appRoleAssignedTo" \
    --query "value[?principalId=='$TESTERS_ID'].id | [0]" -o tsv)"
  if [[ -z "$existing" ]]; then
    graph --method post --url "$GRAPH/servicePrincipals/$1/appRoleAssignedTo" --body "{
      \"principalId\": \"$TESTERS_ID\", \"resourceId\": \"$1\", \"appRoleId\": \"$DEFAULT_ACCESS_ROLE\"}" -o none
  fi
}

ensure_consent() { # <client-sp-id> <resource-sp-id> <scopes>
  local existing
  existing="$(graph --method get --url "$GRAPH/oauth2PermissionGrants" \
    --url-parameters "\$filter=clientId eq '$1' and resourceId eq '$2' and consentType eq 'AllPrincipals'" --query "value[0].id" -o tsv)"
  if [[ -z "$existing" ]]; then
    graph --method post --url "$GRAPH/oauth2PermissionGrants" --body "{
      \"clientId\": \"$1\", \"resourceId\": \"$2\", \"consentType\": \"AllPrincipals\", \"scope\": \"$3\"}" -o none
  else
    graph --method patch --url "$GRAPH/oauth2PermissionGrants/$existing" --body "{\"scope\": \"$3\"}" -o none
  fi
}

ensure_in_user_flow() { # <appId>
  local linked
  linked="$(graph --method get --url "$GRAPH/identity/authenticationEventsFlows/$FLOW_ID/conditions/applications/includeApplications" \
    --query "value[?appId=='$1'].appId | [0]" -o tsv)"
  if [[ -z "$linked" ]]; then
    graph --method post --url "$GRAPH/identity/authenticationEventsFlows/$FLOW_ID/conditions/applications/includeApplications" \
      --body "{\"@odata.type\": \"#microsoft.graph.authenticationConditionApplication\", \"appId\": \"$1\"}" -o none
  fi
}

echo "==> API ($ENV)"
API_NAME="DVD API ($ENV)"
API_APP_ID="$(find_by_name "$GRAPH/applications" "$API_NAME" appId)"
SCOPE_ID=""
if [[ -n "$API_APP_ID" ]]; then
  SCOPE_ID="$(graph --method get --url "$GRAPH/applications(appId='$API_APP_ID')" \
    --query "api.oauth2PermissionScopes[?value=='access_as_user'].id | [0]" -o tsv)"
fi
SCOPE_ID="${SCOPE_ID:-$(uuidgen | tr '[:upper:]' '[:lower:]')}"
API_APP_ID="$(ensure_app "$API_NAME" "{
  \"displayName\": \"$API_NAME\",
  \"signInAudience\": \"AzureADMyOrg\",
  \"api\": {
    \"requestedAccessTokenVersion\": 2,
    \"acceptMappedClaims\": true,
    \"oauth2PermissionScopes\": [{
      \"id\": \"$SCOPE_ID\", \"value\": \"access_as_user\", \"type\": \"User\", \"isEnabled\": true,
      \"adminConsentDisplayName\": \"Toegang tot de API van De Vrolijke Drammers\",
      \"adminConsentDescription\": \"De app en het beheerportal roepen namens de ingelogde gebruiker de API aan.\",
      \"userConsentDisplayName\": \"Toegang tot De Vrolijke Drammers\",
      \"userConsentDescription\": \"De app gebruikt jouw account om de API aan te roepen.\" }] } }")"
graph --method patch --url "$GRAPH/applications(appId='$API_APP_ID')" --body "{\"identifierUris\": [\"api://$API_APP_ID\"]}" -o none
API_SP_ID="$(ensure_service_principal "$API_APP_ID")"
if [[ -z "$(graph --method get --url "$GRAPH/servicePrincipals/$API_SP_ID/claimsMappingPolicies" --query "value[?id=='$POLICY_ID'].id | [0]" -o tsv)" ]]; then
  graph --method post --url "$GRAPH/servicePrincipals/$API_SP_ID/claimsMappingPolicies/\$ref" \
    --body "{\"@odata.id\": \"$GRAPH/policies/claimsMappingPolicies/$POLICY_ID\"}" -o none
fi
ensure_testers_assignment "$API_SP_ID"

REQUIRED_ACCESS="[
  {\"resourceAppId\": \"$API_APP_ID\", \"resourceAccess\": [{\"id\": \"$SCOPE_ID\", \"type\": \"Scope\"}]},
  {\"resourceAppId\": \"$GRAPH_APP_ID\", \"resourceAccess\": [
    {\"id\": \"37f7f235-527c-4136-accd-4a02d197296e\", \"type\": \"Scope\"},
    {\"id\": \"7427e0e9-2fba-42fe-b0c0-848c9e6a8182\", \"type\": \"Scope\"}]}]"
GRAPH_SP_ID="$(graph --method get --url "$GRAPH/servicePrincipals(appId='$GRAPH_APP_ID')" --query id -o tsv)"

echo "==> Beheerportal ($ENV)"
PORTAL_NAME="DVD Beheerportal ($ENV)"
PORTAL_REDIRECTS="$(jq -cn --arg url "$PORTAL_URL" --arg env "$ENV" \
  '[($url | select(length > 0)), (if $env == "dev" then "http://localhost:5173" else empty end)]')"
PORTAL_APP_ID="$(ensure_app "$PORTAL_NAME" "{
  \"displayName\": \"$PORTAL_NAME\", \"signInAudience\": \"AzureADMyOrg\",
  \"spa\": {\"redirectUris\": $PORTAL_REDIRECTS},
  \"requiredResourceAccess\": $REQUIRED_ACCESS }")"
PORTAL_SP_ID="$(ensure_service_principal "$PORTAL_APP_ID")"
ensure_testers_assignment "$PORTAL_SP_ID"
ensure_consent "$PORTAL_SP_ID" "$API_SP_ID" "access_as_user"
ensure_consent "$PORTAL_SP_ID" "$GRAPH_SP_ID" "openid offline_access"
ensure_in_user_flow "$PORTAL_APP_ID"

echo "==> Mobiele app ($ENV)"
MOBILE_NAME="DVD App ($ENV)"
MOBILE_APP_ID="$(ensure_app "$MOBILE_NAME" "{
  \"displayName\": \"$MOBILE_NAME\", \"signInAudience\": \"AzureADMyOrg\",
  \"isFallbackPublicClient\": true,
  \"publicClient\": {\"redirectUris\": [\"drammers://auth\", \"msauth.nl.vrolijkedrammers.app://auth\"]},
  \"requiredResourceAccess\": $REQUIRED_ACCESS }")"
MOBILE_SP_ID="$(ensure_service_principal "$MOBILE_APP_ID")"
ensure_testers_assignment "$MOBILE_SP_ID"
ensure_consent "$MOBILE_SP_ID" "$API_SP_ID" "access_as_user"
ensure_consent "$MOBILE_SP_ID" "$GRAPH_SP_ID" "openid offline_access"
ensure_in_user_flow "$MOBILE_APP_ID"

cat <<VARS

Klaar. GitHub → Settings → Environments → "$ENV" → Environment variables:
  DVD_API_CLIENT_ID             = $API_APP_ID
  DVD_PORTAL_CLIENT_ID          = $PORTAL_APP_ID
  DVD_MOBILE_CLIENT_ID          = $MOBILE_APP_ID
  DVD_ENVIRONMENT_ACCESS_CLAIM  = environmentAccess
Scope voor clients: api://$API_APP_ID/access_as_user
VARS
