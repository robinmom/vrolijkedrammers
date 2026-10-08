#!/usr/bin/env bash
# Eenmalig bij de livegang (fase 7, besluit 2026-10-06): alle gegevens van Dev naar Productie.
#   1. database: BACPAC-export uit Dev, import in de (lege) Prod-database;
#   2. opruimen in Prod: de databasegebruiker van Dev en nog niet verwerkte outbox-berichten (die zijn van Dev);
#   3. bestanden: foto's, documenten en content van de Dev-opslag naar de Prod-opslag;
#   4. sleutelring: dezelfde Data Protection-sleutels, opnieuw versleuteld met de Key Vault-sleutel van Prod, zodat Prod
#      de versleutelde gegevens (IBAN's, QR-sleutels, pushtokens, afmeldlinks) kan lezen;
#   5. geheimen: Facebook-, Expo- en Turnstile-sleutel van Key Vault Dev naar Key Vault Prod. De Mollie-sleutel niet:
#      Prod krijgt de live-sleutel (infra/prod/set-secret.sh prod mollie-api-key). Het e-Boekhouden-token ook niet: in Prod
#      is onze eigen database leidend en staat de koppeling uit (besluit 2026-10-06); de nachtelijke sync gaat uit.
#
# Doe vlak vóór dit script de laatste ledensync in Dev (portal → Ledensync), zodat de kopie de actuele leden bevat.
#
# Voorwaarden (runbook docs/runbooks/livegang-productie.md):
#   - bootstrap-prod.sh is uitgevoerd en de Deploy-workflow is voor "prod" gedraaid met "Alleen infrastructuur" aan
#     (de Prod-database bestaat en is nog leeg; de app is nog niet uitgerold);
#   - je bent lid van sg-dvd-sql-admin-dev en sg-dvd-sql-admin-prod (de bootstrapscripts doen dat);
#   - ingelogd in de tenant van de subscription: az login --tenant <tenant-id-vereniging>
#   AZURE_SUBSCRIPTION_ID=<id> infra/prod/migrate-dev-to-prod.sh [--from <stap>]
#
# Met --from 2..5 gaat het script verder bij die stap, bijv. na een fout nadat de database al is gekopieerd (stap 1
# opnieuw doen kan niet in een gevulde database). Alle stappen zijn vanaf 2 veilig te herhalen.
#
# Het script geeft de ingelogde beheerder tijdelijk de benodigde datarollen (opslag en Key Vault) en firewalltoegang,
# en neemt die aan het eind weer in. Het stopt bij de eerste fout; Prod is dan nog niet in gebruik, dus opnieuw beginnen
# kan na het legen van de Prod-database (Deploy-workflow opnieuw met "Alleen infrastructuur" na het verwijderen ervan).
set -euo pipefail

: "${AZURE_SUBSCRIPTION_ID:?Zet AZURE_SUBSCRIPTION_ID}"
FROM=1
if [[ "${1:-}" == "--from" ]]; then
  FROM="${2:?Gebruik: --from <1-5>}"
  [[ "$FROM" =~ ^[1-5]$ ]] || { echo "--from moet 1 t/m 5 zijn" >&2; exit 2; }
fi
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
WORK="$(mktemp -d)"
DB=sqldb-dvd
CONTAINERS=(content photos-original photos-derived parade-documents)
SECRETS=(facebook-page-token expo-push-access-token turnstile-secret-key)

az account set --subscription "$AZURE_SUBSCRIPTION_ID"
ME="$(az ad signed-in-user show --query id -o tsv)"
IP="$(curl -sf https://api.ipify.org)"
RULE="migratie-$(date +%Y%m%d%H%M%S)"
ASSIGNMENTS=()

cleanup() {
  echo "==> Opruimen: firewallregels en tijdelijke rollen"
  for env in dev prod; do
    az sql server firewall-rule delete -g "rg-dvd-$env" -s "sql-dvd-$env" -n "$RULE" -o none 2>/dev/null || true
  done
  for id in "${ASSIGNMENTS[@]}"; do az role assignment delete --ids "$id" -o none 2>/dev/null || true; done
  rm -rf "${WORK:?}"
}
trap cleanup EXIT

grant() { # <rol> <scope>
  local id
  id="$(az role assignment create --assignee-object-id "$ME" --assignee-principal-type User --role "$1" --scope "$2" --query id -o tsv)"
  ASSIGNMENTS+=("$id")
}

echo "==> Tijdelijke toegang voor $ME"
for env in dev prod; do
  az sql server firewall-rule create -g "rg-dvd-$env" -s "sql-dvd-$env" -n "$RULE" --start-ip-address "$IP" --end-ip-address "$IP" -o none
  grant "Storage Blob Data Contributor" "$(az storage account show -g "rg-dvd-$env" -n "stdvd$env" --query id -o tsv)"
  vault_id="$(az keyvault show -g "rg-dvd-$env" -n "kv-dvd-$env" --query id -o tsv)"
  grant "Key Vault Crypto User" "$vault_id"
  grant "Key Vault Secrets Officer" "$vault_id"
