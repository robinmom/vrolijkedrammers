#!/usr/bin/env bash
# Maakt een inlog met e-mail + wachtwoord, bijvoorbeeld voor het testaccount van de App Store-review (Apple kan geen
# code per mail ontvangen). Uitzondering op ADR-014 (inloggen met e-mail + code): alleen voor losse accounts.
#   infra/entra/set-password-login.sh <e-mailadres>
#
# Het wachtwoord is willekeurig en wordt nergens getoond of opgeslagen. De eigenaar van de mailbox kiest het echte
# wachtwoord via "Wachtwoord vergeten" op het inlogscherm. Het account bij de vereniging (lid + account in het portal)
# maakt het bestuur zoals altijd; de API koppelt de inlog daaraan bij de eerste aanmelding (AccountLinker).
# Graph kan geen wachtwoord toevoegen aan een bestaande inlog met e-mail + code: die moet eerst in het
# Entra-beheercentrum verwijderd worden.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=infra/entra/lib.sh
source "$HERE/lib.sh"

EMAIL="${1:?Gebruik: set-password-login.sh <e-mailadres>}"
DOMAIN="vrolijkedrammersapp.onmicrosoft.com"
use_ciam_tenant

find_login() { # <issuer>
  graph --method get --url "$GRAPH/users" \
    --url-parameters "\$filter=identities/any(i:i/issuerAssignedId eq '$EMAIL' and i/issuer eq '$1')" "\$select=id" \
    --query "value[0].id" -o tsv
}

if [[ -n "$(find_login "$DOMAIN")" ]]; then
  echo "$EMAIL heeft al een inlog met wachtwoord; kies het wachtwoord via 'Wachtwoord vergeten'."
  exit 0
fi

if [[ -n "$(find_login mail)" ]]; then
  echo "$EMAIL heeft al een inlog met e-mail + code. Verwijder die eerst in het Entra-beheercentrum." >&2
  exit 1
fi

BODY="$(EMAIL="$EMAIL" DOMAIN="$DOMAIN" PASSWORD="$(openssl rand -base64 30)Aa1!" python3 - <<'EOF'
import json, os
print(json.dumps({
    "accountEnabled": True,
    "displayName": os.environ["EMAIL"],
    "mail": os.environ["EMAIL"],
    "identities": [{"signInType": "emailAddress", "issuer": os.environ["DOMAIN"], "issuerAssignedId": os.environ["EMAIL"]}],
    "passwordProfile": {"password": os.environ["PASSWORD"], "forceChangePasswordNextSignIn": False},
    "passwordPolicies": "DisablePasswordExpiration",
}))
EOF
)"
USER_ID="$(graph --method post --url "$GRAPH/users" --body "$BODY" --query id -o tsv)"
echo "Inlog met wachtwoord aangemaakt voor $EMAIL ($USER_ID)."
echo "Kies het wachtwoord via 'Wachtwoord vergeten' op het inlogscherm."
