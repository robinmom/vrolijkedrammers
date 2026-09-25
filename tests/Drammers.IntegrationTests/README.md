# Drammers.IntegrationTests

Integratietests tegen een echte SQL Server (Testcontainers; docs/12 §1). Vereist Docker; de container start eenmalig
per testrun, en elke testklasse krijgt een eigen database die met hetzelfde idempotente migratiescript wordt opgebouwd
als in de deploy-pipeline.

Dekking fase 2: migraties (leeg → laatste, idempotent), rechten van `app_runtime` (auditlog niet te wijzigen, geen DDL),
jobcoördinatie over instanties (`sp_getapplock`), outbox (precies één keer, ook na een crash), hash-keten van de
auditlog en de publieke endpoints `/app-config` en `/carnival-years/current`.

Op Apple Silicon draait de SQL Server-image via emulatie; de eerste start duurt daardoor langer.
