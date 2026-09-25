#!/usr/bin/env bash
# Certificaat voor de provisioning-app (Graph in de External ID-tenant), fase 3.
# Federatie met de managed identity werkt niet over tenants heen naar een external tenant (AADSTS700236); daarom een
# certificaat dat in Key Vault blijft: Key Vault maakt en vernieuwt het, de API leest het met zijn managed identity.
#   Vereist: rol "Key Vault Certificates Officer" op kv-dvd-<env> (tijdelijk) en az login in beide tenants.
#   AZURE_SUBSCRIPTION_ID=<id> infra/entra/provisioning-certificate.sh dev [--renew-only]
# Zonder --renew-only: maakt het certificaat aan als het nog niet bestaat. Altijd: zet de huidige publieke sleutel(s)
# op de app (ook na een automatische vernieuwing door Key Vault opnieuw draaien).
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=infra/entra/lib.sh
source "$HERE/lib.sh"

ENV="${1:?Gebruik: provisioning-certificate.sh dev|acc|prod [--renew-only]}"
MODE="${2:-}"
: "${AZURE_SUBSCRIPTION_ID:?Zet AZURE_SUBSCRIPTION_ID}"
VAULT="kv-dvd-$ENV"
CERT="graph-provisioning"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

az account set --subscription "$AZURE_SUBSCRIPTION_ID"
if [[ "$MODE" != "--renew-only" ]] && ! az keyvault certificate show --vault-name "$VAULT" -n "$CERT" -o none 2>/dev/null; then
  echo "==> Certificaat aanmaken in $VAULT"
  az keyvault certificate get-default-policy \
    | jq --arg s "CN=dvd-graph-provisioning-$ENV" \
      '.x509CertificateProperties.subject = $s | .x509CertificateProperties.validityInMonths = 12
       | .lifetimeActions = [{"action": {"actionType": "AutoRenew"}, "trigger": {"daysBeforeExpiry": 60}}]' > "$WORK/policy.json"
  az keyvault certificate create --vault-name "$VAULT" -n "$CERT" -p "@$WORK/policy.json" -o none
fi

echo "==> Publieke sleutels ophalen (huidige versies)"
KEYS="[]"
for version in $(az keyvault certificate list-versions --vault-name "$VAULT" -n "$CERT" --query "[?attributes.enabled && attributes.expires > '$(date -u +%Y-%m-%dT%H:%M:%SZ)'].id" -o tsv); do
  az keyvault certificate download --id "$version" -f "$WORK/cert.der" -e DER
  KEY="$(base64 < "$WORK/cert.der" | tr -d '\n')"
  END="$(az keyvault certificate show --id "$version" --query attributes.expires -o tsv)"
  KEYS="$(jq -c --arg k "$KEY" --arg e "$END" --arg n "$CERT ($(basename "$version"))" \
    '. + [{"type": "AsymmetricX509Cert", "usage": "Verify", "key": $k, "endDateTime": $e, "displayName": $n}]' <<<"$KEYS")"
  rm -f "$WORK/cert.der"
done

echo "==> Sleutels op de app zetten"
use_ciam_tenant
APP_ID="$(find_by_name "$GRAPH/applications" "DVD Provisioning ($ENV)" appId)"
[[ -n "$APP_ID" ]] || { echo "App 'DVD Provisioning ($ENV)' niet gevonden; draai eerst register-provisioning-app.sh" >&2; exit 1; }
graph --method patch --url "$GRAPH/applications(appId='$APP_ID')" --body "{\"keyCredentials\": $KEYS}" -o none
az account set --subscription "$AZURE_SUBSCRIPTION_ID"

cat <<VARS

Klaar ($(jq length <<<"$KEYS") geldige sleutel(s)). GitHub → Environment "$ENV" → variables:
  DVD_GRAPH_CERTIFICATE_NAME = $CERT
Na een automatische vernieuwing door Key Vault: dit script opnieuw draaien met --renew-only en de API herstarten.
VARS
