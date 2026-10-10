#!/usr/bin/env bash
# Eenmalig na de kopie Dev → Prod: testgegevens uit Productie halen (adverteerders, verkoop, ledentickets, optocht).
# Wat er precies weggaat staat in reset-testdata.sql. Zonder --apply is het een proefrun: alles gebeurt in een transactie
# die wordt teruggedraaid, en je ziet per onderdeel hoeveel er weg zou gaan.
#
#   az login --tenant <tenant-id-vereniging>
#   AZURE_SUBSCRIPTION_ID=<id> infra/prod/reset-testdata.sh            # proefrun
#   AZURE_SUBSCRIPTION_ID=<id> infra/prod/reset-testdata.sh --apply    # vastleggen
#   AZURE_SUBSCRIPTION_ID=<id> infra/prod/reset-testdata.sh meldingen [--apply]   # alleen de meldingen (reset-meldingen.sql)
#
# Vereist: lid van sg-dvd-sql-admin-prod (bootstrap-prod.sh). Het script zet tijdelijk een firewallregel voor jouw IP.
set -euo pipefail

: "${AZURE_SUBSCRIPTION_ID:?Zet AZURE_SUBSCRIPTION_ID}"
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
SCRIPT=reset-testdata.sql
if [[ "${1:-}" == "meldingen" ]]; then
  SCRIPT=reset-meldingen.sql
  shift
fi
APPLY=0
if [[ "${1:-}" == "--apply" ]]; then
  APPLY=1
  read -r -p "Testgegevens in PRODUCTIE definitief verwijderen? Typ 'ja': " answer
  [[ "$answer" == "ja" ]] || { echo "Afgebroken."; exit 1; }
fi

az account set --subscription "$AZURE_SUBSCRIPTION_ID"
WORK="$(mktemp -d)"
IP="$(curl -sf https://api.ipify.org)"
RULE="reset-$(date +%Y%m%d%H%M%S)"
cleanup() {
  az sql server firewall-rule delete -g rg-dvd-prod -s sql-dvd-prod -n "$RULE" -o none 2>/dev/null || true
  rm -rf "${WORK:?}"
}
trap cleanup EXIT

az sql server firewall-rule create -g rg-dvd-prod -s sql-dvd-prod -n "$RULE" --start-ip-address "$IP" --end-ip-address "$IP" -o none
sed "s/\$(APPLY)/$APPLY/" "$HERE/$SCRIPT" >"$WORK/reset.sql"
DVD_SQL_ACCESS_TOKEN="$(az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv)" \
  dotnet run --project "$ROOT/tools/Drammers.DbSetup" -- "sql-dvd-prod.database.windows.net" sqldb-dvd migrate "$WORK/reset.sql"