done
echo "    (wachten tot de rollen actief zijn)"
sleep 90

token() { az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv; }
step() { (( FROM <= $1 )); }

if step 1; then
echo "==> 1. Database: export Dev → import Prod"
dotnet tool update --global microsoft.sqlpackage >/dev/null
SQLPACKAGE="$HOME/.dotnet/tools/sqlpackage"
"$SQLPACKAGE" /Action:Export /SourceServerName:"tcp:sql-dvd-dev.database.windows.net,1433" /SourceDatabaseName:"$DB" \
  /AccessToken:"$(token)" /TargetFile:"$WORK/dvd.bacpac"
"$SQLPACKAGE" /Action:Import /TargetServerName:"tcp:sql-dvd-prod.database.windows.net,1433" /TargetDatabaseName:"$DB" \
  /AccessToken:"$(token)" /SourceFile:"$WORK/dvd.bacpac"
fi

if step 2; then
echo "==> 2. Opruimen in Prod: databasegebruiker van Dev, openstaande outbox-berichten, nachtelijke ledensync uit"
cat >"$WORK/opruimen.sql" <<'SQL'
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'app-dvd-api-dev')
    DROP USER [app-dvd-api-dev];
GO
DELETE FROM notification.Outbox WHERE processed_at IS NULL;
GO
UPDATE config.FeatureFlag SET enabled = 0 WHERE [key] = 'members-sync';
GO
SQL
# Hetzelfde token als sqlpackage; DefaultAzureCredential liep op een Mac in een time-out.
DVD_SQL_ACCESS_TOKEN="$(token)" dotnet run --project "$ROOT/tools/Drammers.DbSetup" -- \
  "sql-dvd-prod.database.windows.net" "$DB" migrate "$WORK/opruimen.sql"
fi

if step 3; then
echo "==> 3. Bestanden: ${CONTAINERS[*]}"
for container in "${CONTAINERS[@]}"; do
  mkdir -p "$WORK/blobs/$container"
  az storage blob download-batch --account-name stdvddev --source "$container" --destination "$WORK/blobs/$container" \
    --auth-mode login -o none
  if [[ -n "$(ls -A "$WORK/blobs/$container")" ]]; then
    az storage blob upload-batch --account-name stdvdprod --destination "$container" --source "$WORK/blobs/$container" \
      --auth-mode login --overwrite -o none
  fi
  echo "    $container: $(find "$WORK/blobs/$container" -type f | wc -l | tr -d ' ') bestand(en)"
done

fi

if step 4; then
echo "==> 4. Sleutelring opnieuw versleutelen met de Key Vault-sleutel van Prod"
az storage blob download --account-name stdvddev --container-name dataprotection --name keys.xml \
  --file "$WORK/keys-dev.xml" --auth-mode login -o none
dev_key="$(az keyvault key show --vault-name kv-dvd-dev --name dataprotection --query key.kid -o tsv)"
prod_key="$(az keyvault key show --vault-name kv-dvd-prod --name dataprotection --query key.kid -o tsv)"
# Zonder versie, zoals de app hem gebruikt (Azure__DataProtectionKeyUri).
dotnet run --project "$ROOT/tools/Drammers.KeyRing" -- "$WORK/keys-dev.xml" "${dev_key%/*}" "${prod_key%/*}" "$WORK/keys-prod.xml"
az storage blob upload --account-name stdvdprod --container-name dataprotection --name keys.xml \
  --file "$WORK/keys-prod.xml" --auth-mode login --overwrite -o none

fi

if step 5; then
echo "==> 5. Geheimen: ${SECRETS[*]}"
for name in "${SECRETS[@]}"; do
  if value="$(az keyvault secret show --vault-name kv-dvd-dev --name "$name" --query value -o tsv 2>/dev/null)" && [[ -n "$value" ]]; then
    # Via stdin, zodat de waarde niet in de proceslijst staat.
    printf '%s' "$value" | az keyvault secret set --vault-name kv-dvd-prod --name "$name" --file /dev/stdin \
      --content-type "text/plain" -o none
    echo "    $name: gekopieerd"
  else
    echo "    $name: niet in Dev, overgeslagen"
  fi
done
unset value
fi

echo
echo "Klaar. Zet nu de live Mollie-sleutel: infra/prod/set-secret.sh prod mollie-api-key"
echo "Volgende stap: de Deploy-workflow voor \"prod\" zonder \"Alleen infrastructuur\" (migraties, app, smoke-tests)."
