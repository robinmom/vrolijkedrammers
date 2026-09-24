# ADR-003: Database-architectuur

- **Status**: Voorgesteld · 2026-09-24

## Context

De opdrachtgever overweegt functionele data en authenticatiegegevens te scheiden (§60). Er zijn transacties nodig die modules overspannen: bijvoorbeeld lid goedkeuren → rol toekennen → audit; submit → nummer → historie → outbox. Security mag nooit uitsluitend afhangen van databasescheiding.

## Options considered

**Optie A — Eén Azure SQL Database** met schema's `identity`, `membership`, `content`, `notification`, `ticketing`, `payments`, `parade`, `import`, `audit`, `config`, `reporting`.

**Optie B — Aparte identity-database en applicatiedatabase.**

| Criterium | A: één DB, schema's | B: twee DB's |
|---|---|---|
| Security | Scheiding via schema-rechten per DB-user (managed identities); audit-schema alleen INSERT/SELECT. Wachtwoorden staan sowieso **niet** in onze DB (Entra External ID, ADR-004), dus het "identity"-schema bevat alleen profiel/rollen/devices | Iets sterkere blast-radius-isolatie, maar de API heeft toegang tot beide nodig → een compromis van de API raakt beide |
| Beheer | 1 set backups, firewall, monitoring, migraties | 2× alles; migraties coördineren |
| Kosten | 1 × S1 (~€ 26) | 2 × (min. Basic/S0) → +€ 5–26/mnd |
| Complexiteit | Laag | Hoger (2 DbContexts, 2 connecties) |
| Transacties | Eén lokale transactie over modules | Geen gedistribueerde transacties in Azure SQL DB (elastic transactions beperkt) → outbox/sagas nodig |
| Disaster recovery | Eén consistente PITR | Twee restores moeten op hetzelfde tijdstip worden hersteld om consistent te zijn |
| Ontwikkeling | Eenvoudig lokaal (1 container) | Meer setup |
| Schaalbaarheid | Ruim voldoende (tot honderden GB, hogere tier mogelijk) | Onafhankelijk schalen (niet nodig) |

## Decision

**Optie A: één Azure SQL Database met een schema per module**, met least-privilege DB-users per identiteit.

## Reasoning

- De grootste winst van B (wachtwoordhashes isoleren) is niet van toepassing, omdat credentials in Entra External ID staan.
- Cross-module-transacties (submit + nummer + historie + outbox; goedkeuring + rol + audit) blijven eenvoudig en consistent.
- Lagere kosten en beheerlast; één consistent herstelpunt.
- Isolatie wordt bereikt met schema-rechten en aparte identiteiten, wat fijnmaziger is dan per database.

## Consequences

- DB-users: `app_runtime` (API + workers, ADR-007), optioneel `app_sync` (user-assigned MI voor de sync-verbinding), `app_migrator` (pipeline, DDL), `app_reporting` (read-only views), `dba_readonly` (mensen).
- Rechten per schema, bijv. `GRANT INSERT, SELECT ON SCHEMA::audit TO app_runtime` (geen UPDATE/DELETE); scan-reconciliatie via een stored procedure met `EXECUTE AS`.
- Als later een extern systeem alleen identity-data nodig heeft, kan het schema worden verplaatst (modulegrenzen maken dat mogelijk).

## Security implications

- Entra-only authenticatie op de SQL-server (SQL-logins uit), managed identities, TDE, TLS.
- Data Discovery & Classification voor PII-kolommen; auditing van de SQL-server naar Log Analytics (Prod).
- Aanvullend: applicatie-autorisatie, netwerkbeperkingen (firewall/optioneel private endpoint), Key Vault; de database-scheiding is **niet** de enige verdedigingslinie.

## Cost implications

- Prod: Standard S1 ~€ 26/mnd (of serverless GP 1 vCore bij piekgebruik); Dev/Acc: gratis Azure SQL-offer.
- Optie B zou ~€ 5–26/mnd extra kosten plus extra beheer.
