#!/usr/bin/env bash
# Zet één geheim in de Key Vault van een omgeving. De waarde wordt gevraagd zonder echo en komt niet in de
# shellgeschiedenis, de proceslijst of deze repository.
#   az login --tenant <tenant-id-vereniging> && infra/prod/set-secret.sh prod mollie-api-key
# Bekende namen: mollie-api-key (live_… in Prod), eboekhouden-api-token, facebook-page-token,
# expo-push-access-token, turnstile-secret-key.
set -euo pipefail

ENV="${1:?Gebruik: set-secret.sh dev|acc|prod <naam>}"
NAME="${2:?Gebruik: set-secret.sh dev|acc|prod <naam>}"
[[ "$ENV" =~ ^(dev|acc|prod)$ ]] || { echo "Onbekende omgeving: $ENV" >&2; exit 1; }
[[ "$NAME" =~ ^[a-z0-9-]+$ ]] || { echo "Ongeldige naam: $NAME" >&2; exit 1; }
VAULT="kv-dvd-$ENV"

read -r -s -p "Waarde voor $NAME: " SECRET
echo
[[ -n "$SECRET" ]] || { echo "Lege waarde" >&2; exit 1; }
printf '%s' "$SECRET" | az keyvault secret set --vault-name "$VAULT" --name "$NAME" --file /dev/stdin \
  --content-type "text/plain" -o none
unset SECRET
echo "Opgeslagen in $VAULT als $NAME. De app leest het binnen enkele minuten (of na een herstart)."
