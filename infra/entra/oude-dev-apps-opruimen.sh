#!/usr/bin/env bash
# Laatste stap van de eigen Dev-tenant (H1, docs/runbooks/dev-tenant.md stap 6): de Dev-app-registraties uit de
# gedeelde (prod-)tenant verwijderen. Pas daarna heeft niets van Dev nog rechten op de echte accounts.
# Alleen draaien als Dev al op de eigen tenant draait en het inloggen daar getest is.
#   az login --tenant 260db5a1-e5b6-4388-9f6c-d9b02cb5578b --allow-no-subscriptions
#   DVD_DEV_CIAM_TENANT_ID=<dev-tenant-id> infra/entra/oude-dev-apps-opruimen.sh
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=infra/entra/lib.sh
source "$HERE/lib.sh"

[[ -n "$DEV_CIAM_TENANT_ID" && "$DEV_CIAM_TENANT_ID" != "$PROD_CIAM_TENANT_ID" ]] || {
  echo "Zet DVD_DEV_CIAM_TENANT_ID op de nieuwe Dev-tenant; zonder eigen Dev-tenant ruimt dit script niets op." >&2
  exit 1
}
use_ciam_tenant prod

NAMES=("DVD API (dev)" "DVD Beheerportal (dev)" "DVD App (dev)" "DVD Provisioning (dev)")
FOUND=()
for NAME in "${NAMES[@]}"; do
  ID="$(find_by_name "$GRAPH/applications" "$NAME" id)"
  [[ -n "$ID" ]] && FOUND+=("$ID|$NAME")
done
if [[ ${#FOUND[@]} -eq 0 ]]; then
  echo "Geen Dev-app-registraties meer in de gedeelde tenant."
  exit 0
fi

printf 'Te verwijderen uit de gedeelde tenant:\n'
printf '  %s\n' "${FOUND[@]#*|}"
read -r -p "Typ 'ja' om te verwijderen: " ANSWER
[[ "$ANSWER" == ja ]] || { echo "Afgebroken."; exit 1; }
for ENTRY in "${FOUND[@]}"; do
  graph --method delete --url "$GRAPH/applications/${ENTRY%%|*}"
  echo "Verwijderd: ${ENTRY#*|}"
done

cat <<NEXT

Klaar. Verwijderde app-registraties staan 30 dagen in de prullenbak van Entra (Deleted applications) en kunnen
daar worden teruggezet. De groep Testers en het attribuut environmentAccess in de gedeelde tenant worden niet
meer gebruikt; ze kunnen blijven staan.
NEXT
