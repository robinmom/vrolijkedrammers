// Parameters voor Development. Niet-gevoelige ID's komen uit omgevingsvariabelen
// (GitHub environment variables of lokaal geëxporteerd) en staan niet in de publieke repo.
using '../main.bicep'

param environmentName = 'dev'
param appServicePlanId = readEnvironmentVariable('DVD_APP_SERVICE_PLAN_ID')
param sqlAdminGroupName = 'sg-dvd-sql-admin-dev'
param sqlAdminGroupObjectId = readEnvironmentVariable('DVD_SQL_ADMIN_GROUP_OBJECT_ID')
param sqlUseFreeOffer = true
// Besluit 2026-09-27: bij een opgebruikt tegoed doorgaan tegen betaling in plaats van een maand pauzeren (Dev bleef
// anders tot de 1e onbereikbaar). Sinds de worker de database bij rust met rust laat, pauzeert hij zelf en zijn de
// kosten beperkt tot echt gebruik boven het gratis tegoed.
param sqlFreeLimitExhaustionBehavior = 'BillOverUsage'
// 2026-10-17: Basic (5 DTU, tot 2 GB, ± € 4 per maand) in plaats van serverless (± € 55 per maand); Dev is 80 MB.
param sqlSkuName = 'Basic'
param keyVaultPurgeProtection = false
param logDailyQuotaGb = 1
// Eigen External ID-tenant voor Dev (beveiligingsonderzoek H1, 2026-10-17; docs/runbooks/dev-tenant.md). Zolang de
// GitHub-variabelen leeg zijn, blijft Dev op de gedeelde tenant. De workflow zet de variabelen altijd (ook leeg), dus een
// lege waarde moet net als een ontbrekende variabele terugvallen op de gedeelde tenant — anders wordt de authority
// 'https://.ciamlogin.com//v2.0' en kan niemand inloggen.
var externalIdTenantNameRaw = readEnvironmentVariable('DVD_EXTERNAL_ID_TENANT_NAME', 'vrolijkedrammersapp')
var externalIdTenantName = empty(externalIdTenantNameRaw) ? 'vrolijkedrammersapp' : externalIdTenantNameRaw
var externalIdTenantRaw = readEnvironmentVariable('DVD_EXTERNAL_ID_TENANT_ID', '260db5a1-e5b6-4388-9f6c-d9b02cb5578b')
var externalIdTenant = empty(externalIdTenantRaw) ? '260db5a1-e5b6-4388-9f6c-d9b02cb5578b' : externalIdTenantRaw
param externalIdAuthority = 'https://${externalIdTenantName}.ciamlogin.com/${externalIdTenant}/v2.0'
param externalIdTenantId = externalIdTenant
param externalIdIssuerDomain = '${externalIdTenantName}.onmicrosoft.com'
param apiClientId = readEnvironmentVariable('DVD_API_CLIENT_ID', '')
param environmentAccessClaim = readEnvironmentVariable('DVD_ENVIRONMENT_ACCESS_CLAIM', '')
param graphClientId = readEnvironmentVariable('DVD_GRAPH_CLIENT_ID', '')
param testersGroupId = readEnvironmentVariable('DVD_TESTERS_GROUP_ID', '')
param pushProvider = readEnvironmentVariable('DVD_PUSH_PROVIDER', 'Simulated')
param turnstileSiteKey = readEnvironmentVariable('DVD_TURNSTILE_SITE_KEY', '')
param environmentAccessAttribute = readEnvironmentVariable('DVD_ENVIRONMENT_ACCESS_ATTRIBUTE', '')
param graphCertificateName = readEnvironmentVariable('DVD_GRAPH_CERTIFICATE_NAME', '')
param portalClientId = readEnvironmentVariable('DVD_PORTAL_CLIENT_ID', '')
param mobileClientId = readEnvironmentVariable('DVD_MOBILE_CLIENT_ID', '')
param mobileRedirectBridge = true
param requiredEnvironmentAccess = 'dev'
param budgetAmount = 25
param budgetStartDate = '2026-09-01'
param budgetContactEmails = filter(split(readEnvironmentVariable('DVD_BUDGET_EMAIL', ''), ','), email => !empty(email))
// Na de livegang staat Dev op het gratis F1-plan zonder Always On (GitHub-variabele DVD_ALWAYS_ON=false).
param alwaysOn = readEnvironmentVariable('DVD_ALWAYS_ON', 'true') == 'true'
param customHostNames = filter(split(readEnvironmentVariable('DVD_CUSTOM_HOSTNAMES', ''), ','), host => !empty(host))
param boundHostNames = filter(split(readEnvironmentVariable('DVD_BOUND_HOSTNAMES', ''), ','), host => !empty(host))
// Dev heeft na de livegang een kopie van de echte leden: alle e-mail gaat naar het testadres (besluit 2026-10-07).
param emailRedirectTo = 'app@vrolijkedrammers.nl'
// Inloggen met wachtwoord (runbook accounts-wachtwoord): na infra/entra/wachtwoord-inloggen.sh DVD_PASSWORD_SIGN_IN=true.
param passwordSignIn = readEnvironmentVariable('DVD_PASSWORD_SIGN_IN', 'false') == 'true'
// 2026-10-17: Dev heeft testgegevens (infra/dev/anonimiseer-dev.sh); de koppeling met de echte administratie staat uit.
param eBoekhoudenEnabled = false
