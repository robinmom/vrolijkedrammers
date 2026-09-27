#!/usr/bin/env bash
# Geeft een bestaand account toegang tot Dev en/of Acc (B-02): lid van Testers + attribuut environmentAccess.
#   infra/entra/set-tester.sh <user-object-id of e-mailadres> dev,acc     (leeg tweede argument = toegang intrekken)
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=infra/entra/lib.sh
source "$HERE/lib.sh"

USER_ID="${1:?Gebruik: set-tester.sh <user-object-id of e-mailadres> [dev,acc]}"
ACCESS="${2:-}"
[[ -z "$ACCESS" || "$ACCESS" =~ ^(dev|acc)(,(dev|acc))?$ ]] || { echo "Toegestaan: dev, acc of dev,acc" >&2; exit 1; }
use_ciam_tenant

# Een UPN (…@vrolijkedrammersapp.onmicrosoft.com) of e-mailadres omzetten naar de object-id. Een eigen inlog met
# e-mail + code staat als identity "federated" met issuer "mail"; een account met wachtwoord onder het eigen domein.
if [[ "$USER_ID" == *@vrolijkedrammersapp.onmicrosoft.com ]]; then
  USER_ID="$(graph --method get --url "$GRAPH/users/$USER_ID" --url-parameters "\$select=id" --query id -o tsv)"
elif [[ "$USER_ID" == *@* ]]; then
  EMAIL="$USER_ID"
  USER_ID=""
  for ISSUER in mail vrolijkedrammersapp.onmicrosoft.com; do
    USER_ID="$(graph --method get --url "$GRAPH/users" \
      --url-parameters "\$filter=identities/any(i:i/issuerAssignedId eq '$EMAIL' and i/issuer eq '$ISSUER')" "\$select=id" \
      --query "value[0].id" -o tsv)"
    [[ -n "$USER_ID" ]] && break
  done
  [[ -n "$USER_ID" ]] || { echo "Geen inlog gevonden voor $EMAIL" >&2; exit 1; }
fi

EXTENSION_NAME="$(environment_access_extension)"
TESTERS_ID="$(find_by_name "$GRAPH/groups" Testers id)"

VALUE=$([[ -n "$ACCESS" ]] && echo "\"$ACCESS\"" || echo null)
graph --method patch --url "$GRAPH/users/$USER_ID" --body "{\"$EXTENSION_NAME\": $VALUE}" -o none

IS_MEMBER="$(graph --method get --url "$GRAPH/groups/$TESTERS_ID/members" --url-parameters "\$select=id" \
  --query "value[?id=='$USER_ID'].id | [0]" -o tsv)"
if [[ -n "$ACCESS" && -z "$IS_MEMBER" ]]; then
  graph --method post --url "$GRAPH/groups/$TESTERS_ID/members/\$ref" --body "{\"@odata.id\": \"$GRAPH/directoryObjects/$USER_ID\"}" -o none
elif [[ -z "$ACCESS" && -n "$IS_MEMBER" ]]; then
  graph --method delete --url "$GRAPH/groups/$TESTERS_ID/members/$USER_ID/\$ref" -o none
fi
echo "environmentAccess = ${ACCESS:-<leeg>} voor $USER_ID"
