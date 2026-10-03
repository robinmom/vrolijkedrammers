#!/usr/bin/env bash
# Zet de geheime sleutel van Cloudflare Turnstile (contactformulier, fase 21i) in Key Vault. De sleutel wordt gevraagd
# zonder echo en komt niet in de shellgeschiedenis of in deze repository.
#   Cloudflare-dashboard → Turnstile → widget → Secret Key kopiëren.
#   az login (werkabonnement, niet Giftnation) && infra/turnstile/set-turnstile-secret.sh dev
# Daarna: GitHub-variabele DVD_TURNSTILE_SITE_KEY = de (openbare) Site Key en opnieuw deployen.
set -euo pipefail

ENV="${1:?Gebruik: set-turnstile-secret.sh dev|acc|prod}"
[[ "$ENV" =~ ^(dev|acc|prod)$ ]] || { echo "Onbekende omgeving: $ENV" >&2; exit 1; }
VAULT="kv-dvd-$ENV"

read -r -s -p "Turnstile Secret Key: " SECRET
echo
[[ -n "$SECRET" ]] || { echo "Lege sleutel" >&2; exit 1; }

# Via stdin (bestand /dev/stdin), zodat de sleutel niet in de proceslijst staat.
printf '%s' "$SECRET" | az keyvault secret set --vault-name "$VAULT" --name turnstile-secret-key --file /dev/stdin \
  --content-type "text/plain" -o none
unset SECRET
echo "Opgeslagen in $VAULT als turnstile-secret-key. Zet nu DVD_TURNSTILE_SITE_KEY (GitHub-variabele) en deploy."
