// Orchestrator per omgeving (docs/08 §6). Scope: de resource group van de omgeving (rg-dvd-<env>).
// Voorwaarde: de bootstrap (infra/bootstrap) heeft de resource groups, het gedeelde plan en de SQL-beheergroep aangemaakt.
targetScope = 'resourceGroup'

@allowed(['dev', 'prod'])
param environmentName string
param location string = resourceGroup().location

@description('Resource-ID van het App Service-plan (Dev/Acc delen één B1-plan).')
param appServicePlanId string

@description('Entra-groep (tenant van de subscription) die SQL-beheerder is.')
param sqlAdminGroupName string
param sqlAdminGroupObjectId string
param sqlUseFreeOffer bool

@description('Wat de gratis SQL-database doet als het maandtegoed op is (zie modules/sql.bicep).')
@allowed(['AutoPause', 'BillOverUsage'])
param sqlFreeLimitExhaustionBehavior string = 'AutoPause'

param keyVaultPurgeProtection bool
param logDailyQuotaGb int

@description('Entra External ID: authority, client-ID van de API-app-registratie en de vereiste environmentAccess-waarde (leeg in Prod).')
param externalIdAuthority string
param apiClientId string
param environmentAccessClaim string
param requiredEnvironmentAccess string

@description('Client-ID van de portal-app-registratie; het portal haalt deze op via GET /api/v1/portal-config.')
param portalClientId string

@description('Client-ID van de app-registratie "DVD App"; de app haalt deze op via GET /api/v1/app-auth-config (fase 9).')
param mobileClientId string = ''

@description('Doorstuurpagina /app/auth-redirect voor Expo Go; alleen Dev/Acc.')
param mobileRedirectBridge bool = false

@description('Graph in de External ID-tenant: provisioning-app, issuer-domein en het certificaat in Key Vault (provisioning-certificate.sh).')
param externalIdTenantId string
param graphClientId string
param externalIdIssuerDomain string
param graphCertificateName string

@description('Knop "Toegang tot testomgeving" in het portal (Dev/Acc): object-id van de groep Testers en de volledige naam van de extensie environmentAccess (register-provisioning-app.sh).')
param testersGroupId string = ''
param environmentAccessAttribute string = ''

@description('Push (ADR-009): Expo zodra het access token in Key Vault staat (infra/push/set-expo-token.sh), anders Simulated. Leeg (GitHub-variabele niet gezet) = Simulated.')
param pushProvider string = 'Simulated'

@description('Openbare site key van Cloudflare Turnstile (contactformulier, fase 21i); leeg = uit. Het geheim staat in Key Vault (turnstile-secret-key).')
param turnstileSiteKey string = ''

@description('Koppeling met e-Boekhouden (ledensync en nieuwe leden aanmaken). Uit in Prod: onze eigen database is leidend.')
param eBoekhoudenEnabled bool = true

@description('Alleen Dev: alle e-mail naar dit adres (Dev heeft een kopie van de echte leden). Leeg in Prod.')
param emailRedirectTo string = ''

@description('Always On; uit op het gratis F1-plan (Dev na de livegang).')
param alwaysOn bool = true

@description('Eigen domeinen; het eerste is het hoofdadres (website, portal /beheer en API /api). Leeg tot de DNS-records staan.')
param customHostNames array = []

@description('Eigen afzenderdomein voor e-mail (Productie: vrolijkedrammers.nl); leeg = alleen het Azure-domein.')
param emailCustomDomain string = ''

@description('Pas true na de DNS-records en verificatie in ACS (runbook eigen-maildomein); koppelt het domein en zet de eigen afzenders aan.')
param emailCustomDomainVerified bool = false

// Afzenders op het eigen domein, gelijk aan de contactadressen (besluit 2026-09-30); DoNotReply maakt ACS zelf aan.
var emailSenders = {
  secretaris: 'De Vrolijke Drammers – Secretariaat'
  optocht: 'De Vrolijke Drammers – Optocht'
  penningmeester: 'De Vrolijke Drammers – Penningmeester'
  voorzitter: 'De Vrolijke Drammers – Voorzitter'
}

param budgetAmount int
param budgetStartDate string
param budgetContactEmails array

var tags = {
  application: 'de-vrolijke-drammers'
  environment: environmentName
  managedBy: 'bicep'
}

// Openbaar adres: het eigen domein zodra dat gekoppeld is, anders het azurewebsites-adres.
var publicBaseUrl = empty(customHostNames) ? 'https://app-dvd-api-${environmentName}.azurewebsites.net' : 'https://${customHostNames[0]}'

var aspnetEnvironment = {
  dev: 'Dev'
  prod: 'Production'
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    location: location
    environmentName: environmentName
    tags: tags
    dailyQuotaGb: logDailyQuotaGb
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    location: location
    environmentName: environmentName
    tags: tags
    workspaceId: monitoring.outputs.workspaceId
    enablePurgeProtection: keyVaultPurgeProtection
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    environmentName: environmentName
    tags: tags
    workspaceId: monitoring.outputs.workspaceId
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    location: location
    environmentName: environmentName
    tags: tags
    adminGroupName: sqlAdminGroupName
    adminGroupObjectId: sqlAdminGroupObjectId
    useFreeOffer: sqlUseFreeOffer
    freeLimitExhaustionBehavior: sqlFreeLimitExhaustionBehavior
  }
}

