#!/usr/bin/env bash
# Inloggen met e-mail + wachtwoord voor één omgeving (besluit 2026-10-12): elk lid krijgt direct een account (de API
# maakt het aan in Entra), zelf een inlog maken kan niet meer, en de eerste keer kiest het lid "Wachtwoord vergeten?"
# (code per e-mail) om zelf een wachtwoord te kiezen. Runbook docs/runbooks/accounts-wachtwoord.md.
#
# Het script:
#   1. maakt de user flow "DVD inloggen met wachtwoord" (e-mail + wachtwoord, zelf aanmelden UIT);
#   2. verplaatst de beheerportal- en app-registratie van deze omgeving naar die flow (een app hoort bij één flow);
#   3. geeft de provisioning-app de Graph-rechten User-PasswordProfile.ReadWrite.All en User.ManageIdentities.All (wachtwoordinlog toevoegen aan
#      bestaande inlogs met e-mail + code, zodat ook die leden met "Wachtwoord vergeten?" verder kunnen).
# Daarna in GitHub (environment): DVD_PASSWORD_SIGN_IN=true en Deploy.
#   az login --tenant 260db5a1-e5b6-4388-9f6c-d9b02cb5578b --allow-no-subscriptions
#   infra/entra/wachtwoord-inloggen.sh dev|prod
# Idempotent. Terugdraaien: --terug (apps terug naar "DVD aanmelden"; zet daarna DVD_PASSWORD_SIGN_IN weer uit).
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=infra/entra/lib.sh
source "$HERE/lib.sh"

ENV="${1:?Gebruik: wachtwoord-inloggen.sh dev|prod [--terug]}"
[[ "$ENV" =~ ^(dev|prod)$ ]] || { echo "Onbekende omgeving: $ENV" >&2; exit 1; }
BACK="${2:-}"
use_ciam_tenant

flow_id() { # <displayName>
  graph --method get --url "$GRAPH/identity/authenticationEventsFlows" --query "value[?displayName=='$1'].id | [0]" -o tsv
}

OTP_FLOW="$(flow_id 'DVD aanmelden')"
PW_FLOW="$(flow_id 'DVD inloggen met wachtwoord')"
if [[ -z "$PW_FLOW" ]]; then
  echo "==> User flow 'DVD inloggen met wachtwoord' (e-mail + wachtwoord, zelf aanmelden uit)"
  PW_FLOW="$(graph --method post --url "$GRAPH/identity/authenticationEventsFlows" --body '{
    "@odata.type": "#microsoft.graph.externalUsersSelfServiceSignUpEventsFlow",
    "displayName": "DVD inloggen met wachtwoord",
    "description": "E-mail + wachtwoord; accounts maakt de API, zelf aanmelden kan niet (2026-10-12)",
    "onInteractiveAuthFlowStart": {
      "@odata.type": "#microsoft.graph.onInteractiveAuthFlowStartExternalUsersSelfServiceSignUp",
      "isSignUpAllowed": false },
    "onAuthenticationMethodLoadStart": {
      "@odata.type": "#microsoft.graph.onAuthenticationMethodLoadStartExternalUsersSelfServiceSignUp",
      "identityProviders": [ { "id": "EmailPassword-OAUTH" } ] } }' --query id -o tsv)"
fi

FROM="$OTP_FLOW"; TO="$PW_FLOW"
[[ "$BACK" == "--terug" ]] && { FROM="$PW_FLOW"; TO="$OTP_FLOW"; }

move_app() { # <displayName>
  local app_id
  app_id="$(find_by_name "$GRAPH/applications" "$1" appId)"
  [[ -n "$app_id" ]] || { echo "App '$1' niet gevonden" >&2; exit 1; }
  echo "==> $1 → $([[ "$TO" == "$PW_FLOW" ]] && echo 'wachtwoord' || echo 'e-mail + code')"
  graph --method delete --url "$GRAPH/identity/authenticationEventsFlows/$FROM/conditions/applications/includeApplications/$app_id" \
    -o none 2>/dev/null || true
  local linked
  linked="$(graph --method get --url "$GRAPH/identity/authenticationEventsFlows/$TO/conditions/applications/includeApplications" \
    --query "value[?appId=='$app_id'].appId | [0]" -o tsv)"
  if [[ -z "$linked" ]]; then
    graph --method post --url "$GRAPH/identity/authenticationEventsFlows/$TO/conditions/applications/includeApplications" \
      --body "{\"@odata.type\": \"#microsoft.graph.authenticationConditionApplication\", \"appId\": \"$app_id\"}" -o none
  fi
}
move_app "DVD Beheerportal ($ENV)"
move_app "DVD App ($ENV)"

if [[ "$BACK" != "--terug" ]]; then
  # User-PasswordProfile.ReadWrite.All: een (willekeurig) wachtwoord zetten. User.ManageIdentities.All: de inlog met
  # e-mail + wachtwoord toevoegen aan een bestaand account met e-mail + code (identities wijzigen; zonder dit recht 403).
  GRAPH_SP_ID="$(graph --method get --url "$GRAPH/servicePrincipals(appId='$GRAPH_APP_ID')" --query id -o tsv)"
  PROV_APP_ID="$(find_by_name "$GRAPH/applications" "DVD Provisioning ($ENV)" appId)"
  PROV_SP_ID="$(graph --method get --url "$GRAPH/servicePrincipals(appId='$PROV_APP_ID')" --query id -o tsv)"
  for PERMISSION in User-PasswordProfile.ReadWrite.All User.ManageIdentities.All; do
    echo "==> Provisioning-app: $PERMISSION"
    ROLE="$(graph --method get --url "$GRAPH/servicePrincipals/$GRAPH_SP_ID" --url-parameters "\$select=appRoles" \
      --query "appRoles[?value=='$PERMISSION'].id | [0]" -o tsv)"
    GRANTED="$(graph --method get --url "$GRAPH/servicePrincipals/$PROV_SP_ID/appRoleAssignments" --query "value[?appRoleId=='$ROLE'].id | [0]" -o tsv)"
    if [[ -z "$GRANTED" ]]; then
      graph --method post --url "$GRAPH/servicePrincipals/$GRAPH_SP_ID/appRoleAssignedTo" --body "{
        \"principalId\": \"$PROV_SP_ID\", \"resourceId\": \"$GRAPH_SP_ID\", \"appRoleId\": \"$ROLE\"}" -o none
    fi
  done
fi

cat <<NEXT

Klaar. GitHub → Settings → Environments → "$ENV" → variabele DVD_PASSWORD_SIGN_IN = $([[ "$BACK" == "--terug" ]] && echo false || echo true), daarna Deploy.
NEXT
