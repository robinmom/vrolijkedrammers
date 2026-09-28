#!/usr/bin/env bash
# Zet het Expo-access-token (push, ADR-009) in Key Vault. Het token wordt gevraagd zonder echo en komt niet in de
# shellgeschiedenis of in deze repository.
#   Maak het token op https://expo.dev/settings/access-tokens en zet daar ook "Enhanced Security for Push
#   Notifications" aan.
#   az login (werkabonnement, niet Giftnation) && infra/push/set-expo-token.sh dev
# Daarna: GitHub-environmentvariabele DVD_PUSH_PROVIDER = Expo en opnieuw deployen.
set -euo pipefail

ENV="${1:?Gebruik: set-expo-token.sh dev|acc|prod}"
[[ "$ENV" =~ ^(dev|acc|prod)$ ]] || { echo "Onbekende omgeving: $ENV" >&2; exit 1; }
VAULT="kv-dvd-$ENV"

read -r -s -p "Expo-access-token: " TOKEN
echo
[[ -n "$TOKEN" ]] || { echo "Leeg token" >&2; exit 1; }

# Via stdin (bestand /dev/stdin), zodat het token niet in de proceslijst staat.
printf '%s' "$TOKEN" | az keyvault secret set --vault-name "$VAULT" --name expo-push-access-token --file /dev/stdin \
  --content-type "text/plain" -o none
unset TOKEN
echo "Opgeslagen in $VAULT als expo-push-access-token. Zet nu DVD_PUSH_PROVIDER = Expo voor de omgeving '$ENV' en deploy."
