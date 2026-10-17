// Parameters voor Productie (fase 7). Niet-gevoelige ID's komen uit omgevingsvariabelen
// (GitHub environment "prod") en staan niet in de publieke repo.
using '../main.bicep'

param environmentName = 'prod'
// Eigen B1-plan in rg-dvd-prod (infra/bootstrap/bootstrap-prod.sh); niet gedeeld met Dev/Acc.
param appServicePlanId = readEnvironmentVariable('DVD_APP_SERVICE_PLAN_ID')
param sqlAdminGroupName = 'sg-dvd-sql-admin-prod'
param sqlAdminGroupObjectId = readEnvironmentVariable('DVD_SQL_ADMIN_GROUP_OBJECT_ID')
param sqlUseFreeOffer = true
param sqlFreeLimitExhaustionBehavior = 'BillOverUsage'
// 2026-10-17: Standard S1 (20 DTU, ± € 25 per maand) in plaats van serverless, dat door de achtergrondtaken de hele dag
// wakker is (± € 200 per maand). Bij drukte (11-11, kaartverkoop) kan S2 met één regel.
param sqlSkuName = 'S1'
param keyVaultPurgeProtection = true
param logDailyQuotaGb = 1
param externalIdAuthority = 'https://vrolijkedrammersapp.ciamlogin.com/260db5a1-e5b6-4388-9f6c-d9b02cb5578b/v2.0'
param apiClientId = readEnvironmentVariable('DVD_API_CLIENT_ID', '')
// Productie: geen testomgeving-claim, iedereen met een goedgekeurd account mag erin.
param environmentAccessClaim = ''
param requiredEnvironmentAccess = ''
param externalIdTenantId = '260db5a1-e5b6-4388-9f6c-d9b02cb5578b'
param graphClientId = readEnvironmentVariable('DVD_GRAPH_CLIENT_ID', '')
param pushProvider = readEnvironmentVariable('DVD_PUSH_PROVIDER', 'Simulated')
param turnstileSiteKey = readEnvironmentVariable('DVD_TURNSTILE_SITE_KEY', '')
param externalIdIssuerDomain = 'vrolijkedrammersapp.onmicrosoft.com'
param graphCertificateName = readEnvironmentVariable('DVD_GRAPH_CERTIFICATE_NAME', '')
param portalClientId = readEnvironmentVariable('DVD_PORTAL_CLIENT_ID', '')
param mobileClientId = readEnvironmentVariable('DVD_MOBILE_CLIENT_ID', '')
param mobileRedirectBridge = false
// Na de laatste sync in Dev (vóór de kopie) is onze eigen database leidend: geen koppeling met e-Boekhouden meer.
param eBoekhoudenEnabled = false
// Eerst leeg; na de DNS-records: "www.vrolijkedrammers.nl,vrolijkedrammers.nl" (het eerste is het hoofdadres).
param customHostNames = filter(split(readEnvironmentVariable('DVD_CUSTOM_HOSTNAMES', ''), ','), host => !empty(host))
// Eigen maildomein (runbook eigen-maildomein): eerst aanmaken en DNS zetten, na de verificatie DVD_EMAIL_DOMAIN_VERIFIED=true.
param emailCustomDomain = 'vrolijkedrammers.nl'
param emailCustomDomainVerified = readEnvironmentVariable('DVD_EMAIL_DOMAIN_VERIFIED', 'false') == 'true'
// Mailings via de mailbox in Microsoft 365 (runbook mailing-via-mailbox): na infra/m365/mailbox-verzenden.ps1 DVD_EMAIL_MAILBOX_SENDERS=voorzitter,secretaris.
param emailMailboxSenders = readEnvironmentVariable('DVD_EMAIL_MAILBOX_SENDERS', '')
param boundHostNames = filter(split(readEnvironmentVariable('DVD_BOUND_HOSTNAMES', ''), ','), host => !empty(host))
param budgetAmount = 40
param budgetStartDate = '2026-10-01'
param budgetContactEmails = filter(split(readEnvironmentVariable('DVD_BUDGET_EMAIL', ''), ','), email => !empty(email))
// Inloggen met wachtwoord (runbook accounts-wachtwoord): na infra/entra/wachtwoord-inloggen.sh DVD_PASSWORD_SIGN_IN=true.
param passwordSignIn = readEnvironmentVariable('DVD_PASSWORD_SIGN_IN', 'false') == 'true'
// Malwarescan met Defender for Storage (OQ-65, alleen Prod; Dev/Acc blijven zonder vanwege de kosten). Pas aan
// (DVD_MALWARE_SCANNING=true) nadat de bootstrap opnieuw is gedraaid: de pipeline moet dan ook Storage Blob Data Owner
// mogen toewijzen (infra/bootstrap/environment-access.bicep), anders faalt de deploy op die roltoewijzing.
param malwareScanning = readEnvironmentVariable('DVD_MALWARE_SCANNING', 'false') == 'true'
