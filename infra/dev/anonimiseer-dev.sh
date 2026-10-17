#!/usr/bin/env bash
# Dev met testgegevens (2026-10-17): vervangt de persoonsgegevens in de Dev-database door testwaarden (zie
# anonimiseer-dev.sql). Zonder --apply een proefrun: alles in een transactie die wordt teruggedraaid, met per onderdeel
# de aantallen. Alleen voor DEV; zet eerst de e-Boekhouden-koppeling van Dev uit (dev.bicepparam), anders komen de echte
# leden 's nachts terug.
#
#   az login --tenant <tenant-id-vereniging>
#   AZURE_SUBSCRIPTION_ID=<id> infra/dev/anonimiseer-dev.sh            # proefrun
#   AZURE_SUBSCRIPTION_ID=<id> infra/dev/anonimiseer-dev.sh --apply    # vastleggen
#
# Vereist: lid van sg-dvd-sql-admin-dev. Het script zet tijdelijk een firewallregel voor jouw IP en haalt die weer weg.
set -euo pipefail

: "${AZURE_SUBSCRIPTION_ID:?Zet AZURE_SUBSCRIPTION_ID}"
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
APPLY=0
if [[ "${1:-}" == "--apply" ]]; then
  APPLY=1
  read -r -p "Persoonsgegevens in DEV definitief vervangen door testgegevens? Typ 'ja': " answer
  [[ "$answer" == "ja" ]] || { echo "Afgebroken."; exit 1; }
fi

az account set --subscription "$AZURE_SUBSCRIPTION_ID"
WORK="$(mktemp -d)"
IP="$(curl -sf https://api.ipify.org)"
RULE="anonimiseer-$(date +%Y%m%d%H%M%S)"
cleanup() {
  az sql server firewall-rule delete -g rg-dvd-dev -s sql-dvd-dev -n "$RULE" -o none 2>/dev/null || true
  rm -rf "${WORK:?}"
}
trap cleanup EXIT

az sql server firewall-rule create -g rg-dvd-dev -s sql-dvd-dev -n "$RULE" --start-ip-address "$IP" --end-ip-address "$IP" -o none
sed "s/\$(APPLY)/$APPLY/" "$HERE/anonimiseer-dev.sql" >"$WORK/anonimiseer.sql"
DVD_SQL_ACCESS_TOKEN="$(az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv)" \
  dotnet run --project "$ROOT/tools/Drammers.DbSetup" -- "sql-dvd-dev.database.windows.net" sqldb-dvd migrate "$WORK/anonimiseer.sql"