module email 'modules/email.bicep' = {
  name: 'email'
  params: {
    environmentName: environmentName
    tags: tags
    customDomain: emailCustomDomain
    customDomainVerified: emailCustomDomainVerified
    senders: emailSenders
  }
}

module api 'modules/appservice.bicep' = {
  name: 'api'
  params: {
    location: location
    environmentName: environmentName
    tags: tags
    workspaceId: monitoring.outputs.workspaceId
    appServicePlanId: appServicePlanId
    alwaysOn: alwaysOn
    customHostNames: customHostNames
    appSettings: {
      ASPNETCORE_ENVIRONMENT: aspnetEnvironment[environmentName]
      // App draait uit het zip-pakket, dat bij een deploy in één keer wordt gewisseld (geen half vervangen DLL's).
      WEBSITE_RUN_FROM_PACKAGE: '1'
      APPLICATIONINSIGHTS_CONNECTION_STRING: monitoring.outputs.appInsightsConnectionString
      ConnectionStrings__Drammers: 'Server=tcp:${sql.outputs.serverFqdn},1433;Database=${sql.outputs.databaseName};Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connect Timeout=60'
      Azure__KeyVaultUri: keyVault.outputs.uri
      Azure__BlobEndpoint: storage.outputs.blobEndpoint
      Azure__DataProtectionKeyUri: keyVault.outputs.dataProtectionKeyUri
      Push__Provider: pushProvider == 'Expo' ? 'Expo' : 'Simulated'
      Email__Endpoint: email.outputs.endpoint
      Email__SenderDomain: email.outputs.senderDomain
      Email__CustomSenderDomain: email.outputs.customSenderDomain
      Email__CustomSenders: join(objectKeys(emailSenders), ',')
      Email__RedirectAllTo: emailRedirectTo
      // Kaartverkoop (fase 19): links in e-mails van de nachtelijke job; de Mollie-sleutel staat in Key Vault (mollie-api-key).
      Sales__PublicBaseUrl: publicBaseUrl
      // Andere eigen domeinen (bijv. zonder www) sturen door naar het hoofdadres.
      Website__CanonicalHost: empty(customHostNames) ? '' : customHostNames[0]
      Auth__Authority: externalIdAuthority
      Auth__Audience: apiClientId
      Auth__EnvironmentAccessClaim: environmentAccessClaim
      Auth__RequiredEnvironmentAccess: requiredEnvironmentAccess
      Portal__ClientId: portalClientId
      Auth__MobileClientId: mobileClientId
      Auth__MobileRedirectBridge: string(mobileRedirectBridge)
      Graph__TenantId: empty(graphClientId) ? '' : externalIdTenantId
      Graph__ClientId: graphClientId
      Graph__IssuerDomain: externalIdIssuerDomain
      Graph__CertificateName: graphCertificateName
      Graph__TestersGroupId: testersGroupId
      Graph__EnvironmentAccessAttribute: environmentAccessAttribute
      Turnstile__SiteKey: turnstileSiteKey
      EBoekhouden__Enabled: string(eBoekhoudenEnabled)
    }
  }
}

// Client-ID van de API-identiteit; nodig om de databasegebruiker aan te maken (tools/Drammers.DbSetup).
module apiIdentity 'modules/site-identity.bicep' = {
  name: 'api-identity'
  params: {
    siteName: api.outputs.name
  }
}

module sqlFirewall 'modules/sql-firewall.bicep' = {
  name: 'sql-firewall'
  params: {
    sqlServerName: sql.outputs.serverName
    ipAddresses: api.outputs.possibleOutboundIpAddresses
  }
}

module apiRoles 'modules/role-assignments.bicep' = {
  name: 'api-roles'
  params: {
    principalId: api.outputs.principalId
    keyVaultName: keyVault.outputs.name
    storageAccountName: storage.outputs.name
    communicationServiceName: email.outputs.communicationServiceName
  }
}


module budget 'modules/budget.bicep' = if (!empty(budgetContactEmails)) {
  name: 'budget'
  params: {
    environmentName: environmentName
    amount: budgetAmount
    startDate: budgetStartDate
    contactEmails: budgetContactEmails
  }
}

output apiAppName string = api.outputs.name
output apiIdentityClientId string = apiIdentity.outputs.clientId
output apiUrl string = 'https://${api.outputs.defaultHostName}'
// Het beheerportal wordt door de API-app geserveerd (OQ-76: alles in de EU).
output portalUrl string = 'https://${api.outputs.defaultHostName}/beheer/'
output sqlServerFqdn string = sql.outputs.serverFqdn
output keyVaultName string = keyVault.outputs.name
output storageAccountName string = storage.outputs.name
output publicBaseUrl string = publicBaseUrl
@description('Waarde voor het TXT-record asuid.<domein> bij het koppelen van een eigen domein.')
output customDomainVerificationId string = api.outputs.customDomainVerificationId
output emailDomainRecords object = email.outputs.customDomainRecords
output emailCustomSenderDomain string = email.outputs.customSenderDomain
