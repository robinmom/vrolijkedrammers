# Gedeelde helpers voor Microsoft Graph in de External ID-tenant (B-02).
# De constanten worden gebruikt door de scripts die dit bestand sourcen.
# shellcheck shell=bash disable=SC2034
# Prod (en de leden) staan in de oorspronkelijke tenant. Dev krijgt een eigen tenant (beveiligingsonderzoek H1,
# 2026-10-17), zodat een gelekt Dev-certificaat nooit bij echte accounts kan. Zolang die er nog niet is
# (DVD_DEV_CIAM_TENANT_ID leeg), wijst dev nog naar de gedeelde tenant. use_ciam_tenant zet de variabelen hieronder.
PROD_CIAM_TENANT_ID="260db5a1-e5b6-4388-9f6c-d9b02cb5578b"
PROD_CIAM_NAME="vrolijkedrammersapp"
DEV_CIAM_TENANT_ID="${DVD_DEV_CIAM_TENANT_ID:-}"
DEV_CIAM_NAME="${DVD_DEV_CIAM_NAME:-vrolijkedrammersdev}"
CIAM_TENANT_ID="$PROD_CIAM_TENANT_ID"
CIAM_ISSUER_DOMAIN="$PROD_CIAM_NAME.onmicrosoft.com"
GRAPH="https://graph.microsoft.com/v1.0"
GRAPH_APP_ID="00000003-0000-0000-c000-000000000000"
DEFAULT_ACCESS_ROLE="00000000-0000-0000-0000-000000000000"

graph() { az rest --resource https://graph.microsoft.com --headers "Content-Type=application/json" "$@"; }

# Eerste object in een collectie waarvan displayName exact overeenkomt (client-side gefilterd, geen URL-encoding nodig).
find_by_name() { # <collectie-url> <displayName> <veld>
  local select="id,displayName"
  [[ "$3" == id ]] || select="$select,$3"
  graph --method get --url "$1" --url-parameters "\$select=$select" "\$top=999" \
    --query "value[?displayName=='$2'].$3 | [0]" -o tsv
}

use_ciam_tenant() { # [dev|prod] (standaard prod)
  if [[ "${1:-prod}" == dev && -n "$DEV_CIAM_TENANT_ID" ]]; then
    CIAM_TENANT_ID="$DEV_CIAM_TENANT_ID"
    CIAM_ISSUER_DOMAIN="$DEV_CIAM_NAME.onmicrosoft.com"
  elif [[ "${1:-prod}" == dev ]]; then
    echo "Let op: DVD_DEV_CIAM_TENANT_ID is leeg, dev gebruikt nog de gedeelde tenant." >&2
  fi
  echo "Tenant: $CIAM_ISSUER_DOMAIN ($CIAM_TENANT_ID)" >&2
  if ! az account set --subscription "$CIAM_TENANT_ID" 2>/dev/null; then
    echo "Log eerst in op de External ID-tenant: az login --tenant $CIAM_TENANT_ID --allow-no-subscriptions" >&2
    exit 1
  fi
}

# Volledige naam van de directory-extensie environmentAccess; stopt met instructie als die nog niet bestaat.
environment_access_extension() {
  local ext_app_id name
  ext_app_id="$(graph --method get --url "$GRAPH/applications" --url-parameters "\$select=id,displayName" "\$top=999" \
    --query "value[?starts_with(displayName, 'b2c-extensions-app')].id | [0]" -o tsv)"
  if [[ -n "$ext_app_id" ]]; then
    name="$(graph --method get --url "$GRAPH/applications/$ext_app_id/extensionProperties" \
      --query "value[?ends_with(name, '_environmentAccess')].name | [0]" -o tsv)"
  fi
  if [[ -z "${name:-}" ]]; then
    echo "Custom attribuut ontbreekt. Entra-beheercentrum → External Identities → Custom user attributes → Add:" >&2
    echo "  naam 'environmentAccess', type String. Voer daarna dit script opnieuw uit." >&2
    exit 1
  fi
  echo "$name"
}
