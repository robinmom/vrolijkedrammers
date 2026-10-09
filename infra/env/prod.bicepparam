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
param boundHostNames = filter(split(readEnvironmentVariable('DVD_BOUND_HOSTNAMES', ''), ','), host => !empty(host))
param budgetAmount = 40
param budgetStartDate = '2026-10-01'
param budgetContactEmails = filter(split(readEnvironmentVariable('DVD_BUDGET_EMAIL', ''), ','), email => !empty(email))
