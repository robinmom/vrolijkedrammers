// Parameters voor Acceptance. Niet-gevoelige ID's komen uit omgevingsvariabelen
// (GitHub environment variables of lokaal geëxporteerd) en staan niet in de publieke repo.
using '../main.bicep'

param environmentName = 'acc'
param appServicePlanId = readEnvironmentVariable('DVD_APP_SERVICE_PLAN_ID')
param sqlAdminGroupName = 'sg-dvd-sql-admin-acc'
param sqlAdminGroupObjectId = readEnvironmentVariable('DVD_SQL_ADMIN_GROUP_OBJECT_ID')
param sqlUseFreeOffer = true
param keyVaultPurgeProtection = true
param logDailyQuotaGb = 1
param externalIdAuthority = 'https://vrolijkedrammersapp.ciamlogin.com/260db5a1-e5b6-4388-9f6c-d9b02cb5578b/v2.0'
param apiClientId = readEnvironmentVariable('DVD_API_CLIENT_ID', '')
param environmentAccessClaim = readEnvironmentVariable('DVD_ENVIRONMENT_ACCESS_CLAIM', '')
param externalIdTenantId = '260db5a1-e5b6-4388-9f6c-d9b02cb5578b'
param graphClientId = readEnvironmentVariable('DVD_GRAPH_CLIENT_ID', '')
param externalIdIssuerDomain = 'vrolijkedrammersapp.onmicrosoft.com'
param graphCertificateName = readEnvironmentVariable('DVD_GRAPH_CERTIFICATE_NAME', '')
param requiredEnvironmentAccess = 'acc'
param budgetAmount = 35
param budgetStartDate = '2026-09-01'
param budgetContactEmails = filter(split(readEnvironmentVariable('DVD_BUDGET_EMAIL', ''), ','), email => !empty(email))
